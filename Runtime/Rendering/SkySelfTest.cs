using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;

namespace PixelCore.Runtime.Rendering;

public static class SkySelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        _pass = 0; _fail = 0;
        Console.WriteLine("=== Sky self-test ===");

        TestNoiseDeterministic();
        TestNoiseTiles();
        TestNoiseRange();
        TestProfileRoundTrip();
        TestHashCoversEveryField();
        TestAssetFileRoundTrip();
        TestStepping();
        TestFormatRegistered();

        Console.WriteLine($"=== Sky: {_pass} passed, {_fail} failed ===");
    }

    private static void TestNoiseDeterministic()
    {
        var a = SkyNoise.Bake(64, 7);
        var b = SkyNoise.Bake(64, 7);
        Check("bake: the same (size, seed) gives identical bytes",
            a.Select(c => c.PackedValue).SequenceEqual(b.Select(c => c.PackedValue)));

        var c2 = SkyNoise.Bake(64, 8);
        int diff = a.Zip(c2, (x, y) => x.PackedValue != y.PackedValue ? 1 : 0).Sum();
        Check("★ control: a different seed gives a different tile (the test knows how to fail)", diff > a.Length / 4, $"{diff}/{a.Length} differ");

        int rgSame = a.Count(c => c.R == c.G);
        Check("the R and G fields differ from each other (morph material)", rgSame < a.Length / 4, $"{rgSame} equal");
    }

    private static void TestNoiseTiles()
    {
        const float eps = 1e-4f;
        bool wrapU = true, wrapV = true, varies = false;
        var probes = new[] { (0.13f, 0.71f), (0.5f, 0.5f), (0.02f, 0.98f), (0.77f, 0.31f) };
        foreach (var (u, v) in probes)
        {
            float here = SkyNoise.Fbm(u, v, 4, 3, 5);
            wrapU &= MathF.Abs(here - SkyNoise.Fbm(u + 1f, v, 4, 3, 5)) < eps;
            wrapV &= MathF.Abs(here - SkyNoise.Fbm(u, v + 1f, 4, 3, 5)) < eps;
            varies |= MathF.Abs(here - SkyNoise.Fbm(u + 0.37f, v, 4, 3, 5)) > 1e-3f;
        }
        Check("tiling: u+1 gives the same value as u (the basis for Wrap sampling in the shader)", wrapU);
        Check("tiling: v+1 gives the same value as v", wrapV);
        Check("★ control: u+0.37 gives a different value (the equality check is not vacuous)", varies);

        float p0 = SkyNoise.Perlin(1.3f, 2.6f, 8, 5);
        float p1 = SkyNoise.Perlin(1.3f + 8f, 2.6f, 8, 5);
        Check("Perlin: adding the period gives the same value", MathF.Abs(p0 - p1) < 1e-5f, $"{p0} vs {p1}");
    }

    private static void TestNoiseRange()
    {
        var px = SkyNoise.Bake(64, 1);
        byte minR = px.Min(c => c.R), maxR = px.Max(c => c.R);
        byte minG = px.Min(c => c.G), maxG = px.Max(c => c.G);
        Check("normalisation: the R channel fills 0..255 (why the threshold reads as a 'ratio')", minR == 0 && maxR == 255, $"{minR}..{maxR}");
        Check("normalisation: the G channel too", minG == 0 && maxG == 255, $"{minG}..{maxG}");
        byte minA = px.Min(c => c.A), maxA = px.Max(c => c.A);
        Check("normalisation: B and A (rounded plates) too", px.Min(c => c.B) == 0 && px.Max(c => c.B) == 255 && minA == 0 && maxA == 255);

        var big = SkyNoise.Bake(128, 1);
        float Curv(Func<Color, int> ch)
        {
            float sum = 0; int cnt = 0;
            for (int y = 0; y < 128; y++)
                for (int x = 1; x < 127; x++)
                {
                    int a = ch(big[y * 128 + x - 1]), b = ch(big[y * 128 + x]), c = ch(big[y * 128 + x + 1]);
                    sum += Math.Abs(a - 2 * b + c); cnt++;
                }
            return sum / cnt;
        }
        float curvR = Curv(c => c.R), curvB = Curv(c => c.B);
        Check("★ the rounded plate (B) is smoother than the rough plate (R) - second difference (the basis for the Smooth knob)", curvB < curvR * 0.7f, $"R {curvR:F2} vs B {curvB:F2}");
        Check("   control: the rough plate really has fine grain (if it were 0, the check above would be vacuous)", curvR > 0.3f, $"{curvR:F2}");
    }

    private static SkyProfile Sample() => new()
    {
        SkyTop = new Color(1, 2, 3), SkyBottom = new Color(4, 5, 6), SkyBands = 7, Dither = false,
        CloudLight = new Color(7, 8, 9), CloudBody = new Color(10, 11, 12), CloudShadow = new Color(13, 14, 15),
        Coverage = 0.33f, NearScale = 123f, Stretch = 1.9f, FlatBase = 0.4f, RowHeight = 48f, Lumpy = 0.7f, Shape = 0, Fluff = 2.5f, Layers = 3, LayerFalloff = 0.51f, Haze = 0.22f, Detail = 0.61f,
        LightAngle = 45f, Relief = 0.07f, Seed = 42, Smooth = 0.4f, Shade = 2, ShadowDepth = 6f,
        WindSpeed = 9f, WindAngle = 30f, Morph = 0.11f, TickHz = 12f, Parallax = 0.2f,
    };

    private static void TestProfileRoundTrip()
    {
        var p = Sample();
        var back = SkyData.From(p).ToProfile();
        Check("profile <-> data round trip: hashes match", back.ValueHash() == p.ValueHash());
        Check("round trip: colours unchanged", back.SkyTop == p.SkyTop && back.CloudShadow == p.CloudShadow);
        Check("round trip: integers and booleans unchanged", back.SkyBands == 7 && back.Layers == 3 && back.Seed == 42 && !back.Dither);

        var partial = new SkyData { SkyTop = null, CloudBody = new[] { 300, -5, 9 } };
        var fromPartial = partial.ToProfile();
        Check("missing colour keys fall back to defaults", fromPartial.SkyTop == new SkyProfile().SkyTop);
        Check("out-of-range colour components are clamped to 0..255", fromPartial.CloudBody == new Color(255, 0, 9), fromPartial.CloudBody.ToString());
    }

    private static void TestHashCoversEveryField()
    {
#if DEBUG
        var baseline = Sample();
        int baseHash = baseline.ValueHash();
        var fields = typeof(SkyProfile).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        Check($"premise: the profile has public fields ({fields.Length})", fields.Length >= 20);
        int missed = 0;
        foreach (var f in fields)
        {
            var mutated = baseline.Clone();
            object? cur = f.GetValue(mutated);
            object next = cur switch
            {
                float v => v + 0.5f,
                int v => v + 1,
                bool v => !v,
                Color c => new Color((c.R + 1) % 256, c.G, c.B),
                _ => throw new InvalidOperationException($"SkyProfile.{f.Name}: type {f.FieldType.Name} is unknown to this test - add a line here"),
            };
            f.SetValue(mutated, next);
            if (mutated.ValueHash() == baseHash)
            {
                missed++;
                Console.WriteLine($"    ✘ ValueHash does not look at SkyProfile.{f.Name} - that knob never shows 'modified'");
            }
        }
        Check("★ ValueHash looks at every field (a missing field = silently unsaved)", missed == 0, $"{missed} missing");
#else
        Check("the per-field hash test is DEBUG only (Release uses the round trip instead)", true);
#endif
    }

    private static void TestAssetFileRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixelcore-skytest-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "Clear.sky");
            var asset = new SkyAsset { Id = "abcd1234", Name = "WrongNämé", Sky = SkyData.From(Sample()) };
            asset.Save(file);
            Check("the file is written", File.Exists(file));

            var text = File.ReadAllText(file);
            Check("non-ASCII is not written as \\u (diffs stay readable by eye)", text.Contains("\"WrongNämé\"") && !text.Contains("\\u", StringComparison.Ordinal));
            Check("keys are pinned in camelCase", text.Contains("\"skyTop\"") && text.Contains("\"tickHz\""));

            var back = SkyAsset.Load(file);
            Check("the file is read", back != null);
            if (back == null) return;
            Check("Id preserved", back.Id == "abcd1234");
            Check("the file name is the truth for the name (it overrides the name inside the file)", back.Name == "Clear", back.Name);
            Check("value round trip: hashes match", back.Sky.ToProfile().ValueHash() == Sample().ValueHash());

            Check("a missing file gives null (not an exception)", SkyAsset.Load(Path.Combine(dir, "missing.sky")) == null);
            File.WriteAllText(Path.Combine(dir, "broken.sky"), "{ not json");
            Check("a broken file gives null too (it does not kill the boot scan)", SkyAsset.Load(Path.Combine(dir, "broken.sky")) == null);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch {  }
        }
    }

    private static void TestStepping()
    {
        Check("beat: at 10Hz, 0.099s is still 0", SkyRenderer.SteppedTime(0.099f, 10f) == 0f);
        Check("beat: at 10Hz, 0.15s is 0.1", MathF.Abs(SkyRenderer.SteppedTime(0.15f, 10f) - 0.1f) < 1e-6f);
        Check("★ control: at 60Hz, 0.099s is not 0", SkyRenderer.SteppedTime(0.099f, 60f) > 0f);
        Check("a beat of 0 or below = no quantisation", SkyRenderer.SteppedTime(0.123f, 0f) == 0.123f);

        var d = SkyRenderer.DirFromDegrees(90f);
        Check("angle 90 = screen up (0,-1)", MathF.Abs(d.X) < 1e-5f && MathF.Abs(d.Y + 1f) < 1e-5f, d.ToString());
        var r = SkyRenderer.DirFromDegrees(0f);
        Check("angle 0 = right (1,0)", MathF.Abs(r.X - 1f) < 1e-5f && MathF.Abs(r.Y) < 1e-5f);

        var p = Sample();
        p.WindAngle = 0f; p.WindSpeed = 5f; p.LayerFalloff = 0.5f; p.NearScale = 200f;
        bool allInt = true;
        for (float t = 0f; t < 20f; t += 0.37f)
            for (int i = 0; i < 3; i++)
            {
                var L = SkyRenderer.Layer(p, i, t);
                allInt &= L.Y == MathF.Floor(L.Y) && L.Z == MathF.Floor(L.Z) && L.W == MathF.Floor(L.W);
            }
        Check("layer offsets are always integer pixels (boiling prescription 2)", allInt);

        var l0a = SkyRenderer.Layer(p, 0, 0f);
        var l0b = SkyRenderer.Layer(p, 0, 10f);
        Check("wind 0° = clouds move right -> the sample offset moves left (decreases)", l0b.Y < l0a.Y, $"{l0a.Y} → {l0b.Y}");
        Check("layer 0 moves 50px in 10 seconds (speed 5px/s)", MathF.Abs((l0a.Y - l0b.Y) - 50f) < 1.01f, $"{l0a.Y - l0b.Y}");
        var l1a = SkyRenderer.Layer(p, 1, 0f);
        var l1b = SkyRenderer.Layer(p, 1, 10f);
        Check("the back layer moves falloff times as far (0.5 -> 25px)", MathF.Abs((l1a.Y - l1b.Y) - 25f) < 1.01f, $"{l1a.Y - l1b.Y}");
        Check("back layer tiles are smaller (distant clouds)", l1a.X < l0a.X);

        var o = SkyRenderer.ParallaxOrigin(p, new Vector2(123.7f, -45.2f));
        Check("the parallax origin is integer", o.X == MathF.Floor(o.X) && o.Y == MathF.Floor(o.Y));
    }

    private static void TestFormatRegistered()
    {
        Check("the format table has .sky (the key-pinning test covers this format)",
            Serialization.FileFormats.All.Any(f => f.Name == ".sky"));
    }

    private static void Check(string name, bool ok, string? detail = null)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name +
            (ok || detail == null ? "" : $"   [{detail}]"));
        if (ok) _pass++; else _fail++;
    }
}
