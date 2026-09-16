using System;
using System.Text.Json;
using System.Linq;
using System.IO;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Runtime.Particles;

public static class ParticleSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        _pass = 0; _fail = 0;
        Console.WriteLine("=== particle self-test ===");

        TestValueSemantics();
        TestCurve();
        TestShape();
        TestDeterminism();
        TestCap();
        TestPoolReuse();
        TestBurst();
        TestRateAccumulator();
        TestLifetimeKill();
        TestSwayDoesNotDrift();
        TestEmittingToggle();
        TestFollowEmitter();
        TestSpawnSeeds();
        TestSortYOffset();
        TestAdditiveSplit();
        TestPrewarm();
        TestPresetRoundTrip();
        TestSceneRoundTrip();
        TestUnresolvedPresetIsPreserved();
        TestAtmosphereRouting();
        TestDrawPositionSnap();
        TestSnapSwitchRoundTrip();
        TestNoiseField();
        TestCurlField();
        TestVelocityNoise();
        TestBuiltinTexture();

        Console.WriteLine($"=== Particles: {_pass} passed, {_fail} failed ===");
    }

    private static void TestValueSemantics()
    {
        var c = ParticleValue.Constant(7f);
        Check("constant: independent of seed", Near(c.Evaluate(0f, 0f), 7f) && Near(c.Evaluate(1f, 0f), 7f));
        Check("constant: independent of lifetime", Near(c.Evaluate(0.5f, 0f), 7f) && Near(c.Evaluate(0.5f, 1f), 7f));

        var r = ParticleValue.Range(10f, 20f);
        Check("* range: the seed picks the value (0 gives min, 1 gives max, 0.5 the middle)",
            Near(r.Evaluate(0f, 0f), 10f) && Near(r.Evaluate(1f, 0f), 20f) && Near(r.Evaluate(0.5f, 0f), 15f),
            $"{r.Evaluate(0f, 0f)}/{r.Evaluate(0.5f, 0f)}/{r.Evaluate(1f, 0f)}");
        Check("* range: with no curve it holds for the whole lifetime (delta cannot change it)",
            Near(r.Evaluate(0.3f, 0f), r.Evaluate(0.3f, 1f)));

        var cv = ParticleValue.Curved(0f, 1f, 0f);
        Check("* curve only: independent of seed (min==max, so the seed cannot change it)",
            Near(cv.Evaluate(0f, 0.5f), cv.Evaluate(1f, 0.5f)));
        Check("curve only: both ends and the middle", Near(cv.Evaluate(0f, 0f), 0f) && Near(cv.Evaluate(0f, 0.5f), 1f)
              && Near(cv.Evaluate(0f, 1f), 0f));

        var rc = ParticleValue.RangeCurved(0.7f, 1.3f, 1f, 0f);
        Check("** per-particle times curve: the seed picks the size (0.7 / 1.3 at t=0)",
            Near(rc.Evaluate(0f, 0f), 0.7f) && Near(rc.Evaluate(1f, 0f), 1.3f),
            $"{rc.Evaluate(0f, 0f):F2} / {rc.Evaluate(1f, 0f):F2}");
        Check("** per-particle times curve: the curve shrinks it (same seed, later t, half the value)",
            Near(rc.Evaluate(1f, 0.5f), 0.65f), $"{rc.Evaluate(1f, 0.5f):F3}");
        Check("** per-particle times curve: both reach 0 at the end", Near(rc.Evaluate(0f, 1f), 0f) && Near(rc.Evaluate(1f, 1f), 0f));
        Check("** the two axes are independent (changing seed alone, or t alone, changes the value)",
            !Near(rc.Evaluate(0f, 0.5f), rc.Evaluate(1f, 0.5f)) &&
            !Near(rc.Evaluate(0.5f, 0.2f), rc.Evaluate(0.5f, 0.8f)));

        Check("t outside the range is clamped",
            Near(cv.Evaluate(0f, -1f), 0f) && Near(cv.Evaluate(0f, 2f), 0f));

        var rng = new Random(1234);
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < 200; i++)
        {
            float v = r.SampleAtSpawn(rng);
            if (v < min) min = v;
            if (v > max) max = v;
        }
        Check("[settled at spawn] within the range", min >= 10f && max <= 20f, $"{min:F2}~{max:F2}");
        Check("* [settled at spawn] varies per particle (a degenerate constant is caught here)", max - min > 5f, $"spread {max - min:F2}");
        Check("[settled at spawn] with a curve, the value at 0 is multiplied in",
            Near(ParticleValue.Curved(2f, 9f).SampleAtSpawn(new Random(1)), 2f));
    }

    private static void TestCurve()
    {
        var curve = ParticleValue.Curved(0f, 1f, 0f);

        Check("curve: both end points are exact", Near(curve.Evaluate(0f, 0f), 0f) && Near(curve.Evaluate(0f, 1f), 0f));
        Check("* curve: the middle point sits at 0.5", Near(curve.Evaluate(0f, 0.5f), 1f));
        Check("* curve: linear interpolation between points", Near(curve.Evaluate(0f, 0.25f), 0.5f), $"{curve.Evaluate(0f, 0.25f):F3}");
        Check("curve: four points are evenly spaced too", Near(ParticleValue.Curved(0f, 1f, 2f, 3f).Evaluate(0f, 1f / 3f), 1f));

        var empty = new ParticleValue { Min = 3f, Max = 3f, Curve = null };
        Check("* no curve means times 1 (it does not collapse to 0)", Near(empty.Evaluate(0f, 0.5f), 3f));
        var emptyArr = new ParticleValue { Min = 3f, Max = 3f, Curve = Array.Empty<float>() };
        Check("* an empty curve array is times 1 as well", Near(emptyArr.Evaluate(0f, 0.5f), 3f));
        Check("a single-point curve multiplies by that value",
            Near(new ParticleValue { Min = 4f, Max = 4f, Curve = new[] { 0.5f } }.Evaluate(0f, 0.7f), 2f));
    }

    private static void TestShape()
    {
        var rng = new Random(7);

        Check("the point shape is always the origin", ParticleShapeAlwaysZero(rng));

        var box = EmitterShape.Box(40f, 20f);
        float bx = 0f, by = 0f;
        for (int i = 0; i < 500; i++)
        {
            var p = box.RandomPointIn(rng);
            bx = MathF.Max(bx, MathF.Abs(p.X));
            by = MathF.Max(by, MathF.Abs(p.Y));
        }
        Check("* the box is centred on the entity, plus or minus half (a top-left origin is caught here)",
            bx <= 20.001f && by <= 10.001f && bx > 15f && by > 7f, $"±{bx:F1}, ±{by:F1}");

        var circle = EmitterShape.Circle(100f);
        int outer = 0, total = 4000;
        for (int i = 0; i < total; i++)
            if (circle.RandomPointIn(rng).Length() > 50f) outer++;
        float ratio = (float)outer / total;
        Check("* the circle is uniform by area (dropping the sqrt pushes this down to 0.5)",
            ratio > 0.70f && ratio < 0.80f, $"outer-half ratio {ratio:F3} (expected 0.75)");
    }

    private static bool ParticleShapeAlwaysZero(Random rng)
    {
        var point = EmitterShape.Point();
        for (int i = 0; i < 20; i++)
            if (point.RandomPointIn(rng) != Vector2.Zero) return false;
        return true;
    }

    private static void TestDeterminism()
    {
        var preset = SnowLike();

        var a = RunFrames(new ParticleSimulation(42), preset, 120);
        var b = RunFrames(new ParticleSimulation(42), preset, 120);
        var c = RunFrames(new ParticleSimulation(43), preset, 120);

        Check("* the same seed gives the same result (count, positions and lifetimes)", a == b, $"{a}\n           vs {b}");
        Check("* a different seed gives a different result (the seed is really used)", a != c);

        var sim = new ParticleSimulation(42);
        RunFrames(sim, preset, 60);
        sim.Reseed(42);
        Check("Reseed rewinds the sequence to the start", sim.AliveCount == 0 && RunFrames(sim, preset, 120) == a);
    }

    private static string RunFrames(ParticleSimulation sim, ParticlePreset preset, int frames)
    {
        for (int i = 0; i < frames; i++)
            sim.Update(1f / 60f, new Vector2(100f, 0f), preset, emitting: true);

        var alive = sim.Alive;
        double sx = 0, sy = 0, sl = 0;
        for (int i = 0; i < alive.Length; i++)
        {
            sx += alive[i].RenderPosition.X; sy += alive[i].RenderPosition.Y; sl += alive[i].Lifetime;
        }
        return $"n={alive.Length} x={sx:F4} y={sy:F4} life={sl:F4}";
    }

    private static void TestCap()
    {
        var preset = SnowLike();
        preset.MaxParticles = 10;

        var sim = new ParticleSimulation(1);
        int made = sim.Emit(100, Vector2.Zero, preset);

        Check("* never spawns past the cap", sim.AliveCount == 10 && made == 10, $"alive {sim.AliveCount}, made {made}");

        preset.MaxParticles = 50;
        var sim2 = new ParticleSimulation(1);
        int made2 = sim2.Emit(100, Vector2.Zero, preset);
        Check("* raising the cap admits that many more (so returning a constant 10 would not pass)",
            sim2.AliveCount == 50 && made2 == 50, $"alive {sim2.AliveCount}");

        var sim3 = new ParticleSimulation(1);
        preset.MaxParticles = 8;
        preset.Rate = 6000f;
        preset.Lifetime = ParticleValue.Constant(999f);
        for (int i = 0; i < 30; i++) sim3.Update(1f / 60f, Vector2.Zero, preset, emitting: true);
        Check("* continuous emission respects the cap too (a different path from Emit)", sim3.AliveCount == 8, $"{sim3.AliveCount}");
    }

    private static void TestPoolReuse()
    {
        var preset = SnowLike();
        preset.MaxParticles = 64;
        preset.Lifetime = ParticleValue.Range(0.1f, 0.3f);
        preset.Rate = 400f;

        var sim = new ParticleSimulation(9);
        sim.Update(1f / 60f, Vector2.Zero, preset, emitting: true);
        int afterWarmup = sim.PoolAllocations;
        Check("premise: warm-up allocated the pool once (0 would make the rest vacuous)", afterWarmup == 1, $"{afterWarmup}");

        for (int i = 0; i < 600; i++) sim.Update(1f / 60f, Vector2.Zero, preset, emitting: true);
        Check("* no new allocation over 600 frames after warm-up (the pool is reused)",
            sim.PoolAllocations == 1, $"{sim.PoolAllocations} allocations");
        Check("premise: particles really ran during that time (0 would make the above vacuous)",
            sim.AliveCount > 0 && sim.Capacity == 64, $"alive {sim.AliveCount}, pool {sim.Capacity}");

        preset.MaxParticles = 128;
        sim.Update(1f / 60f, Vector2.Zero, preset, emitting: true);
        Check("* growing the cap reallocates exactly once (the counter is not dead)",
            sim.PoolAllocations == 2 && sim.Capacity == 128, $"{sim.PoolAllocations} allocations, pool {sim.Capacity}");

        preset.MaxParticles = 16;
        sim.Update(1f / 60f, Vector2.Zero, preset, emitting: true);
        Check("shrinking the cap does not reallocate", sim.PoolAllocations == 2, $"{sim.PoolAllocations}");
    }

    private static void TestBurst()
    {
        var preset = PuffLike();

        preset.BurstMin = 12; preset.BurstMax = 12;
        var sim = new ParticleSimulation(3);
        Check("burst: min==max gives exactly that count", sim.Burst(Vector2.Zero, preset) == 12);

        preset.BurstMin = 5; preset.BurstMax = 9;
        int lo = int.MaxValue, hi = int.MinValue;
        for (int i = 0; i < 200; i++)
        {
            var s = new ParticleSimulation(100 + i);
            int n = s.Burst(Vector2.Zero, preset);
            lo = Math.Min(lo, n); hi = Math.Max(hi, n);
        }
        Check("burst: within the range", lo >= 5 && hi <= 9, $"{lo}~{hi}");
        Check("* burst: both ends are inclusive (catches the exclusive-upper-bound mistake)", lo == 5 && hi == 9, $"{lo}~{hi}");

        preset.MaxParticles = 4;
        preset.BurstMin = preset.BurstMax = 20;
        var capped = new ParticleSimulation(3);
        Check("* bursts respect the cap too", capped.Burst(Vector2.Zero, preset) == 4, $"{capped.AliveCount}");

        var quiet = PuffLike();
        var qs = new ParticleSimulation(3);
        for (int i = 0; i < 60; i++) qs.Update(1f / 60f, Vector2.Zero, quiet, emitting: true);
        Check("* rate 0 means no continuous emission (a burst-only preset)", qs.AliveCount == 0, $"{qs.AliveCount}");
    }

    private static void TestRateAccumulator()
    {
        var preset = SnowLike();
        preset.Rate = 30f;
        preset.MaxParticles = 1000;
        preset.Lifetime = ParticleValue.Constant(999f);

        var sim = new ParticleSimulation(5);
        for (int i = 0; i < 60; i++) sim.Update(1f / 60f, Vector2.Zero, preset, emitting: true);
        Check("* rate 30 over one second gives 30", sim.AliveCount == 30, $"{sim.AliveCount}");

        preset.Rate = 300f;
        var fast = new ParticleSimulation(5);
        for (int i = 0; i < 60; i++) fast.Update(1f / 60f, Vector2.Zero, preset, emitting: true);
        Check("* a rate above the frame rate still emits fully (a single if would cap it at 60)",
            fast.AliveCount == 300, $"{fast.AliveCount}");

        preset.Rate = 0.5f;
        var slow = new ParticleSimulation(5);
        for (int i = 0; i < 300; i++) slow.Update(1f / 60f, Vector2.Zero, preset, emitting: true);
        Check("* rate 0.5 over five seconds gives 2 (a fractional rate is not lost)", slow.AliveCount == 2, $"{slow.AliveCount}");

        preset.Rate = 60f;
        var scaled = new ParticleSimulation(5);
        for (int i = 0; i < 60; i++) scaled.Update(1f / 60f, Vector2.Zero, preset, emitting: true, rateScale: 0.5f);
        Check("* RateScale is multiplied in", scaled.AliveCount == 30, $"{scaled.AliveCount}");
    }

    private static void TestLifetimeKill()
    {
        var preset = SnowLike();
        preset.Rate = 0f;
        preset.MaxParticles = 100;
        preset.Lifetime = ParticleValue.Constant(1f);

        var sim = new ParticleSimulation(11);
        sim.Emit(10, Vector2.Zero, preset);
        Check("premise: ten were born", sim.AliveCount == 10);

        for (int i = 0; i < 59; i++) sim.Update(1f / 60f, Vector2.Zero, preset, emitting: false);
        Check("* nothing dies before its lifetime", sim.AliveCount == 10, $"{sim.AliveCount}");

        for (int i = 0; i < 3; i++) sim.Update(1f / 60f, Vector2.Zero, preset, emitting: false);
        Check("* they die once the lifetime passes (the reaper is alive)", sim.AliveCount == 0, $"{sim.AliveCount}");

        preset.Lifetime = ParticleValue.Range(0.2f, 1.0f);
        var mixed = new ParticleSimulation(12);
        mixed.Emit(50, Vector2.Zero, preset);
        int prev = mixed.AliveCount;
        bool monotonic = true;
        for (int i = 0; i < 70; i++)
        {
            mixed.Update(1f / 60f, Vector2.Zero, preset, emitting: false);
            if (mixed.AliveCount > prev) monotonic = false;
            prev = mixed.AliveCount;
        }
        Check("* with emission off the count decreases monotonically (swap-remove does not drop a live one)",
            monotonic && mixed.AliveCount == 0, $"{mixed.AliveCount} left");
    }

    private static void TestSwayDoesNotDrift()
    {
        var preset = SnowLike();
        preset.Rate = 0f;
        preset.Speed = ParticleValue.Constant(0f);
        preset.GravityX = 0f; preset.GravityY = 0f;
        preset.SwayAmplitude = 10f;
        preset.SwayFrequency = 1f;
        preset.Lifetime = ParticleValue.Constant(999f);
        preset.MaxParticles = 4;
        preset.Shape = EmitterShape.Point();

        var sim = new ParticleSimulation(21);
        sim.Emit(4, new Vector2(500f, 0f), preset);

        float maxSeen = 0f, minSeen = 0f;
        for (int i = 0; i < 600; i++)
        {
            sim.Update(1f / 60f, Vector2.Zero, preset, emitting: false);
            foreach (var p in sim.Alive)
            {
                float dx = p.RenderPosition.X - p.Position.X;
                maxSeen = MathF.Max(maxSeen, dx);
                minSeen = MathF.Min(minSeen, dx);
            }
        }

        Check("* the sway amplitude stays within the configured value", maxSeen <= 10.001f && minSeen >= -10.001f, $"{minSeen:F2}~{maxSeen:F2}");
        Check("* the sway goes both ways (one-sided means it accumulated)",
            maxSeen > 8f && minSeen < -8f, $"{minSeen:F2}~{maxSeen:F2}");

        bool stayed = true;
        foreach (var p in sim.Alive) if (MathF.Abs(p.Position.X - 500f) > 0.001f) stayed = false;
        Check("* sway does not touch the integrated position (accumulating into Position would drift)", stayed);
    }

    private static void TestEmittingToggle()
    {
        var preset = SnowLike();
        preset.Rate = 60f;
        preset.MaxParticles = 500;
        preset.Lifetime = ParticleValue.Constant(2f);

        var sim = new ParticleSimulation(31);
        for (int i = 0; i < 60; i++) sim.Update(1f / 60f, Vector2.Zero, preset, emitting: true);
        int before = sim.AliveCount;
        Check("premise: they accumulate while emitting", before == 60, $"{before}");

        for (int i = 0; i < 30; i++) sim.Update(1f / 60f, Vector2.Zero, preset, emitting: false);
        Check("* turning it off spawns nothing new", sim.AliveCount <= before, $"{sim.AliveCount}");
        Check("* turning it off does not erase what exists (snow vanishing wholesale reads as a bug)",
            sim.AliveCount == before, $"{sim.AliveCount} (expected {before})");

        sim.Clear();
        Check("Clear removes everything at once", sim.AliveCount == 0);
    }

    private static void TestSpawnSeeds()
    {
        var preset = SnowLike();
        preset.Rate = 0f;
        preset.MaxParticles = 200;
        preset.Lifetime = ParticleValue.Constant(999f);

        var sim = new ParticleSimulation(77);
        sim.Emit(200, Vector2.Zero, preset);
        Check("premise: 200 were born (otherwise the rest is vacuous)", sim.AliveCount == 200, $"{sim.AliveCount}");

        float sMin = 1f, sMax = 0f, aMin = 1f, aMax = 0f;
        int differ = 0;
        foreach (var p in sim.Alive)
        {
            sMin = MathF.Min(sMin, p.ScaleSeed); sMax = MathF.Max(sMax, p.ScaleSeed);
            aMin = MathF.Min(aMin, p.AlphaSeed); aMax = MathF.Max(aMax, p.AlphaSeed);
            if (MathF.Abs(p.ScaleSeed - p.AlphaSeed) > 0.001f) differ++;
        }

        Check("spawn seeds lie in 0..1", sMin >= 0f && sMax <= 1f && aMin >= 0f && aMax <= 1f);
        Check("** scale seeds vary (degenerating to a constant makes every flake the same size)",
            sMax - sMin > 0.8f, $"spread {sMax - sMin:F3}");
        Check("** alpha seeds vary too", aMax - aMin > 0.8f, $"spread {aMax - aMin:F3}");
        Check("** scale and alpha seeds are independent (sharing them makes big flakes necessarily dense)",
            differ >= 195, $"{differ}/200 differ");
    }

    private static void TestFollowEmitter()
    {
        var preset = SnowLike();
        preset.Rate = 0f;
        preset.Speed = ParticleValue.Constant(0f);
        preset.GravityY = 0f;
        preset.SwayAmplitude = 0f;
        preset.Shape = EmitterShape.Point();
        preset.Lifetime = ParticleValue.Constant(999f);
        preset.MaxParticles = 8;

        var world = new ParticleSimulation(41);
        world.Update(1f / 60f, new Vector2(100f, 0f), preset, emitting: false);
        world.Emit(4, new Vector2(100f, 0f), preset);
        world.Update(1f / 60f, new Vector2(150f, 0f), preset, emitting: false);
        Check("* with follow off the particles stay put as the emitter moves (snow belongs to the world)",
            Near(world.Alive[0].Position.X, 100f), $"{world.Alive[0].Position.X:F1}");

        preset.FollowEmitter = true;
        var local = new ParticleSimulation(41);
        local.Update(1f / 60f, new Vector2(100f, 0f), preset, emitting: false);
        local.Emit(4, new Vector2(100f, 0f), preset);
        local.Update(1f / 60f, new Vector2(150f, 0f), preset, emitting: false);
        Check("* with follow on they track the emitter (dust under walking feet)",
            Near(local.Alive[0].Position.X, 150f), $"{local.Alive[0].Position.X:F1}");

        var fresh = new ParticleSimulation(41);
        fresh.Emit(4, new Vector2(500f, 0f), preset);
        fresh.Update(1f / 60f, new Vector2(500f, 0f), preset, emitting: false);
        Check("* nothing is shoved on the first frame (no delta may be measured without a reference point)",
            Near(fresh.Alive[0].Position.X, 500f), $"{fresh.Alive[0].Position.X:F1}");

        local.Clear();
        local.Emit(2, new Vector2(20f, 0f), preset);
        local.Update(1f / 60f, new Vector2(20f, 0f), preset, emitting: false);
        Check("* Clear drops the follow reference too (so the first delta after a room change is not a whole room)",
            Near(local.Alive[0].Position.X, 20f), $"{local.Alive[0].Position.X:F1}");
    }

    private static void TestSortYOffset()
    {
        const string id = "5017a001";
        ParticlePresetCache.Put(new ParticleAsset
        {
            Id = id, Name = "SortTest",
            Preset = new ParticlePreset { SortYOffset = 12f, RenderLayer = RenderLayers.Entities },
        });

        var scene = new Scene("Sort");
        var e = scene.CreateEntity("Emitter");
        e.GetComponent<Transform>()!.Position = new Vector2(0f, 100f);
        var em = e.AddComponent<ParticleEmitter>();
        em.PresetId = id;
        scene.FlushPendingAdds();

        Check("premise: the preset resolves (otherwise the rest is vacuous)", em.ResolvePreset() != null);
        Check("* the preset offset is added to the Y-sort key (steam in front of the kettle)",
            Near(em.SortY, 112f), $"{em.SortY}");
        Check("* the preset decides the layer as well", em.RenderLayer == RenderLayers.Entities, $"{em.RenderLayer}");

        ParticlePresetCache.Put(new ParticleAsset
        {
            Id = "5017a002", Name = "SortTest0",
            Preset = new ParticlePreset { SortYOffset = 0f },
        });
        em.PresetId = "5017a002";
        Check("* offset 0 leaves the foot Y untouched (a constant return is caught here)",
            Near(em.SortY, 100f), $"{em.SortY}");
    }

    private static void TestAdditiveSplit()
    {
        ParticlePresetCache.Put(new ParticleAsset
        {
            Id = "add00001", Name = "Additive", Preset = new ParticlePreset { Additive = true },
        });
        ParticlePresetCache.Put(new ParticleAsset
        {
            Id = "add00002", Name = "Alpha", Preset = new ParticlePreset { Additive = false },
        });

        var scene = new Scene("AdditiveSplit");
        var a = scene.CreateEntity("AdditiveEmitter");
        a.AddComponent<ParticleEmitter>().PresetId = "add00001";
        var b = scene.CreateEntity("AlphaEmitter");
        b.AddComponent<ParticleEmitter>().PresetId = "add00002";
        scene.FlushPendingAdds();

        var addEm = a.GetComponent<ParticleEmitter>()!;
        var alphaEm = b.GetComponent<ParticleEmitter>()!;

        Check("premise: both presets resolve (otherwise the rest is vacuous)",
            addEm.ResolvePreset() != null && alphaEm.ResolvePreset() != null);
        Check("* the preset decides whether it is additive", addEm.IsAdditive && !alphaEm.IsAdditive);

        Check("* the decision seam agrees with the emitter (two copies would diverge)",
            Scene.IsAdditiveRenderable(addEm) && !Scene.IsAdditiveRenderable(alphaEm));

        Check("* the scene knows it contains additive renderables", scene.HasAdditive());

        ParticlePresetCache.Put(new ParticleAsset
        {
            Id = "add00001", Name = "Additive", Preset = new ParticlePreset { Additive = false },
        });
        Check("* with no additive renderables no batch is opened (control group: a constant true is caught here)",
            !scene.HasAdditive());
    }

    private static void TestPrewarm()
    {
        var preset = SnowLike();
        preset.Rate = 20f;
        preset.MaxParticles = 500;
        preset.Lifetime = ParticleValue.Constant(999f);

        var cold = new ParticleSimulation(51);
        cold.Prewarm(Vector2.Zero, preset, emitting: true);
        Check("premise: prewarmSeconds 0 does nothing", cold.AliveCount == 0, $"{cold.AliveCount}");

        preset.PrewarmSeconds = 3f;
        var warm = new ParticleSimulation(51);
        warm.Prewarm(Vector2.Zero, preset, emitting: true);
        Check("* prewarming means the room is already full on entry (rate 20 for 3s is about 60)",
            warm.AliveCount is >= 55 and <= 62, $"{warm.AliveCount}");

        var again = new ParticleSimulation(51);
        again.Prewarm(Vector2.Zero, preset, emitting: true);
        Check("* prewarming is deterministic (same seed, same sky)", again.AliveCount == warm.AliveCount);

        preset.PrewarmSeconds = 3600f;
        preset.MaxParticles = 40;
        var capped = new ParticleSimulation(51);
        int steps = capped.Prewarm(Vector2.Zero, preset, emitting: true);
        int maxSteps = (int)(ParticlePreset.MaxPrewarmSeconds * 30f);
        Check("* prewarming has a cap (writing 3600s still runs only 30s)",
            steps <= maxSteps && steps >= maxSteps - 2, $"{steps} steps (cap {maxSteps})");
        Check("the pool cap holds even when clamped", capped.AliveCount <= 40, $"{capped.AliveCount}");

        preset.PrewarmSeconds = 3f;
        preset.MaxParticles = 500;
        var off = new ParticleSimulation(51);
        off.Prewarm(Vector2.Zero, preset, emitting: false);
        Check("* with emission off, prewarming produces nothing", off.AliveCount == 0, $"{off.AliveCount}");

        const string id = "9ee00001";
        ParticlePresetCache.Put(new ParticleAsset
        {
            Id = id, Name = "Prewarm",
            Preset = new ParticlePreset
            {
                Rate = 20f, MaxParticles = 500, PrewarmSeconds = 3f,
                Lifetime = ParticleValue.Constant(999f), Shape = EmitterShape.Point(),
            },
        });

        var scene = new Scene("Prewarm");
        var onEnt = scene.CreateEntity("On");
        onEnt.AddComponent<ParticleEmitter>().PresetId = id;
        var offEnt = scene.CreateEntity("Off");
        var offEm = offEnt.AddComponent<ParticleEmitter>();
        offEm.PresetId = id; offEm.Emitting = false;
        scene.FlushPendingAdds();

        scene.Update(1f / 60f);
        var onEm = onEnt.GetComponent<ParticleEmitter>()!;
        Check("** the emitter prewarms on its first tick (wiring: measuring the sim alone never touches this line)",
            onEm.AliveCount > 50, $"{onEm.AliveCount}");
        Check("** Emitting=false on the emitter blocks prewarming too",
            offEm.AliveCount == 0, $"{offEm.AliveCount}");

        onEm.OnDisable();
        Check("premise: OnDisable clears", onEm.AliveCount == 0);
        scene.Update(1f / 60f);
        Check("** re-entering the room fills the sky again (an effect correct only on first visit is the worst kind)",
            onEm.AliveCount > 50, $"{onEm.AliveCount}");
    }

    private static void TestPresetRoundTrip()
    {
        var asset = new ParticleAsset
        {
            Id = "cafe0001",
            Name = "Ünïcode",
            Preset = new ParticlePreset
            {
                Shape = EmitterShape.Box(320f, 8f),
                Rate = 12.5f,
                BurstMin = 3, BurstMax = 7,
                MaxParticles = 300,
                Speed = ParticleValue.Range(8f, 22f),
                Direction = 95f, Spread = 20f,
                GravityX = -3f, GravityY = 14f,
                SwayAmplitude = 6f, SwayFrequency = 0.4f,
                Lifetime = ParticleValue.Range(3f, 6f),
                Scale = ParticleValue.RangeCurved(0.8f, 1.2f, 0.5f, 1f, 0.8f),
                Alpha = ParticleValue.Curved(1f, 0f),
                Rotation = ParticleValue.Range(0f, 360f),
                RotationSpeed = ParticleValue.Constant(45f),
                TextureId = "aabbccdd",
                TintR = 220, TintG = 235, TintB = 255,
                RenderLayer = RenderLayers.AboveEntities,
            },
        };

        var dir = Path.Combine(Path.GetTempPath(), "pixelcore-particle-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "Ünïcode.particle");
        try
        {
            asset.Save(file);
            var text = File.ReadAllText(file);

            Check("* enums serialize by name (numbers would shift silently the day a member moves)",
                text.Contains("\"kind\": \"Box\""), Snippet(text, "kind"));

            Check("* the derived tint is absent from the file (get-only properties serialize too)", !text.Contains("\"tint\":"));

            Check("* non-ASCII is not escaped", text.Contains("Ünïcode") && !text.Contains("\\u"));

            var back = ParticleAsset.Load(file);
            Check("round trip: it loads", back != null);
            if (back == null) return;

            var p = back.Preset;
            Check("round trip: id and name", back.Id == "cafe0001" && back.Name == "Ünïcode", $"{back.Id}/{back.Name}");
            Check("round trip: shape", p.Shape.Kind == EmitterShapeKind.Box && Near(p.Shape.BoxWidth, 320f));
            Check("round trip: emission", Near(p.Rate, 12.5f) && p.BurstMin == 3 && p.BurstMax == 7 && p.MaxParticles == 300);
            Check("round trip: speed and direction", Near(p.Speed.Min, 8f) && Near(p.Speed.Max, 22f)
                  && Near(p.Direction, 95f) && Near(p.Spread, 20f));
            Check("round trip: forces and sway", Near(p.GravityX, -3f) && Near(p.GravityY, 14f)
                  && Near(p.SwayAmplitude, 6f) && Near(p.SwayFrequency, 0.4f));
            Check("* round trip: the curve points survive",
                p.Scale.Curve is { Length: 3 } sc && Near(sc[1], 1f),
                p.Scale.Curve == null ? "(null)" : $"{p.Scale.Curve.Length} points");
            Check("** round trip: per-particle variation (min != max) and a curve coexist in one field",
                Near(p.Scale.Min, 0.8f) && Near(p.Scale.Max, 1.2f) && p.Scale.Curve is { Length: 3 },
                $"{p.Scale.Min}..{p.Scale.Max} x {p.Scale.Curve?.Length} points");
            Check("* the old value format (kind:Range and friends) is absent from the file",
                !text.Contains("\"kind\": \"Range\"") && !text.Contains("\"kind\": \"Constant\"")
                && !text.Contains("\"kind\": \"Curve\""), Snippet(text, "kind"));
            Check("round trip: texture, tint and layer", p.TextureId == "aabbccdd" && p.TintR == 220 && p.TintB == 255
                  && p.RenderLayer == RenderLayers.AboveEntities);

            var renamed = Path.Combine(dir, "Renamed.particle");
            File.Move(file, renamed);
            Check("* the file name is the truth for the name (renaming the file renames the preset)",
                ParticleAsset.Load(renamed)?.Name == "Renamed");

            var broken = Path.Combine(dir, "Broken.particle");
            File.WriteAllText(broken, "{ this is not JSON");
            Check("* a broken .particle yields null, not an exception", ParticleAsset.Load(broken) == null);

            var errBuf = new System.Text.StringBuilder();
            var savedErr = Console.Error;
            ParticleAsset? legacyCompact, legacyIndented;
            var lc = Path.Combine(dir, "OldFormat_Compact.particle");
            var li = Path.Combine(dir, "OldFormat_Indented.particle");
            File.WriteAllText(lc, "{\"id\":\"dead0001\",\"preset\":{\"scale\":{\"kind\":\"Range\",\"a\":1,\"b\":0}}}");
            File.WriteAllText(li, "{\n  \"id\": \"dead0002\",\n  \"preset\": {\n    \"scale\": { \"kind\": \"Curve\", \"a\": 1 }\n  }\n}");
            try
            {
                Console.SetError(new StringWriter(errBuf));
                legacyCompact = ParticleAsset.Load(lc);
                legacyIndented = ParticleAsset.Load(li);
            }
            finally { Console.SetError(savedErr); }

            Check("** the old format is rejected on load: compact form (it must not become a silently empty preset)", legacyCompact == null);
            Check("** the old format is rejected on load: indented form", legacyIndented == null);
            Check("** it says it is the old format and explains how to port",
                errBuf.ToString().Contains("old format") && errBuf.ToString().Contains("min"),
                errBuf.Length == 0 ? "(said nothing)" : errBuf.ToString().Split('\n')[0]);
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch {  }
        }
    }

    private static void TestSceneRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixelcore-pscene-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "ParticleRoundTrip.scene");
        var plainPath = Path.Combine(dir, "NoTint.scene");
        try
        {
            var scene = new Scene("ParticleRoundTrip");
            var e = scene.CreateEntity("SnowEmitter");
            e.GetComponent<Transform>()!.Position = new Vector2(64f, 32f);
            var emitter = e.AddComponent<ParticleEmitter>();
            emitter.PresetId = "beef0001";
            emitter.RateScale = 0.5f;
            emitter.Emitting = false;
            emitter.TintOverride = new Color(200, 210, 255);

            scene.FlushPendingAdds();
            SceneSerializer.SaveToFile(SceneSerializer.ToData(scene), path);
            var json = File.ReadAllText(path);

            Check("* the scene file holds the preset by id",
                json.Contains("\"preset\": \"beef0001\""), Snippet(json, "preset"));
            Check("the scene file has a particleEmitter entry", json.Contains("\"particleEmitter\""));

            var back = new Scene("Restored");
            SceneSerializer.FromData(back, SceneSerializer.LoadFromFile(path)!);
            var got = back.FindEntity("SnowEmitter")?.GetComponent<ParticleEmitter>();

            Check("round trip: the emitter comes back", got != null);
            Check("* round trip: preset id, scale, toggles and tint",
                got != null && got.PresetId == "beef0001" && Near(got.RateScale, 0.5f)
                && !got.Emitting && got.TintOverride == new Color(200, 210, 255));

            var plain = new Scene("NoTint");
            var pe = plain.CreateEntity("Plain");
            pe.AddComponent<ParticleEmitter>().PresetId = "beef0002";
            plain.FlushPendingAdds();
            SceneSerializer.SaveToFile(SceneSerializer.ToData(plain), plainPath);
            var plainJson = File.ReadAllText(plainPath);
            Check("premise: the emitter was really saved (otherwise the rest is vacuous)",
                plainJson.Contains("\"preset\": \"beef0002\""), Snippet(plainJson, "preset"));
            Check("* with no tint override nothing goes to the file (so the preset does not lie)",
                !plainJson.Contains("tintR"), Snippet(plainJson, "tint"));

            var backPlain = new Scene("Restored2");
            SceneSerializer.FromData(backPlain, SceneSerializer.LoadFromFile(plainPath)!);
            var gotPlain = backPlain.FindEntity("Plain")?.GetComponent<ParticleEmitter>();
            Check("the round trip holds without a tint too", gotPlain != null && gotPlain.TintOverride == null);
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch {  }
        }
    }

    private static void TestUnresolvedPresetIsPreserved()
    {
        ParticleEmitter.ResetWarnings();

        var scene = new Scene("Unresolved");
        var e = scene.CreateEntity("BrokenEmitter");
        var emitter = e.AddComponent<ParticleEmitter>();
        emitter.PresetId = "deadbe01";

        Check("premise: the id does not resolve (resolving would make the rest vacuous)", emitter.ResolvePreset() == null);
        Check("* the unresolved id is preserved", emitter.UnresolvedPresetRef == "deadbe01", emitter.UnresolvedPresetRef ?? "(null)");

        var dir = Path.Combine(Path.GetTempPath(), "pixelcore-punres-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Unresolved.scene");
        try
        {
            scene.FlushPendingAdds();
            SceneSerializer.SaveToFile(SceneSerializer.ToData(scene), path);
            var json = File.ReadAllText(path);
            Check("* saving does not lose the reference (clearing it silently would never reattach)",
                json.Contains("\"preset\": \"deadbe01\""), Snippet(json, "preset"));
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch {  }
        }

        emitter.Update(1f / 60f);
        Check("* an unresolved emitter emits nothing and does not die", emitter.AliveCount == 0);

        ParticleEmitter.ResetWarnings();
    }

    private static ParticlePreset SnowLike() => new()
    {
        Shape = EmitterShape.Box(320f, 4f),
        Rate = 20f,
        MaxParticles = 256,
        Speed = ParticleValue.Range(10f, 18f),
        Direction = 90f, Spread = 16f,
        GravityY = 2f,
        SwayAmplitude = 5f, SwayFrequency = 0.35f,
        Lifetime = ParticleValue.Range(4f, 7f),
        Scale = ParticleValue.Constant(1f),
        Alpha = ParticleValue.Constant(1f),
    };

    private static ParticlePreset PuffLike() => new()
    {
        Shape = EmitterShape.Circle(3f),
        Rate = 0f,
        BurstMin = 8, BurstMax = 12,
        MaxParticles = 64,
        Speed = ParticleValue.Range(20f, 45f),
        Direction = 0f, Spread = 360f,
        Lifetime = ParticleValue.Range(0.3f, 0.6f),
        Scale = ParticleValue.Curved(1f, 0.2f),
        Alpha = ParticleValue.Curved(1f, 0f),
    };

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 0.001f;

    private static string Snippet(string json, string key)
    {
        int i = json.IndexOf(key, StringComparison.Ordinal);
        return i < 0 ? $"'{key}' not found" : json.Substring(Math.Max(0, i - 20), Math.Min(60, json.Length - Math.Max(0, i - 20)));
    }

    private static void TestAtmosphereRouting()
    {
        Console.WriteLine("--- atmosphere layer routing ---");

        ParticlePresetCache.Put(new ParticleAsset { Id = "atm00001", Name = "ambient",
            Preset = new ParticlePreset { SnapToPixels = false } });
        ParticlePresetCache.Put(new ParticleAsset { Id = "atm00002", Name = "world",
            Preset = new ParticlePreset() });
        ParticlePresetCache.Put(new ParticleAsset { Id = "atm00003", Name = "ambientAdditive",
            Preset = new ParticlePreset { SnapToPixels = false, Additive = true } });

        var scene = new Scene("TwoLayers");
        var a = scene.CreateEntity("ambient"); a.AddComponent<ParticleEmitter>().PresetId = "atm00001";
        var w = scene.CreateEntity("world"); w.AddComponent<ParticleEmitter>().PresetId = "atm00002";
        var g = scene.CreateEntity("ambientAdditive"); g.AddComponent<ParticleEmitter>().PresetId = "atm00003";
        scene.FlushPendingAdds();
        var atm = a.GetComponent<ParticleEmitter>()!;
        var world = w.GetComponent<ParticleEmitter>()!;
        var glow = g.GetComponent<ParticleEmitter>()!;

        Check("premise: all three presets resolve", atm.ResolvePreset() != null
            && world.ResolvePreset() != null && glow.ResolvePreset() != null);

        Check("* a preset with nothing written is on (the basis for the seven existing presets being unchanged)",
            world.ResolvePreset()!.SnapsToPixels && !world.InAtmosphere);
        Check("* an off preset goes to the atmosphere layer", atm.InAtmosphere && glow.InAtmosphere);
        Check("* the decision seam agrees with the emitter (two copies would diverge)",
            Scene.IsAtmosphereRenderable(atm) && !Scene.IsAtmosphereRenderable(world));
        Check("* atmosphere and additive are orthogonal (it can be both at once)",
            Scene.IsAtmosphereRenderable(glow) && Scene.IsAdditiveRenderable(glow));
        Check("* the scene knows it has an atmosphere layer", scene.HasAtmosphere());

        var order = scene.BuildRenderOrder();
        Check("premise: all three are in the order list (missing here would make the rest vacuous)",
            order.Contains(atm) && order.Contains(world) && order.Contains(glow),
            order.Count.ToString());

        var buckets = new System.Collections.Generic.Dictionary<Scene.RenderBucket, int>();
        foreach (var r in order)
        {
            var b = Scene.Classify(r);
            buckets[b] = buckets.TryGetValue(b, out var n) ? n + 1 : 1;
        }
        Check("* every renderer belongs to exactly one bucket (exhaustive - the sum is the list size)",
            buckets.Values.Sum() == order.Count, $"{buckets.Values.Sum()} vs {order.Count}");
        Check("* world alpha does not include the atmosphere",
            Scene.Classify(world) == Scene.RenderBucket.WorldAlpha
            && Scene.Classify(atm) == Scene.RenderBucket.AtmosphereAlpha
            && Scene.Classify(glow) == Scene.RenderBucket.AtmosphereAdditive,
            $"{Scene.Classify(world)} / {Scene.Classify(atm)} / {Scene.Classify(glow)}");
        Check("   control: the buckets are really in use (piling into one would make the above vacuous)",
            buckets.Count >= 3, string.Join(",", buckets.Select(kv => $"{kv.Key}={kv.Value}")));

        ParticlePresetCache.Put(new ParticleAsset { Id = "atm00001", Name = "ambient",
            Preset = new ParticlePreset() });
        ParticlePresetCache.Put(new ParticleAsset { Id = "atm00003", Name = "ambientAdditive",
            Preset = new ParticlePreset { Additive = true } });
        Check("* control: with everything on, the atmosphere pass is not opened", !scene.HasAtmosphere());
        Check("   control: the additive decision is unchanged then (orthogonality did not collapse)",
            Scene.IsAdditiveRenderable(glow) && !Scene.IsAtmosphereRenderable(glow));
    }

    private static void TestDrawPositionSnap()
    {
        Console.WriteLine("--- snap position ---");
        var p = new Vector2(160.5f, 90.3f);
        Check("* on rounds to world integers (= pixel snapping)",
            ParticleEmitter.DrawPosition(p, true) == new Vector2(161f, 90f),
            ParticleEmitter.DrawPosition(p, true).ToString());
        Check("* off leaves it as it is (it sits between grid lines)",
            ParticleEmitter.DrawPosition(p, false) == p);
        Check("   control: the two really differ (equal would make the two above vacuous)",
            ParticleEmitter.DrawPosition(p, true) != ParticleEmitter.DrawPosition(p, false));
        Check("an integer position is the same on and off (rounding does not disturb the value)",
            ParticleEmitter.DrawPosition(new Vector2(12f, 34f), true)
            == ParticleEmitter.DrawPosition(new Vector2(12f, 34f), false));
        Check("* it is Floor(+0.5), not (int) truncation (-0.5 goes to 0)",
            ParticleEmitter.DrawPosition(new Vector2(-0.5f, 0f), true).X == 0f,
            ParticleEmitter.DrawPosition(new Vector2(-0.5f, 0f), true).X.ToString());
    }

    private static void TestSnapSwitchRoundTrip()
    {
        Console.WriteLine("--- SnapToPixels round trip ---");

        static (string Text, ParticleAsset? Back) Trip(ParticleAsset a)
        {
            var path = Path.Combine(Path.GetTempPath(),
                "pc_snap_" + Guid.NewGuid().ToString("N")[..8] + ".particle");
            try
            {
                a.Save(path);
                return (File.ReadAllText(path), ParticleAsset.Load(path));
            }
            finally { try { File.Delete(path); } catch { } }
        }

        var (t1, b1) = Trip(new ParticleAsset { Id = "snap0001", Name = "Plain",
            Preset = new ParticlePreset() });
        Check("* the default (on) does not write a snapToPixels key (zero diff in the seven real presets)",
            !t1.Contains("snapToPixels"), t1.Length.ToString());
        Check("   no key loads as on",
            b1 != null && b1.Preset.SnapsToPixels && !b1.Preset.InAtmosphere);

        var (t2, b2) = Trip(new ParticleAsset { Id = "snap0002", Name = "ambient",
            Preset = new ParticlePreset { SnapToPixels = false } });
        Check("* off is written to the file (if omitted it comes back as on at load - a silent failure)",
            t2.Contains("\"snapToPixels\": false"), t2.Length.ToString());
        Check("* off comes back as it was",
            b2 != null && !b2.Preset.SnapsToPixels && b2.Preset.InAtmosphere);

        var (_, b3) = Trip(new ParticleAsset { Id = "snap0003", Name = "explicit",
            Preset = new ParticlePreset { SnapToPixels = true } });
        Check("* an explicit on round-trips too (a file with true written in does not break)",
            b3 != null && b3.Preset.SnapsToPixels);
    }

    private static void TestNoiseField()
    {
        Console.WriteLine("--- noise field ---");

        Check("* the same coordinates always give the same value (the hash is pure arithmetic)",
            ParticleSimulation.Perlin(3.7f, 1.2f, 0.3f, 2) == ParticleSimulation.Perlin(3.7f, 1.2f, 0.3f, 2));
        Check("* different coordinates give different values (so returning a constant 0 does not pass)",
            ParticleSimulation.Perlin(3.7f, 1.2f, 0.3f, 2) != ParticleSimulation.Perlin(9.1f, 1.2f, 0.3f, 2));

        float lo = 1f, hi = -1f;
        for (int i = 0; i < 400; i++)
        {
            float v = ParticleSimulation.Perlin(i * 0.37f, i * 0.11f, 0.3f, 2);
            lo = MathF.Min(lo, v); hi = MathF.Max(hi, v);
        }
        Check($"* values stay within -1..1 ({lo:0.00}~{hi:0.00}) - so that NoiseStrength reads in px",
            lo >= -1.001f && hi <= 1.001f, $"{lo}~{hi}");
        Check("   premise: it really varies (a flat range would make the check above vacuous)", hi - lo > 0.3f,
            (hi - lo).ToString("0.00"));
        Check("* more octaves do not increase the strength (normalised by the sum of amplitudes)",
            MathF.Abs(ParticleSimulation.Perlin(2.5f, 0.5f, 0.3f, 4)) <= 1.001f);

        var preset = new ParticlePreset
        {
            Rate = 0f, MaxParticles = 8, Lifetime = ParticleValue.Constant(10f),
            NoiseStrength = 4f, NoiseFrequency = 0.3f, NoiseScroll = 0.5f, NoiseOctaves = 2,
        };
        static Vector2[] RunOnce(ParticlePreset ps, int seed)
        {
            var sim = new ParticleSimulation(seed);
            sim.Configure(ps.MaxParticles);
            sim.Emit(6, Vector2.Zero, ps);
            for (int i = 0; i < 30; i++)
                sim.Update(1f / 60f, Vector2.Zero, ps, emitting: false);
            var outp = new Vector2[sim.AliveCount];
            for (int i = 0; i < sim.AliveCount; i++) outp[i] = sim.Alive[i].RenderPosition;
            return outp;
        }
        var r1 = RunOnce(preset, 4242);
        var r2 = RunOnce(preset, 4242);
        Check("premise: particles are alive", r1.Length > 0, r1.Length.ToString());
        Check("* same seed, same trajectory (the basis for checks being able to catch regressions)",
            r1.Length == r2.Length && r1.SequenceEqual(r2));
        var r3 = RunOnce(preset, 777);
        Check("   control: a different seed gives a different trajectory (so returning a constant does not pass)",
            !r1.SequenceEqual(r3));

        static Vector2[] RunOn(ParticleSimulation sim, ParticlePreset ps, int seed)
        {
            sim.Reseed(seed);
            sim.Configure(ps.MaxParticles);
            sim.Emit(6, Vector2.Zero, ps);
            for (int i = 0; i < 30; i++) sim.Update(1f / 60f, Vector2.Zero, ps, emitting: false);
            var o = new Vector2[sim.AliveCount];
            for (int i = 0; i < sim.AliveCount; i++) o[i] = sim.Alive[i].RenderPosition;
            return o;
        }
        var reused = new ParticleSimulation(1);
        var q1 = RunOn(reused, preset, 4242);
        var q2 = RunOn(reused, preset, 4242);
        Check("* reseeding the same simulation gives the same trajectory (the noise clock is rewound)",
            q1.Length == q2.Length && q1.SequenceEqual(q2),
            q1.Length == q2.Length ? "trajectories differ" : $"{q1.Length} vs {q2.Length}");
        Check("   premise: that trajectory matches a new simulation too (reuse must not give a different answer)",
            q1.SequenceEqual(r1));

        var decor = new ParticleSimulation(31);
        decor.Configure(preset.MaxParticles);
        decor.Emit(6, Vector2.Zero, preset);
        for (int i = 0; i < 20; i++) decor.Update(1f / 60f, Vector2.Zero, preset, emitting: false);
        int differ = 0;
        for (int i = 0; i < decor.AliveCount; i++)
        {
            var n = decor.Alive[i].NoiseOffset;
            if (MathF.Abs(n.X - n.Y) > 1e-4f) differ++;
        }
        Check($"* X and Y differ ({differ}/{decor.AliveCount}) - equal would mean flowing only diagonally",
            decor.AliveCount > 0 && differ == decor.AliveCount, $"{differ}/{decor.AliveCount}");

        var off = new ParticlePreset { Rate = 0f, MaxParticles = 8,
            Lifetime = ParticleValue.Constant(10f), NoiseStrength = 0f };
        var o1 = RunOnce(off, 4242);
        bool allZeroOffset = true;
        var simOff = new ParticleSimulation(4242);
        simOff.Configure(off.MaxParticles); simOff.Emit(6, Vector2.Zero, off);
        for (int i = 0; i < 30; i++) simOff.Update(1f / 60f, Vector2.Zero, off, emitting: false);
        for (int i = 0; i < simOff.AliveCount; i++)
            if (simOff.Alive[i].NoiseOffset != Vector2.Zero) allZeroOffset = false;
        Check("* at strength 0 the noise offset is 0 (existing preset trajectories unchanged)", allZeroOffset);
        Check("   control: with a strength it is not 0", r1.Length > 0 && o1.Length > 0
            && !r1.SequenceEqual(o1));
    }

    private static void TestCurlField()
    {
        Console.WriteLine("--- curl field ---");

        var a = ParticleSimulation.Curl(new Vector2(37f, 11f), 0.02f, 0.3f, 2);
        var b = ParticleSimulation.Curl(new Vector2(37f, 11f), 0.02f, 0.3f, 2);
        Check("* the same place at the same time gives the same vector (deterministic)", a == b, $"{a} vs {b}");
        Check("   premise: it is not the zero vector (so returning a constant 0 does not pass)",
            a.LengthSquared() > 1e-6f, a.ToString());

        Check("* the length is at most 1 - |noise velocity| ≤ strength comes from here",
            MeasureCurl(0.02f, 2).max <= 1.0001f, MeasureCurl(0.02f, 2).max.ToString("0.000"));

        float m1 = MeasureCurl(0.005f, 2).median;
        float m2 = MeasureCurl(0.02f, 2).median;
        float m3 = MeasureCurl(0.08f, 2).median;
        float lo3 = MathF.Min(m1, MathF.Min(m2, m3)), hi3 = MathF.Max(m1, MathF.Max(m2, m3));
        Check($"* sweeping the frequency across 16x leaves the typical |curl| the same ({m1:0.00} / {m2:0.00} / {m3:0.00})",
            lo3 > 1e-4f && hi3 / lo3 < 1.3f, $"{m1} {m2} {m3} - ratio {hi3 / MathF.Max(lo3, 1e-9f):0.00}");
        Check($"   premise: that value is near 1 - the basis for CurlGain {ParticleSimulation.CurlGain} ({m2:0.00})",
            m2 > 0.7f && m2 < 1.4f, m2.ToString("0.000"));

        var near = ParticleSimulation.Curl(new Vector2(40f, 11f), 0.02f, 0.3f, 2);
        var far = ParticleSimulation.Curl(new Vector2(940f, 611f), 0.02f, 0.3f, 2);
        Check("* a neighbour (3px over) flows in almost the same direction - that is what a 'field' is",
            Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(near)) > 0.9f,
            Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(near)).ToString("0.000"));
        Check("   control: a distant spot is unrelated (everything in the same direction would make the above vacuous)",
            Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(far)) < 0.9f,
            Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(far)).ToString("0.000"));
    }

    private static (float median, float max) MeasureCurl(float freq, int octaves)
    {
        var mags = new float[2000];
        float max = 0f;
        for (int i = 0; i < mags.Length; i++)
        {
            var c = ParticleSimulation.Curl(new Vector2(i * 7.13f % 900f - 450f, i * 3.77f % 900f - 450f),
                                            freq, 0f, octaves);
            mags[i] = c.Length();
            max = MathF.Max(max, mags[i]);
        }
        Array.Sort(mags);
        return (mags[mags.Length / 2], max);
    }

    private static ParticlePreset DustLike(NoiseMode mode, float strength) => new()
    {
        Shape = EmitterShape.Box(200f, 70f),
        Rate = 0f, MaxParticles = 16,
        Speed = ParticleValue.Constant(0f),
        Lifetime = ParticleValue.Constant(20f),
        Scale = ParticleValue.Constant(1f),
        Alpha = ParticleValue.Constant(1f),
        NoiseMode = mode,
        NoiseStrength = strength,
        NoiseFrequency = 0.02f,
        NoiseScroll = 0.1f,
        NoiseOctaves = 2,
        NoiseResponse = 0.6f,
    };

    private static ParticleSimulation RunDust(ParticlePreset ps, int steps, int seed = 99)
    {
        var sim = new ParticleSimulation(seed);
        sim.Configure(ps.MaxParticles);
        sim.Emit(8, Vector2.Zero, ps);
        for (int i = 0; i < steps; i++) sim.Update(1f / 60f, Vector2.Zero, ps, emitting: false);
        return sim;
    }

    private static float MaxNoiseSpeed(ParticleSimulation sim)
    {
        float m = 0f;
        for (int i = 0; i < sim.AliveCount; i++) m = MathF.Max(m, sim.Alive[i].NoiseVelocity.Length());
        return m;
    }

    private static float MaxDrift(ParticleSimulation sim)
    {
        float m = 0f;
        for (int i = 0; i < sim.AliveCount; i++) m = MathF.Max(m, sim.Alive[i].NoiseOffset.Length());
        return m;
    }

    private static void TestVelocityNoise()
    {
        Console.WriteLine("--- velocity noise ---");

        var vel = RunDust(DustLike(NoiseMode.Velocity, 14f), 600);
        var dis = RunDust(DustLike(NoiseMode.Displacement, 14f), 600);

        Check("premise: particles are alive", vel.AliveCount > 0 && dis.AliveCount > 0);
        Check($"* velocity mode drifts far in 10 seconds ({MaxDrift(vel):0} px)",
            MaxDrift(vel) > 40f, MaxDrift(vel).ToString("0.0"));
        Check($"* control: displacement mode jitters in place (within {MaxDrift(dis):0} px at the same strength)",
            MaxDrift(dis) <= 14.001f * MathF.Sqrt(2f), MaxDrift(dis).ToString("0.0"));

        var hardPreset = DustLike(NoiseMode.Velocity, 14f);
        hardPreset.Lifetime = ParticleValue.Constant(120f);
        var hard = RunDust(hardPreset, 3600);
        Check($"* after 60 seconds the noise speed still does not exceed strength ({MaxNoiseSpeed(hard):0.0} ≤ 14·√2)",
            MaxNoiseSpeed(hard) <= 14f * MathF.Sqrt(2f) + 0.001f, MaxNoiseSpeed(hard).ToString("0.000"));
        Check("   premise: a speed has really built up (0 would make the bound above vacuous)",
            MaxNoiseSpeed(hard) > 1f, MaxNoiseSpeed(hard).ToString("0.000"));

        var fast = DustLike(NoiseMode.Velocity, 14f); fast.NoiseResponse = 0.05f;
        var slow = DustLike(NoiseMode.Velocity, 14f); slow.NoiseResponse = 5f;
        float vFast = MaxNoiseSpeed(RunDust(fast, 30));
        float vSlow = MaxNoiseSpeed(RunDust(slow, 30));
        Check($"* with a small time constant it has already caught up at 0.5 seconds ({vFast:0.0} px/s)", vFast > 5f, vFast.ToString("0.0"));
        Check($"* control: with a 5-second time constant it has barely caught up at 0.5 seconds ({vSlow:0.0} px/s)",
            vSlow < vFast * 0.5f, $"fast={vFast:0.00} slow={vSlow:0.00}");

        var a = RunDust(DustLike(NoiseMode.Velocity, 14f), 300);
        var b = RunDust(DustLike(NoiseMode.Velocity, 14f), 300);
        var c = RunDust(DustLike(NoiseMode.Velocity, 14f), 300, seed: 12345);
        Check("* same seed, same trajectory", MaxDrift(a) == MaxDrift(b), $"{MaxDrift(a)} vs {MaxDrift(b)}");
        Check("   control: a different seed gives a different trajectory", MaxDrift(a) != MaxDrift(c));

        var pointPreset = DustLike(NoiseMode.Velocity, 14f);
        pointPreset.Shape = EmitterShape.Circle(0f);
        pointPreset.NoiseResponse = 0.05f;
        static Vector2 NoiseVelAt(ParticlePreset ps, Vector2 where)
        {
            var sim = new ParticleSimulation(7);
            sim.Configure(4);
            sim.Emit(1, where, ps);
            sim.Update(1f / 60f, where, ps, emitting: false);
            return sim.AliveCount > 0 ? sim.Alive[0].NoiseVelocity : Vector2.Zero;
        }
        var here = NoiseVelAt(pointPreset, new Vector2(40f, 11f));
        var next = NoiseVelAt(pointPreset, new Vector2(43f, 11f));
        var away = NoiseVelAt(pointPreset, new Vector2(940f, 611f));
        Check("   premise: a noise velocity has built up", here.LengthSquared() > 1e-6f, here.ToString());
        Check("* a particle 3px over flows in the same direction - evidence the field is read at the world position",
            Vector2.Dot(Vector2.Normalize(here), Vector2.Normalize(next)) > 0.9f,
            Vector2.Dot(Vector2.Normalize(here), Vector2.Normalize(next)).ToString("0.000"));
        Check("   control: a particle 900px away is unrelated (all the same would make the above vacuous)",
            Vector2.Dot(Vector2.Normalize(here), Vector2.Normalize(away)) < 0.9f,
            Vector2.Dot(Vector2.Normalize(here), Vector2.Normalize(away)).ToString("0.000"));

        var readPreset = DustLike(NoiseMode.Velocity, 14f);
        readPreset.Shape = EmitterShape.Circle(0f);
        readPreset.NoiseScroll = 0f;
        readPreset.NoiseResponse = 1e-4f;
        var drifted = new ParticleSimulation(3);
        drifted.Configure(4);
        drifted.Emit(1, new Vector2(120f, 40f), readPreset);
        for (int i = 0; i < 300; i++) drifted.Update(1f / 60f, Vector2.Zero, readPreset, emitting: false);

        if (drifted.AliveCount > 0)
        {
            var d0 = drifted.Alive[0];
            var sampledAt = d0.Position + d0.NoiseOffset - d0.NoiseVelocity * (1f / 60f);
            var atRender = ParticleSimulation.Curl(sampledAt, readPreset.NoiseFrequency, 0f,
                                                   readPreset.NoiseOctaves) * readPreset.NoiseStrength;
            var atBase = ParticleSimulation.Curl(d0.Position, readPreset.NoiseFrequency, 0f,
                                                 readPreset.NoiseOctaves) * readPreset.NoiseStrength;
            Check($"   premise: it drifted far enough that the field differs at the two points (offset {d0.NoiseOffset.Length():0}px)",
                (atRender - atBase).Length() > 1f,
                $"offset {d0.NoiseOffset.Length():0.0} difference {(atRender - atBase).Length():0.00}");
            Check($"* the field is read at the drawn position (error {(d0.NoiseVelocity - atRender).Length():0.000})",
                (d0.NoiseVelocity - atRender).Length() < 0.05f,
                $"measured {d0.NoiseVelocity} vs render {atRender}");
            Check("   control: it differs from the value at the base (equal would make the above vacuous)",
                (d0.NoiseVelocity - atBase).Length() > 1f,
                $"measured {d0.NoiseVelocity} vs base {atBase}");
        }
        else Check("* the field is read at the drawn position - premise: a particle is alive", false);

        var withGravity = DustLike(NoiseMode.Velocity, 14f);
        withGravity.GravityY = 20f;
        var g = RunDust(withGravity, 120);
        float vy = g.AliveCount > 0 ? g.Alive[0].Velocity.Y : 0f;
        Check($"* the noise does not overwrite the particle's own velocity (2 seconds of gravity = {vy:0.0} ≈ 40)",
            MathF.Abs(vy - 40f) < 0.5f, vy.ToString("0.000"));
    }

    private static void TestBuiltinTexture()
    {
        Console.WriteLine("--- built-in textures ---");

        Check("* @soft is a built-in name", ParticleEmitter.IsBuiltinTexture(ParticleEmitter.SoftDot)
            && ParticleEmitter.IsKnownBuiltin(ParticleEmitter.SoftDot));
        Check("* an unknown @name also takes the built-in path but is not 'known' (it complains and falls back to 1px)",
            ParticleEmitter.IsBuiltinTexture("@nope") && !ParticleEmitter.IsKnownBuiltin("@nope"));
        Check("* an id is not built-in - it goes to the registry",
            !ParticleEmitter.IsBuiltinTexture("d7a1c930"));
        Check("   an empty value is not built-in either", !ParticleEmitter.IsBuiltinTexture(null)
            && !ParticleEmitter.IsBuiltinTexture(""));
    }

    private static void Check(string name, bool ok, string? detail = null)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name +
            (ok || detail == null ? "" : $"   [{detail}]"));
        if (ok) _pass++; else _fail++;
    }
}
