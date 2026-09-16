using System;
using System.IO;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Runtime.Rendering;

public static class PostFxSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        _pass = 0; _fail = 0;
        Console.WriteLine("=== Effects volume (FxStack, grain, LUT) self-test ===");

        TestStackTopWins();
        TestStackPopById();
        TestStackReset();
        TestNeedsPostSeesStack();
        TestGrainStaircase();
        TestGrainCells();
        TestGrainIgnoresTimeScale();
        TestMovedKeysBark();
        TestLutTextureCounts();
        TestLutTextureRoundTrip();

        Console.WriteLine($"=== PostFx: {_pass} passed, {_fail} failed ===");
    }

    private static void TestStackTopWins()
    {
        FxStack.ResetAll();
        Check("① an empty stack is neutral", FxStack.Current.IsNeutral);

        FxStack.SetWeather(new FxLayer { FogOpacity = 0.2f, ChromAb = 0.1f, FogColor = Color.White });
        Check("① with only weather applied, the weather shows",
            FxStack.Current.FogOpacity == 0.2f && FxStack.Current.ChromAb == 0.1f);

        int id = FxStack.Push(new FxLayer { FogOpacity = 0.3f, ChromAb = 0.5f });
        Check("★ ① when layered, the top wins (fog 0.3 - not the sum 0.5)",
            FxStack.Current.FogOpacity == 0.3f, $"measured {FxStack.Current.FogOpacity}");
        Check("★ ① chromatic aberration is the top value too (0.5 - not 0.6)",
            FxStack.Current.ChromAb == 0.5f, $"measured {FxStack.Current.ChromAb}");

        FxStack.Pop(id);
        int only = FxStack.Push(new FxLayer { ChromAb = 0.5f });
        Check("★ ① an axis the top layer does not use does not leak up from below (fog 0)",
            FxStack.Current.FogOpacity == 0f, $"measured {FxStack.Current.FogOpacity}");
        FxStack.Pop(only);
        FxStack.ResetAll();
    }

    private static void TestStackPopById()
    {
        FxStack.ResetAll();
        int a = FxStack.Push(new FxLayer { ChromAb = 0.1f });
        int b = FxStack.Push(new FxLayer { ChromAb = 0.2f });
        Check("② the ids differ", a != b, $"{a} / {b}");

        Check("★ ② with two layers the top (pushed later) wins (0.2 - with bottom-wins it would be 0.1)",
            FxStack.Current.ChromAb == 0.2f, $"measured {FxStack.Current.ChromAb}");

        FxStack.Pop(a);
        Check("★ ② removing the lower layer keeps the upper one (0.2 - with remove-top it would be 0.1)",
            FxStack.Current.ChromAb == 0.2f, $"measured {FxStack.Current.ChromAb}");
        Check("② one layer remains", FxStack.PushedCount == 1, $"{FxStack.PushedCount}");

        FxStack.Pop(a);
        Check("② removing the same id again does not pull out another's layer",
            FxStack.PushedCount == 1 && FxStack.Current.ChromAb == 0.2f);

        FxStack.Pop(b);
        Check("② removing everything is neutral", FxStack.Current.IsNeutral && FxStack.PushedCount == 0);
        FxStack.ResetAll();
    }

    private static void TestStackReset()
    {
        FxStack.ResetAll();
        FxStack.SetWeather(new FxLayer { FogOpacity = 0.25f });
        FxStack.Push(new FxLayer { ChromAb = 0.5f });
        Check("③ before rewind: it is polluted (premise - otherwise the check below is vacuous)",
            !FxStack.Current.IsNeutral && FxStack.HasWeather && FxStack.PushedCount == 1);

        FxStack.ResetAll();
        Check("★ ③ ResetAll clears both the event layers and the weather",
            FxStack.Current.IsNeutral && !FxStack.HasWeather && FxStack.PushedCount == 0);
    }

    private static void TestNeedsPostSeesStack()
    {
        var neutral = new PostProfile();
        Check("④ when both are neutral, no post pass",
            !PostProcessor.NeedsPost(neutral, FxLayer.Neutral));

        Check("④ when the profile touches colour, the post pass runs",
            PostProcessor.NeedsPost(new PostProfile { Vignette = 0.3f }, FxLayer.Neutral));

        Check("★ ④ neutral profile + stack chromatic aberration -> post runs (looking only at the profile kills this)",
            PostProcessor.NeedsPost(neutral, new FxLayer { ChromAb = 0.2f }));
        Check("★ ④ neutral profile + stack fog -> post runs",
            PostProcessor.NeedsPost(neutral, new FxLayer { FogOpacity = 0.2f }));
        Check("★ ④ neutral profile + stack lens distortion -> post runs",
            PostProcessor.NeedsPost(neutral, new FxLayer { LensDistortion = -0.3f }));
    }

    private static void TestGrainStaircase()
    {
        Check("⑤ within the same step the seed does not change",
            PostProcessor.GrainSeedFor(0f) == PostProcessor.GrainSeedFor(0.124f));
        Check("★ ⑤ crossing a step changes it (0.124 -> 0.125)",
            PostProcessor.GrainSeedFor(0.124f) != PostProcessor.GrainSeedFor(0.125f));

        var seen = new System.Collections.Generic.HashSet<float>();
        for (int i = 0; i < 600; i++) seen.Add(PostProcessor.GrainSeedFor(i / 600f));
        Check("★ ⑤ exactly 8 steps in one second (even with 600 frames)", seen.Count == 8, $"measured {seen.Count} steps");

        Check("⑤ wraps at 64 seconds (float precision - at large values the steps get mushy)",
            PostProcessor.GrainSeedFor(64f) == PostProcessor.GrainSeedFor(0f));
    }

    private static void TestGrainIgnoresTimeScale()
    {
        const float scale = 16f;
        var wall = new System.Collections.Generic.HashSet<float>();
        var game = new System.Collections.Generic.HashSet<float>();
        for (int i = 0; i < 2000; i++)
        {
            float w = i / 2000f;
            wall.Add(PostProcessor.GrainSeedFor(w));
            game.Add(PostProcessor.GrainSeedFor(w * scale));
        }
        Check("★ ⑥ one wall-clock second = 8 steps (unchanged at TimeScale 16)", wall.Count == 8, $"measured {wall.Count}");
        Check("★ ⑥ control: feeding the scaled clock gives 128 steps (which is why it must be the wall clock)",
            game.Count == 128, $"measured {game.Count}");

#if DEBUG
        const string g1 = "Game1.cs";
        if (File.Exists(g1))
        {
            var src = File.ReadAllText(g1);
            Check("★ ⑥ wiring: _totalSeconds comes from GameTime.TotalGameTime (real time)",
                src.Contains("_totalSeconds = (float)gameTime.TotalGameTime.TotalSeconds"));
        }
        else Check("★ ⑥ wiring: Game1.cs was read", false, g1);
#endif
    }

    private static void TestGrainCells()
    {
        Console.WriteLine("--- Grain cells ---");

        var hd = PostProcessor.GrainCellsFor(new Rectangle(0, 0, 1920, 1080), 720f);
        Check($"★ the horizontal cell count is the profile value as is ({hd.X:0})", hd.X == 720f, hd.X.ToString());
        Check($"★ the vertical count comes from the aspect ratio so cells are square ({hd.Y:0} = 720×9/16)",
            MathF.Abs(hd.Y - 405f) < 0.5f, hd.Y.ToString("0.0"));
        Check($"   so the cells are square ({1920f / hd.X:0.00}px wide × {1080f / hd.Y:0.00}px tall)",
            MathF.Abs(1920f / hd.X - 1080f / hd.Y) < 0.01f);

        var uhd = PostProcessor.GrainCellsFor(new Rectangle(0, 0, 3840, 2160), 720f);
        Check("★ the cell count is the same at 4K too (cells get physically larger)", uhd == hd, $"{uhd} vs {hd}");

        var sig = typeof(PostProcessor).GetMethod(nameof(PostProcessor.GrainCellsFor))!;
        var names = Array.ConvertAll(sig.GetParameters(), q => q.Name ?? "");
        Check($"★ the cell computation does not take the pixel-art scale as a parameter ({string.Join(", ", names)})",
            Array.IndexOf(names, "dotScale") < 0 && names.Length == 2, string.Join(",", names));

        var compose = typeof(PostProcessor).GetMethod(nameof(PostProcessor.Compose))!;
        Check("★ Compose has no pixel-art scale parameter either (if it did, there would be a place for it to come back)",
            Array.TrueForAll(compose.GetParameters(), q => q.Name != "dotScale"),
            string.Join(",", Array.ConvertAll(compose.GetParameters(), q => q.Name ?? "")));

        var coarse = PostProcessor.GrainCellsFor(new Rectangle(0, 0, 1920, 1080), 480f);
        Check("   control: fewer cells make larger cells (so returning a constant does not pass)",
            coarse.X == 480f && 1920f / coarse.X > 1920f / hd.X);

        Check("★ pure black stays pure black however hard it is shaken (the dodge is a product)",
            ColorDodge(0f, 1f * 0.03f) == 0f && ColorDodge(0f, 1f) == 0f);
        Check("   control: bright areas really do get brighter (so returning 0 does not pass)",
            ColorDodge(0.5f, 0.03f) > 0.5f, ColorDodge(0.5f, 0.03f).ToString("0.0000"));
        Check($"★ the cap is 3% of intensity (0.5 -> {ColorDodge(0.5f, 0.03f):0.0000})",
            ColorDodge(0.5f, 0.03f) < 0.5f * 1.031f);
        Check("   the protection code is gone from the source (the cause was fixed, not the symptom)",
            !System.IO.File.ReadAllText("Content/Shaders/PostCompose.fx").Contains("protectBlack"));
    }

    private static float ColorDodge(float b, float blend) => MathF.Min(b / MathF.Max(1f - blend, 1e-4f), 1f);

    private static void TestMovedKeysBark()
    {
        var prev = Console.Error;
        var buf = new StringWriter();
        Console.SetError(buf);
        try
        {
            PostData.ResetMovedWarnings();
            PostData.WarnMovedKeys("{\"profile\":{\"chromAb\":0.2,\"vignette\":0.4}}", "testA");
            var said = buf.ToString();
            Check("⑦ warns when a non-zero old key is present", said.Contains("chromAb"), said.Trim());
            Check("★ ⑦ says where it went (FxStack, cutscenes/weather)",
                said.Contains("FxStack") && said.Contains("cutscene"), said.Trim());
            Check("⑦ states the value too (needed to bring it back)", said.Contains("0.2"), said.Trim());

            buf.GetStringBuilder().Clear();
            PostData.WarnMovedKeys("{\"profile\":{\"chromAb\":0.2}}", "testA");
            Check("⑦ does not warn twice for the same file", buf.ToString().Length == 0);

            buf.GetStringBuilder().Clear();
            PostData.WarnMovedKeys("{\"profile\":{\"chromAb\":0,\"lensDistortion\":0}}", "testB");
            Check("★ ⑦ a file with only default (0) keys stays quiet (if 8 scenes warned every time, nobody would read it)",
                buf.ToString().Length == 0, buf.ToString().Trim());

            buf.GetStringBuilder().Clear();
            PostData.WarnMovedKeys(
                "{\"postPresets\":[{\"chromAb\":0},{\"chromAb\":0.3}]}", "testC");
            Check("★ ⑦ caught even when the first is 0 and a later one is non-zero (looking only at the first match leaks)",
                buf.ToString().Contains("0.3"), buf.ToString().Trim());

            buf.GetStringBuilder().Clear();
            PostData.WarnMovedKeys("{\"profile\":{\"grainResponse\":0.8,\"grainSize\":1.6}}", "testE");
            var g = buf.ToString();
            Check("★ ⑦ the grain keys whose meaning disappeared also warn (response, size)",
                g.Contains("grainResponse") && g.Contains("grainSize"), g.Trim());

            buf.GetStringBuilder().Clear();
            PostData.WarnMovedKeys("{\"postFogOpacity\":0.5}", "testD");
            Check("★ ⑦ the v1 flat key postFogOpacity is a different key (the opening and closing quotes are measured too)",
                buf.ToString().Length == 0, buf.ToString().Trim());
        }
        finally { Console.SetError(prev); PostData.ResetMovedWarnings(); }
    }

    private static void TestLutTextureCounts()
    {
        var plain = new PostProfile();
        Check("⑧ control: with every value at its default the colour group is neutral", !plain.HasColorGrading && plain.IsNeutral);

        var lut = new PostProfile { LutTextureId = "abc12345" };
        Check("★ ⑧ a file LUT counts as colour grading", lut.HasColorGrading);
        Check("★ ⑧ so it takes the post pass", !lut.IsNeutral);

        Check("★ ⑧ the colour hash includes the LUT id (otherwise changing the id would not rebind it)",
            plain.ColorHash() != lut.ColorHash());
        Check("⑧ different ids give different hashes",
            lut.ColorHash() != new PostProfile { LutTextureId = "zzz99999" }.ColorHash());

        var a = new PostProfile { LutTextureId = "aaaaaaaa" };
        var b = new PostProfile { LutTextureId = "bbbbbbbb" };
        Check("⑧ Lerp switches the LUT at the halfway point (it is not a blendable value)",
            PostProfile.Lerp(a, b, 0.4f).LutTextureId == "aaaaaaaa" &&
            PostProfile.Lerp(a, b, 0.6f).LutTextureId == "bbbbbbbb");
    }

    private static void TestLutTextureRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_lut_rt");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "roundtrip.post");
        try
        {
            var asset = new Assets.PostAsset
            {
                Id = "deadbeef",
                Name = "roundtrip",
                Profile = PostData.From(new PostProfile { LutTextureId = "c0ffee00", Vignette = 0.25f }),
            };
            asset.Save(file);

            var bytes = File.ReadAllText(file);
            Check("★ ⑨ lutTexture is actually written to the file", bytes.Contains("\"lutTexture\": \"c0ffee00\""),
                bytes.Length > 400 ? bytes[..400] : bytes);
            Check("★ ⑨ removed keys are not written to the file (chromAb, fog*)",
                !bytes.Contains("chromAb") && !bytes.Contains("fogOpacity"));

            var back = Assets.PostAsset.Load(file);
            Check("⑨ reading it back gives the same id", back?.Profile.ToProfile().LutTextureId == "c0ffee00");
            Check("⑨ the other values are unchanged too", back?.Profile.ToProfile().Vignette == 0.25f);

            var plain = new Assets.PostAsset { Id = "cafe0001", Name = "empty", Profile = PostData.From(new PostProfile()) };
            var file2 = Path.Combine(dir, "empty.post");
            plain.Save(file2);
            Check("★ ⑨ without a LUT the key is not written at all",
                !File.ReadAllText(file2).Contains("lutTexture"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static void Check(string name, bool ok, string? detail = null)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name +
            (ok || detail == null ? "" : $"   [{detail}]"));
        if (ok) _pass++; else _fail++;
    }
}
