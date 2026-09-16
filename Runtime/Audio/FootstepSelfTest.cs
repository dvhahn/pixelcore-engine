using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Animation;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Audio;

public static class FootstepSelfTest
{
    private static int _pass, _fail;

    private const float Dt = 1f / 60f;

    private const float FrameDur = 1f / 6f;

    public static void Run()
    {
        Console.WriteLine("=== Footstep self-test ===");
        _pass = _fail = 0;

        var am = AudioManager.Instance;
        float savedMaster = am.MasterVolume;
        bool savedListener = am.ListenerActive;
        try
        {
            am.MasterVolume = 0f;
            am.ListenerActive = true;
            WriteFixtureClips();

            TestClipIdPrecondition();
            TestClipSetSeparation();
            TestSceneDefault();
            TestAreaOverride();
            TestNonBoxShapes();
            TestAreaPriority();
            TestAreaIgnoredCases();
            TestTeleport();
            TestMoveGuard();
            TestZombieSubscription();
            TestListenerGate();
            TestPitchJitterRange();
            TestSurfaceMigration();
        }
        finally
        {
            am.MasterVolume = savedMaster;
            am.ListenerActive = savedListener;
            RemoveFixtureClips();
        }

        Console.WriteLine($"=== Footstep: {_pass} passed, {_fail} failed ===");
    }

    private static void TestClipSetSeparation()
    {
        var (scene, entity, _, emitter) = NewRig(TestSurface("t_sep", "Wood").Id);
        var transform = entity.GetComponent<Transform>()!;

        int left = 0, right = 0, wrongSet = 0;
        for (int i = 0; i < 60 * 6; i++)
        {
            transform.Position += new Vector2(0.4f, 0f);
            int before = emitter.StepCount;
            scene.Update(Dt);
            if (emitter.StepCount == before) continue;

            var step = emitter.LastStep!.Value;
            if (step.Left) { left++; if (!Path.GetFileName(step.ClipPath).StartsWith("L")) wrongSet++; }
            else { right++; if (!Path.GetFileName(step.ClipPath).StartsWith("R")) wrongSet++; }
        }

        Check($"steps occur over six seconds (left {left}, right {right})", left > 0 && right > 0);
        Check("left and right alternate roughly evenly (not just one side)",
              Math.Abs(left - right) <= 1);
        Check($"the drawn clip is always in that foot's set ({wrongSet} from the other side)", wrongSet == 0);

        Check($"two steps per one-second cycle, so twelve over six seconds (actual {left + right})", left + right == 12);
    }

    private static void TestSceneDefault()
    {
        var wood = TestSurface("t_wood", "Wood");
        var snow = TestSurface("t_snow", "Snow");

        var (scene, entity, _, emitter) = NewRig(wood.Id);
        Walk(scene, entity, emitter, 1);
        Check($"the scene default surface is used ({emitter.LastStep?.SurfaceId})",
              emitter.LastStep?.SurfaceId == "t_wood");

        scene.DefaultSurfaceId = snow.Id;
        int before = emitter.StepCount;
        Walk(scene, entity, emitter, 1);
        Check($"changing the scene declaration switches surface from the next step ({emitter.LastStep?.SurfaceId})",
              emitter.StepCount > before && emitter.LastStep?.SurfaceId == "t_snow");

        var (scene2, entity2, _, emitter2) = NewRig(null);
        Walk(scene2, entity2, emitter2, 2);
        Check("a room with no surface declared is silent (the previous surface does not leak)", emitter2.StepCount == 0);
    }

    private static void TestAreaOverride()
    {
        var floor = TestSurface("t_floor", "Wood");
        var rug = TestSurface("t_rug", "Snow");

        var (scene, entity, _, emitter) = NewRig(floor.Id);
        MakeArea(scene, "Rug", new Vector2(100, 100), new Vector2(40, 40), rug.Id, priority: 10);
        var transform = entity.GetComponent<Transform>()!;

        transform.Position = new Vector2(0, 0);
        StepInPlaceAt(scene, entity, emitter, new Vector2(0, 0));
        Check($"outside the area gives the room default ({emitter.LastStep?.SurfaceId})", emitter.LastStep?.SurfaceId == "t_floor");

        StepInPlaceAt(scene, entity, emitter, new Vector2(100, 100));
        Check($"inside the area gives that area's surface ({emitter.LastStep?.SurfaceId})", emitter.LastStep?.SurfaceId == "t_rug");

        StepInPlaceAt(scene, entity, emitter, new Vector2(119.9f, 100f));
        Check("just inside the boundary (119.9) is the area", emitter.LastStep?.SurfaceId == "t_rug");

        StepInPlaceAt(scene, entity, emitter, new Vector2(120.1f, 100f));
        Check("just outside the boundary (120.1) is the room default", emitter.LastStep?.SurfaceId == "t_floor");
    }

    private static void TestNonBoxShapes()
    {
        var floor = TestSurface("t_shape_floor", "Wood");
        var zone = TestSurface("t_shape_zone", "Snow");

        {
            var (scene, entity, _, emitter) = NewRig(floor.Id);
            var e = scene.CreateEntity("CircleZone");
            e.GetComponent<Transform>()!.Position = new Vector2(200, 200);
            var circle = e.AddComponent<CircleCollider2D>();
            circle.Radius = 20f;
            circle.IsTrigger = true;
            var area = e.AddComponent<SurfaceArea>();
            area.SurfaceId = zone.Id; area.Priority = 10;
            scene.FlushPendingAdds();

            StepInPlaceAt(scene, entity, emitter, new Vector2(200, 200));
            Check($"it is stepped on inside a circular area ({emitter.LastStep?.SurfaceId})",
                  emitter.LastStep?.SurfaceId == "t_shape_zone");

            StepInPlaceAt(scene, entity, emitter, new Vector2(219f, 219f));
            Check($"the corner outside the circle gives the room default (it is not measured loosely by AABB) ({emitter.LastStep?.SurfaceId})",
                  emitter.LastStep?.SurfaceId == "t_shape_floor");
        }

        {
            var (scene, entity, _, emitter) = NewRig(floor.Id);
            var e = scene.CreateEntity("CapsuleZone");
            e.GetComponent<Transform>()!.Position = new Vector2(300, 300);
            var capsule = e.AddComponent<CapsuleCollider2D>();
            capsule.Radius = 10f; capsule.Length = 40f; capsule.Horizontal = true;
            capsule.IsTrigger = true;
            var area = e.AddComponent<SurfaceArea>();
            area.SurfaceId = zone.Id; area.Priority = 10;
            scene.FlushPendingAdds();

            StepInPlaceAt(scene, entity, emitter, new Vector2(320, 300));
            Check($"it is stepped on inside a capsule area ({emitter.LastStep?.SurfaceId})",
                  emitter.LastStep?.SurfaceId == "t_shape_zone");

            StepInPlaceAt(scene, entity, emitter, new Vector2(300, 320));
            Check($"beyond the capsule radius gives the room default ({emitter.LastStep?.SurfaceId})",
                  emitter.LastStep?.SurfaceId == "t_shape_floor");
        }
    }

    private static void TestAreaPriority()
    {
        var low = TestSurface("t_low", "Wood");
        var high = TestSurface("t_high", "Snow");
        var floor = TestSurface("t_base", "Wood");

        foreach (bool highFirst in new[] { false, true })
        {
            var (scene, entity, _, emitter) = NewRig(floor.Id);
            var at = new Vector2(50, 50);
            if (highFirst)
            {
                MakeArea(scene, "High", at, new Vector2(40, 40), high.Id, priority: 5);
                MakeArea(scene, "Low", at, new Vector2(40, 40), low.Id, priority: 1);
            }
            else
            {
                MakeArea(scene, "Low", at, new Vector2(40, 40), low.Id, priority: 1);
                MakeArea(scene, "High", at, new Vector2(40, 40), high.Id, priority: 5);
            }

            StepInPlaceAt(scene, entity, emitter, at);
            Check($"on overlap the higher priority wins ({(highFirst ? "higher first" : "lower first")} layout gives "
                + $"{emitter.LastStep?.SurfaceId})", emitter.LastStep?.SurfaceId == "t_high");
        }
    }

    private static void TestAreaIgnoredCases()
    {
        var floor = TestSurface("t_ign_floor", "Wood");
        var over = TestSurface("t_ign_over", "Snow");
        var at = new Vector2(70, 70);

        {
            var (scene, entity, _, emitter) = NewRig(floor.Id);
            MakeArea(scene, "Empty", at, new Vector2(40, 40), "", priority: 10);
            StepInPlaceAt(scene, entity, emitter, at);
            Check("an area with no surface is ignored (room default)", emitter.LastStep?.SurfaceId == "t_ign_floor");
        }

        {
            var (scene, entity, _, emitter) = NewRig(floor.Id);
            var area = MakeArea(scene, "Off", at, new Vector2(40, 40), over.Id, priority: 10);
            area.Enabled = false;
            StepInPlaceAt(scene, entity, emitter, at);
            Check("a disabled component's area is ignored", emitter.LastStep?.SurfaceId == "t_ign_floor");
        }

        {
            var (scene, entity, _, emitter) = NewRig(floor.Id);
            var area = MakeArea(scene, "Hidden", at, new Vector2(40, 40), over.Id, priority: 10);
            area.Entity.Active = false;
            StepInPlaceAt(scene, entity, emitter, at);
            Check("a disabled entity's area is ignored", emitter.LastStep?.SurfaceId == "t_ign_floor");
        }

        {
            var (scene, entity, _, emitter) = NewRig(floor.Id);
            var e = scene.CreateEntity("Shapeless");
            e.GetComponent<Transform>()!.Position = at;
            var area = e.AddComponent<SurfaceArea>();
            area.SurfaceId = over.Id; area.Priority = 10;
            scene.FlushPendingAdds();
            StepInPlaceAt(scene, entity, emitter, at);
            Check("an area with no collider is ignored (without crashing)", emitter.LastStep?.SurfaceId == "t_ign_floor");
        }
    }

    private static void TestTeleport()
    {
        var floor = TestSurface("t_tp_floor", "Wood");
        var zone = TestSurface("t_tp_zone", "Snow");

        var (scene, entity, _, emitter) = NewRig(floor.Id);
        MakeArea(scene, "Zone", new Vector2(500, 500), new Vector2(40, 40), zone.Id, priority: 10);

        StepInPlaceAt(scene, entity, emitter, new Vector2(0, 0));
        StepInPlaceAt(scene, entity, emitter, new Vector2(500, 500));
        Check($"warping from outside into an area immediately gives that surface ({emitter.LastStep?.SurfaceId})",
              emitter.LastStep?.SurfaceId == "t_tp_zone");

        StepInPlaceAt(scene, entity, emitter, new Vector2(-999, -999));
        Check($"warping from inside an area out immediately gives the room default ({emitter.LastStep?.SurfaceId})",
              emitter.LastStep?.SurfaceId == "t_tp_floor");
    }

    private static void TestMoveGuard()
    {
        var (scene, _, _, emitter) = NewRig(TestSurface("t_guard", "Wood").Id);
        for (int i = 0; i < 60 * 3; i++) scene.Update(Dt);
        Check($"a walk animation on the spot is silent ({emitter.StepCount} steps over three seconds)", emitter.StepCount == 0);

        var (scene2, entity2, _, emitter2) = NewRig(TestSurface("t_guard2", "Wood").Id);
        Walk(scene2, entity2, emitter2, 4);
        Check("it sounds while walking (the guard is not too broad)", emitter2.StepCount >= 4);

        int atStop = emitter2.StepCount;
        for (int i = 0; i < 60; i++) scene2.Update(Dt);
        int afterGrace = emitter2.StepCount;
        Check($"stopping goes silent after the grace ({emitter2.MoveGrace:0.00}s) (+{afterGrace - atStop} steps over one second stopped)",
              afterGrace - atStop <= 1);

        for (int i = 0; i < 120; i++) scene2.Update(Dt);
        Check($"standing still stays completely silent (+{emitter2.StepCount - afterGrace} more steps)",
              emitter2.StepCount == afterGrace);
    }

    private static void TestZombieSubscription()
    {
        var (scene, entity, anim, emitter) = NewRig(TestSurface("t_zombie", "Wood").Id);
        var transform = entity.GetComponent<Transform>()!;
        Walk(scene, entity, emitter, 2);
        int before = emitter.StepCount;

        entity.RemoveComponent(emitter);
        for (int i = 0; i < 60 * 3; i++)
        {
            transform.Position += new Vector2(0.4f, 0f);
            anim.Update(Dt);
        }
        Check($"removing the component stops the events (+{emitter.StepCount - before} steps after removal)",
              emitter.StepCount == before);

        var (scene2, entity2, _, emitter2) = NewRig(TestSurface("t_zombie2", "Wood").Id);
        Walk(scene2, entity2, emitter2, 2);
        emitter2.Enabled = false;
        int off = emitter2.StepCount;
        Walk(scene2, entity2, emitter2, 2);
        Check($"it does not sound while disabled (+{emitter2.StepCount - off} steps)", emitter2.StepCount == off);

        emitter2.Enabled = true;
        int on = emitter2.StepCount;
        var (l, r) = WalkCounting(scene2, entity2, emitter2, cycles: 3);
        Check($"re-enabling gives exactly two steps per cycle ({l + r} over three cycles - a double subscription would exceed six)",
              emitter2.StepCount - on == 6);
    }

    private static void TestListenerGate()
    {
        var am = AudioManager.Instance;
        var (scene, entity, _, emitter) = NewRig(TestSurface("t_gate", "Wood").Id);

        am.ListenerActive = false;
        Walk(scene, entity, emitter, 4);
        Check($"while editing (listener off) it is silent ({emitter.StepCount} steps)", emitter.StepCount == 0);

        am.ListenerActive = true;
        Walk(scene, entity, emitter, 2);
        Check("turning the listener on makes it sound again", emitter.StepCount > 0);
    }

    private static void TestPitchJitterRange()
    {
        var fresh = new SurfaceAsset();
        Check($"the default pitch jitter is in XNA units (+/-{fresh.PitchJitter:0.000} = playback rate +/-{MathF.Pow(2f, fresh.PitchJitter):0.000})",
              fresh.PitchJitter > 0.05f && fresh.PitchJitter < 0.09f);

        Check("the equivalent of a 1.05 playback rate is around 0.07 (why playback-rate numbers must not be copied across)",
              MathF.Abs(MathF.Log2(1.05f) - 0.07f) < 0.005f);

        TestPitchApplied();
    }

    private static void TestPitchApplied()
    {
        var dull = TestSurface("t_pitch", "Wood");
        dull.Pitch = -0.097f;
        dull.PitchJitter = 0.07f;

        var (scene, entity, _, emitter) = NewRig(dull.Id);
        var transform = entity.GetComponent<Transform>()!;

        float sum = 0f; int n = 0; bool inRange = true;
        float maxPitch = float.MinValue;
        for (int i = 0; i < 60 * 20; i++)
        {
            transform.Position += new Vector2(0.4f, 0f);
            int before = emitter.StepCount;
            scene.Update(Dt);
            if (emitter.StepCount == before) continue;

            float p = emitter.LastStep!.Value.Pitch;
            sum += p; n++;
            if (p > maxPitch) maxPitch = p;
            if (p < dull.Pitch - dull.PitchJitter - 1e-4f || p > dull.Pitch + dull.PitchJitter + 1e-4f)
                inRange = false;
        }

        float mean = n > 0 ? sum / n : 0f;
        Check($"every step is within [base +/- jitter] ({n} steps, mean {mean:0.000})", n >= 30 && inRange);

        float ceiling = dull.Pitch + dull.PitchJitter;
        Check($"on a surface with a negative base pitch no step can exceed 0 (ceiling {ceiling:0.000} < 0)",
              ceiling < 0f && maxPitch < 0f);
    }

    private static void TestSurfaceMigration()
    {
        var reg = AssetRegistry.Instance;
        const string FixtureId = "beef0001";
        const string FixturePath = "Audio/Test/Step.wav";
            var file = Path.Combine(Path.GetTempPath(), "MigrateTest.surface");

        try
        {
            reg.Register(FixtureId, FixturePath);

            File.WriteAllText(file, """
            {
              "id": "mig00001",
              "name": "MigrateTest",
              "clipsL": [ "Audio/Test/Step" ],
              "clipsR": [ "Audio/Test/missingSound" ],
              "volume": 1.0
            }
            """);

            SurfaceAsset? a = null;
            string bark = CaptureStderr(() => a = SurfaceAsset.Load(file));

            Check("premise: an old-format .surface opens", a != null);
            if (a == null) return;

            Check("* the old left-foot path becomes an id (extension-less notation searches for .wav)",
                  a.ClipIdsL.Count == 1 && a.ClipIdsL[0] == FixtureId);
            Check("* an unresolvable right-foot value is preserved (dropping it silently would make that foot alone silent)",
                  a.ClipIdsR.Count == 1 && a.ClipIdsR[0] == "Audio/Test/missingSound");
            Check("* an unresolvable value is reported, naming the file, surface and foot",
                  bark.Contains("✘") && bark.Contains("missingSound")
                  && bark.Contains("MigrateTest") && bark.Contains("right"));
            Check("* a resolved value is not reported (reporting successes turns warnings into background noise)",
                  !bark.Contains("Audio/Test/Step\""));

            a.Save(file);
            var text = File.ReadAllText(file);
            Check("* the re-saved file has no old clipsL/clipsR keys",
                  !text.Contains("\"clipsL\"") && !text.Contains("\"clipsR\""));
            Check("* the re-saved file has the new clipIdsL/clipIdsR keys",
                  text.Contains("\"clipIdsL\"") && text.Contains("\"clipIdsR\""));

            SurfaceLibrary.Put(a);
            var (scene, entity, _, emitter) = NewRig(a.Id);
            string stepBark = CaptureStderr(() => WalkCounting(scene, entity, emitter, 6));

            int barks = 0;
            foreach (var line in stepBark.Split('\n')) if (line.Contains("✘")) barks++;
            Check($"premise: the left foot really sounded over six cycles ({emitter.StepCount} steps)",
                  emitter.StepCount > 0);
            Check($"* an unresolvable clip stepped on for six cycles reports exactly once (latched) - {barks} lines", barks == 1);
            Check("* the reported line names which foot", stepBark.Contains("right foot"));
        }
        finally
        {
            reg.Remove(FixtureId);
            if (File.Exists(file)) File.Delete(file);
        }
    }

    private static string CaptureStderr(Action body)
    {
        var prev = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try { body(); }
        finally { Console.SetError(prev); }
        return sw.ToString();
    }

    private static SurfaceAsset TestSurface(string id, string folder)
    {
        var asset = new SurfaceAsset { Id = id, Name = id };
        for (int i = 1; i <= 3; i++)
        {
            asset.ClipIdsL.Add(ClipId(folder, $"L{i}"));
            asset.ClipIdsR.Add(ClipId(folder, $"R{i}"));
        }
        SurfaceLibrary.Put(asset);
        return asset;
    }

    private static string FixtureDir => Path.Combine(Path.GetTempPath(), "pixelcore_footstep_fixtures");

    private static string FixturePath(string folder, string clip) => Path.Combine(FixtureDir, folder, clip + ".wav");

    private static string ClipId(string folder, string clip)
        => $"f007{(folder == "Snow" ? 1 : 0)}{(clip[0] == 'L' ? 'a' : 'b')}{int.Parse(clip.AsSpan(1)):x2}";

    private static void WriteFixtureClips()
    {
        foreach (var folder in new[] { "Wood", "Snow" })
        {
            Directory.CreateDirectory(Path.Combine(FixtureDir, folder));
            for (int i = 1; i <= 3; i++)
                foreach (var clip in new[] { $"L{i}", $"R{i}" })
                {
                    File.WriteAllBytes(FixturePath(folder, clip), SilentWav(220));
                    AssetRegistry.Instance.Register(ClipId(folder, clip), FixturePath(folder, clip));
                }
        }
    }

    private static void RemoveFixtureClips()
    {
        foreach (var folder in new[] { "Wood", "Snow" })
            for (int i = 1; i <= 3; i++)
            {
                AssetRegistry.Instance.Remove(ClipId(folder, $"L{i}"));
                AssetRegistry.Instance.Remove(ClipId(folder, $"R{i}"));
            }
        try { Directory.Delete(FixtureDir, true); } catch (IOException) {  }
    }

    private static byte[] SilentWav(int samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int data = samples * 2;
        w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(22050); w.Write(22050 * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(data); w.Write(new byte[data]);
        w.Flush();
        return ms.ToArray();
    }

    private static void TestClipIdPrecondition()
    {
        foreach (var folder in new[] { "Wood", "Snow" })
        {
            int found = 0;
            for (int i = 1; i <= 3; i++)
                foreach (var clip in new[] { $"L{i}", $"R{i}" })
                    if (AssetRegistry.Instance.GetPath(ClipId(folder, clip)) is { } p && File.Exists(p)) found++;
            Check($"premise: {folder} fixture footsteps L1-3 and R1-3 resolve to files ({found}/6)", found == 6);
        }
    }

    private static SurfaceArea MakeArea(Scene scene, string name, Vector2 center, Vector2 size,
                                       string surfaceId, int priority)
    {
        var e = scene.CreateEntity(name);
        e.GetComponent<Transform>()!.Position = center;

        var box = e.AddComponent<BoxCollider2D>();
        box.Size = size;
        box.IsTrigger = true;

        var area = e.AddComponent<SurfaceArea>();
        area.SurfaceId = surfaceId;
        area.Priority = priority;

        scene.FlushPendingAdds();
        return area;
    }

    private static void StepInPlaceAt(Scene scene, Entity entity, FootstepEmitter emitter, Vector2 at)
    {
        var transform = entity.GetComponent<Transform>()!;
        int target = emitter.StepCount + 1;
        for (int i = 0; i < 60 * 10 && emitter.StepCount < target; i++)
        {
            transform.Position = at + new Vector2(i % 2 == 0 ? 0f : 0.05f, 0f);
            scene.Update(Dt);
        }
    }

    private static (Scene, Entity, Animator, FootstepEmitter) NewRig(string? surfaceId)
    {
        var scene = new Scene("footstep-test") { DefaultSurfaceId = surfaceId };
        var entity = scene.CreateEntity("Walker");

        var emitter = entity.AddComponent<FootstepEmitter>();
        var animator = entity.AddComponent<Animator>();
        animator.AddClip(WalkClip());
        scene.FlushPendingAdds();
        animator.Play("Walk");

        return (scene, entity, animator, emitter);
    }

    private static AnimationClip WalkClip()
    {
        var clip = new AnimationClip("Walk") { Loop = true };
        for (int i = 0; i < 6; i++)
        {
            string? evt = i switch
            {
                2 => FootstepEmitter.StepLeft,
                5 => FootstepEmitter.StepRight,
                _ => null,
            };
            clip.AddFrame(new AnimationFrame(new Rectangle(0, 0, 48, 48), FrameDur, evt));
        }
        return clip;
    }

    private static void Walk(Scene scene, Entity entity, FootstepEmitter emitter, int steps)
    {
        var transform = entity.GetComponent<Transform>()!;
        int target = emitter.StepCount + steps;
        for (int i = 0; i < 60 * 10 && emitter.StepCount < target; i++)
        {
            transform.Position += new Vector2(0.4f, 0f);
            scene.Update(Dt);
        }
    }

    private static (int left, int right) WalkCounting(Scene scene, Entity entity,
                                                     FootstepEmitter emitter, int cycles)
    {
        var transform = entity.GetComponent<Transform>()!;
        int left = 0, right = 0;
        for (int i = 0; i < 60 * cycles; i++)
        {
            transform.Position += new Vector2(0.4f, 0f);
            int before = emitter.StepCount;
            scene.Update(Dt);
            if (emitter.StepCount == before) continue;
            if (emitter.LastStep!.Value.Left) left++; else right++;
        }
        return (left, right);
    }

    private static void Check(string label, bool ok)
    {
        if (ok) { _pass++; Console.WriteLine($"  PASS  {label}"); }
        else { _fail++; Console.WriteLine($"  FAIL  {label}"); }
    }
}
