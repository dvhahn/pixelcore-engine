using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Runtime.Rendering;

public static class LightingSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        _pass = 0; _fail = 0;
        Console.WriteLine("=== Lighting self-test ===");

        TestTextureLightRoundTrip();
        TestAmbientTransition();
        TestAmbientNeverBaked();
        TestReadabilityFloor();
        TestLightDimming();
        TestFrameResolve();
        TestLightContribution();
        TestScenePasses();
        TestCompositeOrder();
        TestAdditiveNegativeRoundTrip();
        TestAuthoringDefaults();
        TestRadialFalloff();
        TestRectBake();
        TestRectSofteningRoundTrip();
        TestProfileTable();
        TestProfileCurve();
        TestProfileBlend();
        TestLightSwitching();
        TestActiveWhenRoundTrip();
        TestGroundShadow();
        TestShadowPlate();
        TestShadowAlphaForce();
        TestShadowBands();
        TestShadowTextureRoundTrip();
        TestShadowShapeRoundTrip();
#if DEBUG
        TestTextureRefMissingBarks();
#endif

        Console.WriteLine($"=== Lighting: {_pass} passed, {_fail} failed ===");
    }

    private static void TestTextureLightRoundTrip()
    {
        Console.WriteLine("--- Texture light round trip ---");

        const string decal = "Sprites/Test/WindowPool.png";

        var src = new Scene("TextureLight");
        var e = src.CreateEntity("WindowLight");
        var l = e.AddComponent<Light2D>();
        l.Shape = LightShape.Texture;
        l.TexturePath = decal;
        l.Rotation = 24f;
        l.Scale = new Vector2(2.5f, 1.25f);
        l.Color = new Color(120, 160, 255);
        l.Intensity = 1.75f;

        src.Update(0f);

        var id = Assets.AssetRegistry.Instance.GetOrCreateId(decal);

        var path = Path.Combine(Path.GetTempPath(), "pc_lighting_" + Guid.NewGuid().ToString("N")[..8] + ".scene");
        try
        {
            SceneSerializer.SaveToFile(SceneSerializer.ToData(src), path);

            var bytes = File.ReadAllText(path);
            Check("★ the file carries an id, not a path",
                bytes.Contains($"\"texture\": \"{id}\"") && !bytes.Contains(decal),
                bytes.Contains(decal) ? "a path is in the file" : "the id is missing");

            var data = SceneSerializer.LoadFromFile(path);
            var dst = new Scene("Loaded");
            if (data != null) SceneSerializer.FromData(dst, data);
            dst.Update(0f);
            var rl = dst.FindEntity("WindowLight")?.GetComponent<Light2D>();

            Check("shape is Texture", rl != null && rl.Shape == LightShape.Texture);
            Check("the path is restored from the id", rl != null && rl.TexturePath == decal, rl?.TexturePath ?? "(null)");
            Check("rotation and scale are restored", rl != null && rl.Rotation == 24f
                && rl.Scale == new Vector2(2.5f, 1.25f), rl?.Scale.ToString());
            Check("tint and intensity are restored", rl != null && rl.Color == new Color(120, 160, 255) && rl.Intensity == 1.75f);
            Check("the unresolved-reference slot is empty, because it resolved",
                rl != null && string.IsNullOrEmpty(rl.UnresolvedTextureRef));
        }
        finally { try { File.Delete(path); } catch { } }

        var plain = new Scene("PlainLight");
        plain.CreateEntity("PointLight").AddComponent<Light2D>();
        plain.Update(0f);
        var plainPath = Path.Combine(Path.GetTempPath(), "pc_lighting_plain_" + Guid.NewGuid().ToString("N")[..8] + ".scene");
        try
        {
            SceneSerializer.SaveToFile(SceneSerializer.ToData(plain), plainPath);
            var text = File.ReadAllText(plainPath);
            Check("★ a light with no picture omits the texture key", !text.Contains("\"texture\""),
                text.Contains("\"texture\"") ? "the key was written" : null);
        }
        finally { try { File.Delete(plainPath); } catch { } }
    }

    private static void TestAmbientTransition()
    {
        Console.WriteLine("--- room light level transitions ---");

        var lightsOff = new Color(25, 59, 70);
        var lightsOn = new Color(210, 205, 180);

        var s = new Scene("Bathroom") { AmbientLight = lightsOn };
        s.SetAmbient(lightsOff);
        Check("immediate mode: the effective level is the target at once", s.EffectiveAmbient == lightsOff, s.EffectiveAmbient.ToString());
        Check("★ the authored value is untouched (immediate mode)", s.AmbientLight == lightsOn, s.AmbientLight.ToString());

        s.ResetAmbient();
        s.SetAmbient(lightsOff, 1f);
        Check("fade start: still near the authored value", s.EffectiveAmbient == lightsOn, s.EffectiveAmbient.ToString());
        s.UpdateAmbient(0.5f);
        var mid = s.EffectiveAmbient;
        Check("mid-fade: between the two colours", mid != lightsOn && mid != lightsOff && mid.B > lightsOff.B && mid.B < lightsOn.B,
            mid.ToString());
        s.UpdateAmbient(0.5f);
        Check("fade end: exactly the target", s.EffectiveAmbient == lightsOff, s.EffectiveAmbient.ToString());
        s.UpdateAmbient(10f);
        Check("running further after the end is a no-op", s.EffectiveAmbient == lightsOff);
        Check("★ the authored value is untouched (fading)", s.AmbientLight == lightsOn);

        s.ResetAmbient();
        s.SetAmbient(lightsOff, 0.2f);
        s.UpdateAmbient(5f);
        Check("one long tick still lands exactly on the target", s.EffectiveAmbient == lightsOff, s.EffectiveAmbient.ToString());

        s.ResetAmbient();
        s.SetAmbient(lightsOff, 1f);
        s.UpdateAmbient(0.5f);
        var seen = s.EffectiveAmbient;
        s.SetAmbient(lightsOn, 1f);
        Check("★ a repeat call continues from the colour on screen (no jump)", s.EffectiveAmbient == seen,
            $"was showing {seen}, now {s.EffectiveAmbient}");
        Check("   control: that colour differs from the authored value (otherwise the check above is vacuous)", seen != lightsOn);

        s.ResetAmbient();
        Check("releasing returns to the authored value", s.EffectiveAmbient == lightsOn && s.AmbientRuntime == null);
        s.UpdateAmbient(1f);
        Check("★ releasing discards a fade in progress (it does not come back)", s.EffectiveAmbient == lightsOn,
            s.EffectiveAmbient.ToString());

        var outdoor = new Scene("Street") { Exterior = true, AmbientLight = lightsOn };
        var bark = CaptureStderrLocal(() => outdoor.SetAmbient(lightsOff));
        Check("★ an exterior scene complains", bark.Contains("exterior scene") && bark.Contains("Street"), bark);
        Check("★ an exterior scene refuses (not a silent success)", outdoor.AmbientRuntime == null);
    }

    private static void TestAmbientNeverBaked()
    {
        Console.WriteLine("--- the runtime light level does not harden into the file ---");

        var authored = new Color(210, 205, 180);
        var runtimeAmb = new Color(25, 59, 70);

        var s = new Scene("Bathroom") { LightingEnabled = true, AmbientLight = authored };
        s.Update(0f);
        s.SetAmbient(runtimeAmb);

        var path = Path.Combine(Path.GetTempPath(), "pc_ambient_" + Guid.NewGuid().ToString("N")[..8] + ".scene");
        try
        {
            SceneSerializer.SaveToFile(SceneSerializer.ToData(s), path);
            var text = File.ReadAllText(path);

            Check("★ the file carries the authored value (210,205,180)",
                text.Contains("\"ambientR\": 210") && text.Contains("\"ambientG\": 205") && text.Contains("\"ambientB\": 180"),
                "the runtime level hardened");
            Check("   control: the runtime value appears nowhere in the file (otherwise the check above is vacuous)",
                !text.Contains("\"ambientR\": 25"));

            var data = SceneSerializer.LoadFromFile(path);
            var dst = new Scene("LoadedRoom");
            dst.SetAmbient(runtimeAmb);
            if (data != null) SceneSerializer.FromData(dst, data);
            Check("★ scene loading releases the runtime level (no darkness carried from the previous room)",
                dst.AmbientRuntime == null && dst.EffectiveAmbient == authored, dst.EffectiveAmbient.ToString());
        }
        finally { try { File.Delete(path); } catch { } }
    }

    private static void TestReadabilityFloor()
    {
        Console.WriteLine("--- safety device 1: readability floor ---");

        var lifted = LightingBalance.ApplyFloor(Color.Black);
        Check("even pure black is lifted by the floor (25,25,51)",
            lifted.R == 25 && lifted.G == 25 && lifted.B == 51, lifted.ToString());
        Check("★ blue is lifted more than red and green (lifting evenly washes darkness to grey)",
            lifted.B > lifted.R && lifted.B >= lifted.R * 2, lifted.ToString());

        var darkInterior = new Color(25, 59, 70);
        var raised = LightingBalance.ApplyFloor(darkInterior);
        Check("★ a dark interior is lifted too", raised.R > darkInterior.R && raised.B > darkInterior.B,
            $"{darkInterior} → {raised}");

        Check("an already bright colour does not overflow (saturates)", LightingBalance.ApplyFloor(Color.White) == Color.White);
        Check("alpha is untouched", LightingBalance.ApplyFloor(new Color(0, 0, 0, 123)).A == 123);

        bool monotone = true;
        for (int i = 0; i < 255; i++)
            if (LightingBalance.ApplyFloor(new Color(i, i, i)).R > LightingBalance.ApplyFloor(new Color(i + 1, i + 1, i + 1)).R)
                monotone = false;
        Check("monotonically increasing (a brighter room never gets darker)", monotone);
    }

    private static void TestLightDimming()
    {
        Console.WriteLine("--- safety device 2: light contribution dimming ---");

        Check("black ambient: no dimming (1.0)", LightingBalance.DimmingFactor(Color.Black) == 1f,
            LightingBalance.DimmingFactor(Color.Black).ToString());
        Check("white ambient: the floor of 0.5", Math.Abs(LightingBalance.DimmingFactor(Color.White) - 0.5f) < 0.0001f,
            LightingBalance.DimmingFactor(Color.White).ToString());

        float mid = LightingBalance.DimmingFactor(new Color(128, 128, 128));
        Check("★ intermediate brightness gives an intermediate value (not a constant)", mid > 0.5f && mid < 1f, mid.ToString("0.000"));

        float dark = LightingBalance.DimmingFactor(new Color(25, 59, 70));
        Check("★ a dark interior is dimmed by only about 10% (measured 0.899)", dark > 0.85f, dark.ToString("0.000"));

        bool monotone = true, floored = true;
        float prev = 2f;
        for (int i = 0; i <= 255; i++)
        {
            float f = LightingBalance.DimmingFactor(new Color(i, i, i));
            if (f > prev) monotone = false;
            if (f < LightingBalance.MinDimming) floored = false;
            prev = f;
        }
        Check("monotonically decreasing as it brightens", monotone);
        Check("never below the floor (half of each light remains)", floored);
    }

    private static void TestFrameResolve()
    {
        Console.WriteLine("--- light-level decision (exterior, interior, switch) ---");

        var authored = new Color(25, 59, 70);
        var lightsOn = new Color(210, 205, 180);
        var sunColor = new Color(251, 255, 224);

        var indoor = new Scene("Bathroom") { AmbientLight = authored };
        var f = LightingRenderer.ResolveFrame(indoor, null);
        Check("interior: the scene's authored value is the ambient", f.Ambient == authored, f.Ambient.ToString());
        Check("the clear colour has the floor added", f.Clear == LightingBalance.ApplyFloor(authored), f.Clear.ToString());

        Check("★ dimming derives from the value before the floor is added",
            Math.Abs(f.Dimming - LightingBalance.DimmingFactor(authored)) < 0.0001f
            && Math.Abs(f.Dimming - LightingBalance.DimmingFactor(f.Clear)) > 0.0001f,
            $"{f.Dimming:0.0000} vs after {LightingBalance.DimmingFactor(f.Clear):0.0000}");

        indoor.SetAmbient(lightsOn);
        var lit = LightingRenderer.ResolveFrame(indoor, null);
        Check("★ the light switch (runtime slot) reaches the decision", lit.Ambient == lightsOn, lit.Ambient.ToString());
        Check("   control: switching on strengthens the dimming (a brighter room leaves lights a smaller share)",
            lit.Dimming < f.Dimming, $"{f.Dimming:0.000} → {lit.Dimming:0.000}");

        var outdoor = new Scene("Street") { Exterior = true, AmbientLight = authored };
        var sun = LightingRenderer.ResolveFrame(outdoor, sunColor);
        Check("★ exterior: the clock beats the scene ambient", sun.Ambient == sunColor, sun.Ambient.ToString());
        Check("   control: with no override it falls back to the scene value",
            LightingRenderer.ResolveFrame(outdoor, null).Ambient == authored);
    }

    private static string CaptureStderrLocal(Action body)
    {
        var prev = Console.Error;
        var buf = new StringWriter();
        Console.SetError(buf);
        try { body(); } finally { Console.SetError(prev); }
        return buf.ToString();
    }

#if DEBUG

    private static void TestTextureRefMissingBarks()
    {
        Console.WriteLine("--- does a lost light-decal reference complain ---");

        var root = Path.Combine(Path.GetTempPath(), "pixelcore_lightref_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            using var _ = Assets.AssetRegistry.UseTemporary(Path.Combine(root, "assets.json"));
            var live = Assets.AssetRegistry.Instance.GetOrCreateId("Sprites/Test/Ceiling.png");

            var bark = CaptureStderr(() => ApplyLight(new LightData { Shape = LightShape.Texture, Texture = "bbbb0001" }));
            Check("★ an unregistered light-decal id complains", bark.Contains("light decal asset id 'bbbb0001'"), bark);

            var kept = ApplyLight(new LightData { Shape = LightShape.Texture, Texture = "bbbb0002" });
            Check("★ the unresolved id is held (the next save does not erase it)",
                kept.UnresolvedTextureRef == "bbbb0002", kept.UnresolvedTextureRef ?? "(null)");
            var back = new LightData();
            back.Capture(kept.Entity!);
            Check("★ re-saving writes the unresolved id back out", back.Texture == "bbbb0002", back.Texture ?? "(null)");

            bark = CaptureStderr(() =>
            {
                ApplyLight(new LightData { Shape = LightShape.Texture, Texture = "bbbb0003" });
                ApplyLight(new LightData { Shape = LightShape.Texture, Texture = "bbbb0003" });
                ApplyLight(new LightData { Shape = LightShape.Texture, Texture = "bbbb0003" });
            });
            Check("the same id complains only once (three attempts, one line)", CountOccurrences(bark, "bbbb0003") == 1, bark);
            bark = CaptureStderr(() => ApplyLight(new LightData { Shape = LightShape.Texture, Texture = "bbbb0004" }));
            Check("★ a different id complains again (the latch is per id)", bark.Contains("bbbb0004"), bark);

            bark = CaptureStderr(() => ApplyLight(new LightData { Shape = LightShape.Texture, Texture = live }));
            Check("a resolvable reference says nothing", bark.Length == 0, bark);
            bark = CaptureStderr(() => ApplyLight(new LightData { Shape = LightShape.Point }));
            Check("a light with no picture is quiet too (a point light needs none)", bark.Length == 0, bark);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    private static Light2D ApplyLight(LightData data)
    {
        var scene = new Scene("lightRef");
        var e = scene.CreateEntity("LightEntity");
        scene.Update(0f);
        data.Apply(e);
        return e.GetComponent<Light2D>()!;
    }
#endif

    private static void TestGroundShadow()
    {
        Console.WriteLine("--- ground shadows ---");

        var foot = new Vector2(100f, 200f);
        var scene = new Scene("Ground");
        var sr = scene.CreateEntity("Prop").AddComponent<Components.SpriteRenderer>();
        sr.CastShadow = true;
        scene.Update(0f);

        var q = ShadowRenderer.ShadowQuad(ShadowRenderer.ShadowSize(sr, 48), foot, Point.Zero);
        Check("★ centred under the feet (width = cell width)", q.Width == 48 && q.Center.X == 100, q.ToString());
        Check("★ height = width × 0.62 (ground perspective)", q.Height == (int)MathF.Round(48 * 0.62f), q.Height.ToString());
        Check("   the foot point is the plate's centre", MathF.Abs(q.Center.Y - 200) <= 1, q.Center.Y.ToString());

        sr.ShadowScale = 0.5f;
        var half = ShadowRenderer.ShadowSize(sr, 48);
        Check("★ ShadowScale multiplies the width", half.X == 24, half.X.ToString());
        Check("   control: a different scale gives a different size (not a constant)", half.X != q.Width);

        sr.ShadowScale = 0.01f;
        Check("★ too small is not drawn (empty rectangle)",
            ShadowRenderer.ShadowQuad(ShadowRenderer.ShadowSize(sr, 48), foot, Point.Zero).Width == 0);
        sr.ShadowScale = -3f;
        Check("   a negative scale is safe too",
            ShadowRenderer.ShadowQuad(ShadowRenderer.ShadowSize(sr, 48), foot, Point.Zero).Width == 0);

        sr.ShadowScale = 0.5f;
        sr.ShadowWidth = 30;
        var explicitW = ShadowRenderer.ShadowSize(sr, 48);
        Check("★ an explicit width beats the scale and the cell width", explicitW.X == 30, explicitW.X.ToString());
        Check("   with only the height automatic, it is derived from the explicit width (30 × 0.62)",
            explicitW.Y == (int)MathF.Round(30 * 0.62f), explicitW.Y.ToString());
        sr.ShadowHeight = 7;
        Check("★ with both explicit they are used as is (no ground compression)",
            ShadowRenderer.ShadowSize(sr, 48) == new Point(30, 7));

        var measured = scene.CreateEntity("hero").AddComponent<Components.SpriteRenderer>();
        measured.CastShadow = true;
        measured.SetSprite("Sprites/Characters/Boy/Idle.atlas", "Idle_D_0");
        Check("precondition: the hero atlas loads (without it everything below is vacuous)",
            measured.OpaqueWidth > 0, measured.OpaqueWidth.ToString());
        Check("★ the width measured by the sidecar is used as the base (cell 48, body 15)",
            ShadowRenderer.AutoShadowSize(measured, 48).X == 15,
            ShadowRenderer.AutoShadowSize(measured, 48).X.ToString());

        var unmeasured = scene.CreateEntity("Unnamed").AddComponent<Components.SpriteRenderer>();
        unmeasured.CastShadow = true;
        Check("   control: an unmeasured image falls back to the cell width (48)",
            unmeasured.OpaqueWidth == 0 && ShadowRenderer.AutoShadowSize(unmeasured, 48).X == 48);

        var off = ShadowRenderer.ShadowQuad(new Point(30, 7), foot, new Point(-2, 3));
        Check("★ the offset moves the foot point", off.Center.X == 98 && MathF.Abs(off.Center.Y - 203) <= 1,
            off.ToString());
        Check("   control: with offset 0 the foot point is unchanged",
            ShadowRenderer.ShadowQuad(new Point(30, 7), foot, Point.Zero).Center.X == 100);

        foreach (var probe in new[] { new Point(7, 7), new Point(22, 1), new Point(30, 19) })
        {
            var steps = new List<int>();
            var stepsY = new List<int>();
            for (int i = 0; i <= 4; i++)
            {
                var a = ShadowRenderer.ShadowQuad(probe, new Vector2(100 + i, 100 + i), Point.Zero);
                var b = ShadowRenderer.ShadowQuad(probe, new Vector2(101 + i, 101 + i), Point.Zero);
                steps.Add(b.X - a.X);
                stepsY.Add(b.Y - a.Y);
            }
            Check($"★ size {probe.X}×{probe.Y}: foot point 1px = plate 1px (X), measured {string.Join(",", steps)}",
                steps.TrueForAll(d => d == 1), string.Join(",", steps));
            Check($"★ size {probe.X}×{probe.Y}: foot point 1px = plate 1px (Y), measured {string.Join(",", stepsY)}",
                stepsY.TrueForAll(d => d == 1), string.Join(",", stepsY));
        }

        var bodySr = scene.CreateEntity("Body").AddComponent<Components.SpriteRenderer>();
        var bodyTf = bodySr.Entity.GetComponent<Components.Transform>()!;
        bodySr.DrawSize = new Vector2(7, 7);
        bodySr.PivotX = 0.5f; bodySr.PivotY = 0.5f;
        int mismatched = 0;
        for (int i = 0; i < 8; i++)
        {
            bodyTf.Position = new Vector2(100 + i * 0.5f, 200 + i * 0.5f);
            var body = bodySr.GetDestRect(bodyTf);
            var plate = ShadowRenderer.ShadowQuad(new Point(7, 7), bodyTf.Position, Point.Zero);
            if (body.X != plate.X || body.Y != plate.Y) mismatched++;
        }
        Check("★ the plate rounds the same way as the body (GetDestRect), not even half a pixel off",
            mismatched == 0, mismatched.ToString());

        var texQuad = ShadowRenderer.ShadowQuad(new Point(67, 11), foot, Point.Zero);
        Check("★ a texture keeps its own size (no ground compression)",
            texQuad.Width == 67 && texQuad.Height == 11, texQuad.ToString());
        Check("   control: procedural would have had a different height (67 × 0.62 = 42)",
            texQuad.Height != (int)MathF.Round(67 * 0.62f));

        var m = typeof(ShadowRenderer).GetMethod(nameof(ShadowRenderer.ShadowQuad));
        var ps = m?.GetParameters() ?? Array.Empty<System.Reflection.ParameterInfo>();
        Check("precondition: ShadowQuad is public static", m != null && m.IsStatic);
        Check("★ the quad computation does not take the time as an argument (bringing it back means changing the signature)",
            ps.All(p2 => p2.ParameterType != typeof(Systems.TimeOfDay)
                      && !p2.Name!.Contains("hour", StringComparison.OrdinalIgnoreCase)
                      && !p2.Name!.Contains("angle", StringComparison.OrdinalIgnoreCase)),
            string.Join(", ", ps.Select(p2 => $"{p2.ParameterType.Name} {p2.Name}")));
        var prep = typeof(ShadowRenderer).GetMethod(nameof(ShadowRenderer.PrepareShadows));
        Check("★ the pass itself does not take TimeOfDay either (there is nowhere for switching off at night to come back)",
            prep != null && prep.GetParameters().All(p2 => p2.ParameterType != typeof(Systems.TimeOfDay)),
            string.Join(", ", prep?.GetParameters().Select(p2 => p2.ParameterType.Name) ?? Array.Empty<string>()));

        Check("★ the base opacity equals the old midday maximum (the daytime exterior screen is unchanged)",
            MathF.Abs(ShadowRenderer.BaseOpacity - 0.38f) < 1e-6f, ShadowRenderer.BaseOpacity.ToString());
        Check("★ with no table the shadow multiplier is 1 (no modulation)", ProfileFrame.Neutral.ShadowScale == 1f);

        const string json = """
        { "rows": [ { "scene": "*", "tag": "Day", "shadowScale": 0.4 } ] }
        """;
        WithTable(json, () =>
        {
            var f = LightingProfiles.Resolve("someRoom", "Day", 8f);
            Check("★ the table lowers shadow opacity (interiors lower: the room decides, not the time)",
                MathF.Abs(f.ShadowScale - 0.4f) < 1e-6f, f.ShadowScale.ToString());
            Check("   that value differs from no modulation (control)", f.ShadowScale != ProfileFrame.Neutral.ShadowScale);
            Check("   composite opacity = base × multiplier",
                MathF.Abs(ShadowRenderer.BaseOpacity * f.ShadowScale - 0.152f) < 1e-4f);
        });
    }

    private static void TestShadowPlate()
    {
        Console.WriteLine("--- shadow plate (ellipse to rectangle, flat to soft) ---");

        const int size = 128;
        var flat = ShadowRenderer.BakeRoundedRect(size, size, 1f, 0f);
        Check("precondition: the plate has exactly the pixel count requested (no stretching)", flat.Length == size * size,
            flat.Length.ToString());

        var distinct = new HashSet<byte>();
        foreach (var px in flat) distinct.Add(px.A);
        Check($"★ with softness 0 alpha has only two values (a flat pixel plate), measured {distinct.Count} kinds",
            distinct.Count == 2 && distinct.Contains((byte)0) && distinct.Contains((byte)255),
            string.Join(",", distinct));

        int mid = size / 2;
        Check("the centre is filled", flat[mid * size + mid].A == 255);
        Check("★ the four corners at radius 1 are empty (a circle, not a rectangle)",
            flat[0].A == 0 && flat[size - 1].A == 0
            && flat[(size - 1) * size].A == 0 && flat[size * size - 1].A == 0);
        int filled = 0; foreach (var px in flat) if (px.A > 0) filled++;
        double ratio = (double)filled / flat.Length;
        Check($"★ the filled fraction is close to the circle's area (π/4 ≈ 0.785), measured {ratio:0.000}",
            Math.Abs(ratio - Math.PI / 4) < 0.01, ratio.ToString("0.000"));

        var square = ShadowRenderer.BakeRoundedRect(40, 12, 0f, 0f);
        bool allFilled = true;
        foreach (var px in square) if (px.A != 255) { allFilled = false; break; }
        Check("★ radius 0 is a fully filled rectangle (alpha 255 into the corners)", allFilled);
        Check("   control: at the same size, radius 1 leaves the corners empty",
            ShadowRenderer.BakeRoundedRect(40, 12, 1f, 0f)[0].A == 0);

        const int ew = 48, eh = 20;
        var ellipse = ShadowRenderer.BakeRoundedRect(ew, eh, 1f, 0f);
        int efilled = 0; foreach (var px in ellipse) if (px.A > 0) efilled++;
        double eratio = (double)efilled / ellipse.Length;
        Check($"★ even when wide, radius 1 is an ellipse (area ratio π/4), measured {eratio:0.000}",
            Math.Abs(eratio - Math.PI / 4) < 0.02, eratio.ToString("0.000"));
        int topRow = 0; for (int x = 0; x < ew; x++) if (ellipse[x].A > 0) topRow++;
        Check($"★ control: the first row is nearly empty (a pill would fill the middle solid), measured {topRow}px",
            topRow < ew / 3, topRow.ToString());

        Check("★ radius 1.5 is the same plate as 1 (clamped)",
            SameBytes(ShadowRenderer.BakeRoundedRect(40, 12, 1.5f, 0f),
                      ShadowRenderer.BakeRoundedRect(40, 12, 1f, 0f)));
        Check("   control: 1 and 0 differ (the clamp check is not vacuous)",
            !SameBytes(ShadowRenderer.BakeRoundedRect(40, 12, 1f, 0f),
                       ShadowRenderer.BakeRoundedRect(40, 12, 0f, 0f)));
        Check("   ClampRadius is the authority for that clamp",
            ShadowRenderer.ClampRadius(1.5f) == 1f && ShadowRenderer.ClampRadius(-3f) == 0f);

        Check("★ the plate cache key uses the clamped value (1.5 and 1 do not sit in different slots)",
            ShadowSource().Contains("var key = (size.X, size.Y, Radius: ClampRadius(radius), ShadowSoftness);"));
        Check("★ it bakes from the values read back out of that key (no room for key and image to diverge)",
            ShadowSource().Contains("BakeRoundedRect(size.X, size.Y, key.Radius, ShadowSoftness)"));

        Check("★ baking twice with the same arguments gives the same bytes",
            SameBytes(ShadowRenderer.BakeRoundedRect(37, 13, 0.4f, 0f),
                      ShadowRenderer.BakeRoundedRect(37, 13, 0.4f, 0f)));
        Check("   control: even a 1px size difference differs (size is in the key)",
            ShadowRenderer.BakeRoundedRect(38, 13, 0.4f, 0f).Length
            != ShadowRenderer.BakeRoundedRect(37, 13, 0.4f, 0f).Length);

        var soft = ShadowRenderer.BakeRoundedRect(size, size, 1f, 1f);
        int diff = 0;
        for (int i = 0; i < soft.Length; i++)
        {
            int x = i % size, y = i / size;
            float half = size / 2f;
            float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            float t = Math.Clamp((1f - d) / 0.75f, 0f, 1f);
            float f = t * t * (3f - 2f * t);
            if (soft[i] != new Color(f, f, f, f)) diff++;
        }
        Check("★ control: 128px, radius 1, softness 1 matches the old disc formula pixel for pixel",
            diff == 0, diff.ToString());

        var softDistinct = new HashSet<byte>();
        foreach (var px in soft) softDistinct.Add(px.A);
        Check($"★ control: softness 1 has many alpha steps ({softDistinct.Count} kinds); that is the grain that 'floats separately'",
            softDistinct.Count > 20, softDistinct.Count.ToString());

        var half5 = ShadowRenderer.BakeRoundedRect(size, size, 1f, 0.5f);
        var midDistinct = new HashSet<byte>();
        foreach (var px in half5) midDistinct.Add(px.A);
        Check("an intermediate value is in between (a continuous handle)",
            midDistinct.Count > 2 && midDistinct.Count < softDistinct.Count, midDistinct.Count.ToString());

        Check("★ the plate is baked at exactly the size it is used (no stretching, so no sampler branch)",
            ShadowSource().Contains("new Texture2D(gd, size.X, size.Y)")
            && ShadowSource().Contains("gd.SamplerStates[0] = SamplerState.PointClamp;"));
        Check("★ the default softness is 0 (the author's verdict: a flat pixel plate)", ShadowRenderer.ShadowSoftness == 0f,
            ShadowRenderer.ShadowSoftness.ToString());
    }

    private static void TestShadowAlphaForce()
    {
        Console.WriteLine("--- shadow alpha forcing ---");

        var src = new[] { new Color(200, 30, 30, 90), new Color(0, 0, 0, 0), new Color(9, 9, 9, 1) };
        var forced = ShadowRenderer.ForceOpaqueAlpha(src);
        Check("★ α>0 becomes alpha 255 (α90 → 255)", forced[0].A == 255, forced[0].A.ToString());
        Check("★ α1 becomes 255 too (the only threshold is 'greater than 0')", forced[2].A == 255);
        Check("★ α0 stays 0 (the plate does not grow)", forced[1].A == 0);
        Check("   colour is discarded (opacity is Composite's job)", forced[0].R == 0 && forced[0].G == 0);

        static float Over(float sa, float da) => sa + da * (1f - sa);

        float forcedTwice = Over(forced[0].A / 255f, forced[0].A / 255f);
        Check("★ two forced sheets overlapping give target alpha exactly 1",
            MathF.Abs(forcedTwice - 1f) < 1e-6f, forcedTwice.ToString("0.000"));

        float rawTwice = Over(90f / 255f, 90f / 255f);
        Check($"★ control: without forcing it is 0.58 (only the overlap gets twice as dark), measured {rawTwice:0.00}",
            MathF.Abs(rawTwice - 0.58f) < 0.01f && MathF.Abs(rawTwice - forcedTwice) > 0.4f,
            rawTwice.ToString("0.000"));

        var code = ShadowSource();
        Check("★ an authored texture becomes a plate through OpaquePlate (the original is not drawn directly)",
            code.Contains("plate = OpaquePlate(gd, authored)"));
        Check("★ OpaquePlate bakes with ForceOpaqueAlpha",
            code.Contains("tex.SetData(ForceOpaqueAlpha(src))"));
        Check("★ authored and procedural are drawn through the same door (one DrawQuad call)",
            CountOccurrences(code, "DrawQuad(gd, item.Quad, item.Plate)") == 1);
    }

    private static void TestShadowBands()
    {
        Console.WriteLine("--- shadow bands (where they are laid) ---");

        Check("★ a Floor caster has nowhere to draw (below the floor)",
            ShadowRenderer.ShadowBandOf(RenderLayers.Floor) == ShadowBand.None);
        Check("★ a BelowEntities caster goes in the Below band",
            ShadowRenderer.ShadowBandOf(RenderLayers.BelowEntities) == ShadowBand.Below);
        Check("★ an Entities caster goes in the Entities band",
            ShadowRenderer.ShadowBandOf(RenderLayers.Entities) == ShadowBand.Entities);
        Check("   Above/Overlay go in the Entities band too (no place to lay shadows is made higher up)",
            ShadowRenderer.ShadowBandOf(RenderLayers.AboveEntities) == ShadowBand.Entities
            && ShadowRenderer.ShadowBandOf(RenderLayers.Overlay) == ShadowBand.Entities);
        Check("   below Floor is None too (safe even if a scene uses in-between values)",
            ShadowRenderer.ShadowBandOf(RenderLayers.Floor - 1) == ShadowBand.None);
        Check("   between Floor and Below is Below (there is already a floor to draw on)",
            ShadowRenderer.ShadowBandOf(RenderLayers.Floor + 1) == ShadowBand.Below);

        var code = ShadowSource();
        Check("★ a Floor caster goes into no target",
            code.Contains("if (band == ShadowBand.None) continue;"));
        Check("★ the two bands fill their own targets",
            code.Contains("HasBelowShadows = FillBand(gd, ShadowBand.Below, _belowTarget!);")
            && code.Contains("HasEntityShadows = FillBand(gd, ShadowBand.Entities, _entitiesTarget!);"));
        Check("   FillBand draws only its own band's items",
            code.Contains("if (item.Band == band) DrawQuad(gd, item.Quad, item.Plate);"));

        const string host = "Game1.cs";
        Check($"precondition: the host source is actually read ({host})", File.Exists(host), host);
        if (File.Exists(host))
        {
            var g = File.ReadAllText(host);
            int drawFloor = g.IndexOf("DrawBand(int.MinValue, RenderLayers.BelowEntities);", StringComparison.Ordinal);
            int compBelow = g.IndexOf("Composite(_spriteBatch, Runtime.Rendering.ShadowBand.Below", StringComparison.Ordinal);
            int drawBelow = g.IndexOf("DrawBand(RenderLayers.BelowEntities, RenderLayers.Entities);", StringComparison.Ordinal);
            int compEnt = g.IndexOf("Composite(_spriteBatch, Runtime.Rendering.ShadowBand.Entities", StringComparison.Ordinal);
            int drawEnt = g.IndexOf("DrawBand(RenderLayers.Entities, int.MaxValue);", StringComparison.Ordinal);
            Check("precondition: all five steps are in the source",
                drawFloor >= 0 && compBelow >= 0 && drawBelow >= 0 && compEnt >= 0 && drawEnt >= 0,
                $"{drawFloor},{compBelow},{drawBelow},{compEnt},{drawEnt}");
            Check("★ Below shadows are laid after Floor and before BelowEntities",
                drawFloor < compBelow && compBelow < drawBelow);
            Check("★ Entities shadows are laid after BelowEntities and before Entities",
                drawBelow < compEnt && compEnt < drawEnt);
        }

        var byBand = new Dictionary<ShadowBand, List<string>>
        {
            [ShadowBand.None] = new(), [ShadowBand.Below] = new(), [ShadowBand.Entities] = new(),
        };
        foreach (var dir in new[] { "Content/Scenes", "Content/Prefabs" })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.scene", SearchOption.AllDirectories))
            {
                var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file)) as System.Text.Json.Nodes.JsonObject;
                if (root?["entities"] is not System.Text.Json.Nodes.JsonArray arr) continue;
                foreach (var ent in arr)
                {
                    if (ent?["components"] is not System.Text.Json.Nodes.JsonArray comps) continue;
                    foreach (var c in comps)
                    {
                        if (c?["type"]?.GetValue<string>() != "sprite") continue;
                        if (c["castShadow"]?.GetValue<bool>() != true) continue;
                        int layer = c["renderLayer"]?.GetValue<int>() ?? RenderLayers.Entities;
                        byBand[ShadowRenderer.ShadowBandOf(layer)]
                            .Add($"{Path.GetFileNameWithoutExtension(file)}/{ent["name"]?.GetValue<string>()}");
                    }
                }
            }
        }
        int total = byBand[ShadowBand.None].Count + byBand[ShadowBand.Below].Count + byBand[ShadowBand.Entities].Count;
        Check($"precondition: real content has shadow casters ({total}; with 0 everything below is vacuous)", total > 0);
        Check($"★ Below band casters actually exist ({byBand[ShadowBand.Below].Count}: "
              + string.Join(", ", byBand[ShadowBand.Below]) + ")",
              byBand[ShadowBand.Below].Count > 0);
        Check($"★ no caster has nowhere to draw (Floor layer + CastShadow = silently invisible)"
              + (byBand[ShadowBand.None].Count == 0 ? "" : " — " + string.Join(", ", byBand[ShadowBand.None])),
              byBand[ShadowBand.None].Count == 0);
    }

    private static string? _shadowSource;

    private static string ShadowSource()
    {
        if (_shadowSource != null) return _shadowSource;

        const string path = "Runtime/Rendering/ShadowRenderer.cs";
        Check($"precondition: the contract checks actually read the source ({path})", File.Exists(path), path);
        if (!File.Exists(path)) return _shadowSource = "";

        bool inBlock = false;
        var sb = new System.Text.StringBuilder();
        foreach (var line in File.ReadAllText(path).Replace("\r\n", "\n").Split('\n'))
            sb.Append(SourceScan.StripComments(line, ref inBlock)).Append('\n');
        return _shadowSource = sb.ToString();
    }

    private static bool SameBytes(Color[] a, Color[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static void TestShadowTextureRoundTrip()
    {
        Console.WriteLine("--- shadow texture override ---");

        var plain = new Scene("PlainShadow");
        var e = plain.CreateEntity("Prop");
        var sr = e.AddComponent<Components.SpriteRenderer>();
        sr.CastShadow = true;
        plain.Update(0f);
        RoundTripSprite(plain,
            text => Check("★ not choosing one writes no shadowTexture key (content diff 0)",
                !text.Contains("\"shadowTexture\"")),
            back => Check("   no key means the procedural ellipse (null handle)",
                back.ShadowTexture == null && back.ShadowTexturePath == null));

        const string png = "Sprites/Test/WindowPool.png";
        var id = Assets.AssetRegistry.Instance.GetOrCreateId(png);
        var authored = new Scene("AuthoredShadow");
        var e2 = authored.CreateEntity("Prop");
        var sr2 = e2.AddComponent<Components.SpriteRenderer>();
        sr2.CastShadow = true;
        sr2.ShadowTexturePath = png;
        authored.Update(0f);
        RoundTripSprite(authored,
            text => Check("★ the file carries the id (not a path)",
                text.Contains($"\"shadowTexture\": \"{id}\"") && !text.Contains(png),
                text.Contains(png) ? "a path is in the file" : "the id is missing"),
            back => Check("★ the id resolves to the path", back.ShadowTexturePath == png, back.ShadowTexturePath));

#if DEBUG
        var data = new SpriteData { ShadowTexture = "cccc0132", CastShadow = true };
        var scene = new Scene("BrokenShadow");
        var ent = scene.CreateEntity("Prop");
        scene.Update(0f);
        var bark = CaptureStderr(() => data.Apply(ent));
        var live = ent.GetComponent<Components.SpriteRenderer>()!;
        Check("★ an unresolved shadow id complains", bark.Contains("cccc0132"), bark);
        Check("★ the value is preserved (the next save does not erase it)", live.UnresolvedShadowTextureRef == "cccc0132");
        Check("★ the shadow is still drawn (procedural ellipse: decoration that can go missing without ruining the screen)",
            live.ShadowTexture == null && live.CastShadow);
        var again = CaptureStderr(() => data.Apply(scene.CreateEntity("Prop2")));
        Check("   the same id complains only once (the latch is per id)", !again.Contains("cccc0132"));
#endif
    }

    private static void TestShadowShapeRoundTrip()
    {
        Console.WriteLine("--- shadow shape (rounded rectangle) ---");

        var plain = new Scene("PlainShape");
        var pe = plain.CreateEntity("Prop");
        var psr = pe.AddComponent<Components.SpriteRenderer>();
        psr.CastShadow = true;
        plain.Update(0f);
        RoundTripSprite(plain,
            text => Check("★ with defaults, none of the five shape keys are in the file (content diff 0)",
                !text.Contains("\"shadowW\"") && !text.Contains("\"shadowH\"")
                && !text.Contains("\"shadowRadius\"")
                && !text.Contains("\"shadowOffX\"") && !text.Contains("\"shadowOffY\"")),
            back => Check("   defaults = automatic size, ellipse, offset 0",
                back.ShadowWidth == null && back.ShadowHeight == null
                && back.ShadowRadius == 1f && back.ShadowOffset == Point.Zero));

        var authored = new Scene("AuthoredShape");
        var ae = authored.CreateEntity("Signpost");
        var asr = ae.AddComponent<Components.SpriteRenderer>();
        asr.CastShadow = true;
        asr.ShadowWidth = 28; asr.ShadowHeight = 9;
        asr.ShadowRadius = 0f;
        asr.ShadowOffset = new Point(-2, 3);
        authored.Update(0f);
        RoundTripSprite(authored,
            text => Check("★ control: changing values writes the keys",
                text.Contains("\"shadowW\": 28") && text.Contains("\"shadowH\": 9")
                && text.Contains("\"shadowRadius\": 0") && text.Contains("\"shadowOffX\": -2")
                && text.Contains("\"shadowOffY\": 3")),
            back => Check("★ the values come back unchanged",
                back.ShadowWidth == 28 && back.ShadowHeight == 9
                && back.ShadowRadius == 0f && back.ShadowOffset == new Point(-2, 3)));

        var oldPath = Path.Combine(Path.GetTempPath(), "pc_shadowold_" + Guid.NewGuid().ToString("N")[..8] + ".scene");
        try
        {
            File.WriteAllText(oldPath,
                "{\n  \"schemaVersion\": 2,\n  \"name\": \"OldScene\",\n  \"entities\": [\n" +
                "    { \"id\": 1, \"name\": \"Prop\", \"active\": true, \"components\": [\n" +
                "      { \"type\": \"transform\", \"x\": 10, \"y\": 20, \"rotation\": 0 },\n" +
                "      { \"type\": \"sprite\", \"castShadow\": true, \"shadowScale\": 0.5 } ] } ] }");

            var oldData = SceneSerializer.LoadFromFile(oldPath);
            var oldScene = new Scene("OldScene");
            if (oldData != null) SceneSerializer.FromData(oldScene, oldData);
            oldScene.Update(0f);
            var live = oldScene.FindEntity("Prop")?.GetComponent<Components.SpriteRenderer>();
            Check("★ an old scene without shape keys loads with defaults",
                live != null && live.ShadowWidth == null && live.ShadowHeight == null
                && live.ShadowRadius == 1f && live.ShadowOffset == Point.Zero
                && live.CastShadow && MathF.Abs(live.ShadowScale - 0.5f) < 1e-6f);

            var rePath = oldPath + ".2";
            try
            {
                SceneSerializer.SaveToFile(SceneSerializer.ToData(oldScene), rePath);
                var re = File.ReadAllText(rePath);
                Check("★ saving again does not create shape keys",
                    !re.Contains("\"shadowW\"") && !re.Contains("\"shadowH\"")
                    && !re.Contains("\"shadowRadius\"")
                    && !re.Contains("\"shadowOffX\"") && !re.Contains("\"shadowOffY\""));
            }
            finally { try { File.Delete(rePath); } catch { } }
        }
        finally { try { File.Delete(oldPath); } catch { } }
    }

    private static void RoundTripSprite(Scene scene, Action<string> onBytes,
        Action<Components.SpriteRenderer> onRestored)
    {
        var path = Path.Combine(Path.GetTempPath(), "pc_shadowtex_" + Guid.NewGuid().ToString("N")[..8] + ".scene");
        try
        {
            SceneSerializer.SaveToFile(SceneSerializer.ToData(scene), path);
            onBytes(File.ReadAllText(path));

            var back = new Scene("Restored");
            var d = SceneSerializer.LoadFromFile(path);
            if (d == null) { Check("precondition: the file reads back", false, path); return; }
            SceneSerializer.FromData(back, d);
            back.Update(0f);

            Components.SpriteRenderer? found = null;
            foreach (var en in back.Entities)
            { found = en.GetComponent<Components.SpriteRenderer>(); if (found != null) break; }
            if (found == null) { Check("precondition: the restored scene has a sprite", false); return; }
            onRestored(found);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    private static string CaptureStderr(Action body)
    {
        var prev = Console.Error;
        var buf = new StringWriter();
        Console.SetError(buf);
        try { body(); } finally { Console.SetError(prev); }
        return buf.ToString();
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    private static void TestLightContribution()
    {
        Console.WriteLine("--- light pass distribution ---");

        var plain = new Light2D();
        var c = LightingRenderer.ResolveContribution(plain);
        Check("★ the default is 100% multiply and 0 additive (existing scenes' screens unchanged)",
            c.Multiply == 1f && c.Additive == 0f && !c.Negative, c.ToString());

        c = LightingRenderer.ResolveContribution(new Light2D { Additive = 1f });
        Check("Additive 1 = additive only (multiply share 0)", c.Multiply == 0f && c.Additive == 1f, c.ToString());

        c = LightingRenderer.ResolveContribution(new Light2D { Additive = 0.25f });
        Check("★ an intermediate value is split across both passes (summing to 1: intensity neither grows nor shrinks)",
            MathF.Abs(c.Multiply - 0.75f) < 1e-6f && MathF.Abs(c.Additive - 0.25f) < 1e-6f, c.ToString());

        c = LightingRenderer.ResolveContribution(new Light2D { Additive = 4f });
        Check("upper clamp (4 → 1)", c.Additive == 1f && c.Multiply == 0f, c.ToString());
        c = LightingRenderer.ResolveContribution(new Light2D { Additive = -2f });
        Check("lower clamp (-2 → 0)", c.Additive == 0f && c.Multiply == 1f, c.ToString());

        c = LightingRenderer.ResolveContribution(new Light2D { Negative = true });
        Check("★ a negative light goes to the subtract pass at ×0.4 intensity",
            c.Negative && c.Multiply == LightingRenderer.NegativeScale && c.Additive == 0f, c.ToString());

        c = LightingRenderer.ResolveContribution(new Light2D { Negative = true, Additive = 1f });
        Check("★ negative beats Additive (the inspector shows that state greyed out)",
            c.Negative && c.Additive == 0f, c.ToString());

        Check("   control: NegativeScale is not 1 (if it were, the ×0.4 check would be vacuous)",
            LightingRenderer.NegativeScale != 1f);
    }

    private static void TestScenePasses()
    {
        Console.WriteLine("--- scene pass requirement ---");

        var scene = new Scene("PassDecision");
        var plain = scene.CreateEntity("PlainLight").AddComponent<Light2D>();
        scene.Update(0f);

        var p = LightingRenderer.ResolveScenePasses(scene);
        Check("★ control: with only default lights there are no extra passes (existing scenes cost nothing)",
            !p.Additive && !p.Negative, p.ToString());

        plain.Additive = 0.5f;
        p = LightingRenderer.ResolveScenePasses(scene);
        Check("an additive share requires the additive pass", p.Additive && !p.Negative, p.ToString());

        plain.Enabled = false;
        Check("★ a disabled light does not call for a pass (the same filter as the draw loop)",
            !LightingRenderer.ResolveScenePasses(scene).Additive);
        plain.Enabled = true;
        plain.Entity!.Active = false;
        Check("★ the same goes for a light on an inactive entity",
            !LightingRenderer.ResolveScenePasses(scene).Additive);
        plain.Entity!.Active = true;
        Check("   control: re-enabling it requires the pass again (the two above are not 'always false')",
            LightingRenderer.ResolveScenePasses(scene).Additive);

        plain.Additive = 0f;
        plain.Negative = true;
        p = LightingRenderer.ResolveScenePasses(scene);
        Check("a negative light calls only for the subtract pass", p.Negative && !p.Additive, p.ToString());

        var neon = scene.CreateEntity("Neon").AddComponent<Light2D>();
        neon.Additive = 1f;
        scene.Update(0f);
        p = LightingRenderer.ResolveScenePasses(scene);
        Check("with both in one scene, both run", p.Negative && p.Additive, p.ToString());
    }

    private static void TestCompositeOrder()
    {
        Console.WriteLine("--- composite order ---");

        Check("with no lights no pass runs",
            LightingRenderer.ResolveComposite(hasLights: false, hasAdditive: false).Length == 0);
        Check("★ no additive pass when the additive target is not ready (even with lights)",
            LightingRenderer.ResolveComposite(hasLights: false, hasAdditive: true).Length == 0);

        var one = LightingRenderer.ResolveComposite(hasLights: true, hasAdditive: false);
        Check("★ with no additive lights, a single multiply pass (existing scenes unchanged)",
            one.Length == 1 && one[0] == LightingRenderer.LightPass.Multiply,
            string.Join(",", one));

        var both = LightingRenderer.ResolveComposite(hasLights: true, hasAdditive: true);
        Check("★ multiply first, additive after (reversed, the additive share is eaten by the multiply and the neon gets darker)",
            both.Length == 2 && both[0] == LightingRenderer.LightPass.Multiply
                             && both[1] == LightingRenderer.LightPass.Additive,
            string.Join(",", both));
    }

    private static void TestAdditiveNegativeRoundTrip()
    {
        Console.WriteLine("--- additive/negative round trip ---");

        var plain = new Scene("DefaultLight");
        plain.CreateEntity("PointLight").AddComponent<Light2D>();
        plain.Update(0f);
        RoundTrip(plain, text =>
        {
            Check("★ with defaults, no additive/negative keys are written (content diff 0)",
                !text.Contains("\"additive\"") && !text.Contains("\"negative\""),
                text.Contains("\"additive\"") ? "additive was written" : "negative was written");
        }, restored =>
        {
            Check("   with no keys, loading gives the previous behaviour (0 / false)",
                restored.Additive == 0f && !restored.Negative);
        });

        var neon = new Scene("NeonLight");
        var l = neon.CreateEntity("Sign").AddComponent<Light2D>();
        l.Additive = 0.75f;
        l.Negative = true;
        neon.Update(0f);
        RoundTrip(neon, text =>
        {
            Check("★ authored values are written to the file",
                text.Contains("\"additive\": 0.75") && text.Contains("\"negative\": true"), text.Length.ToString());
        }, restored =>
        {
            Check("★ the values come back unchanged", restored.Additive == 0.75f && restored.Negative,
                $"{restored.Additive} / {restored.Negative}");
        });

        var half = new Scene("HalfLight");
        half.CreateEntity("DoorGap").AddComponent<Light2D>().Additive = 1f;
        half.Update(0f);
        RoundTrip(half, text =>
        {
            Check("★ authoring only one side writes only that key",
                text.Contains("\"additive\"") && !text.Contains("\"negative\""));
        }, restored =>
        {
            Check("   partial omission round-trips too", restored.Additive == 1f && !restored.Negative);
        });
    }

    private static void RoundTrip(Scene scene, Action<string> onBytes, Action<Light2D> onRestored)
    {
        var path = Path.Combine(Path.GetTempPath(), "pc_lightblend_" + Guid.NewGuid().ToString("N")[..8] + ".scene");
        try
        {
            SceneSerializer.SaveToFile(SceneSerializer.ToData(scene), path);
            onBytes(File.ReadAllText(path));

            var back = new Scene("Restored");
            var data = SceneSerializer.LoadFromFile(path);
            if (data == null) { Check("precondition: the file reads back", false, path); return; }
            SceneSerializer.FromData(back, data);
            back.Update(0f);

            Light2D? light = null;
            foreach (var e in back.Entities) { light = e.GetComponent<Light2D>(); if (light != null) break; }
            if (light == null) { Check("precondition: the restored scene has a light", false); return; }
            onRestored(light);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    private static void TestAuthoringDefaults()
    {
        Console.WriteLine("--- light authoring baselines ---");

        var l = new Light2D();
        Check("★ default radius = 280", l.Radius == 280f, l.Radius.ToString());
        Check("★ default intensity = 0.9", l.Intensity == 0.9f, l.Intensity.ToString());
        Check("   control: not the old defaults (96 / 1.0); if they were, the two above would be vacuous",
            l.Radius != 96f && l.Intensity != 1f);
        Check("Rect softening is off by default (existing scenes unchanged)", l.CornerRadius == 0f && l.Core == 0f);

        var d = new LightData();
        Check("★ the DTO default radius matches the component", d.Radius == l.Radius, $"{d.Radius} vs {l.Radius}");
        Check("★ the DTO default intensity matches the component", d.Intensity == l.Intensity, $"{d.Intensity} vs {l.Intensity}");
        Check("the DTO default softening matches too", d.CornerRadius == l.CornerRadius && d.Core == l.Core);
        Check("the DTO default shape and feather match too",
            d.Shape == l.Shape && d.Feather == l.Feather && d.FollowSun == l.FollowSun);
    }

    private static void TestRadialFalloff()
    {
        Console.WriteLine("--- Point falloff curve ---");

        var px = LightingRenderer.BakeRadial();
        int size = (int)MathF.Sqrt(px.Length);
        float half = size / 2f;
        float At(float d)
        {
            int y = size / 2;
            int x = (int)MathF.Round(half + d * half - 0.5f);
            x = Math.Clamp(x, 0, size - 1);
            return px[y * size + x].R / 255f;
        }
        static float Legacy(float d) { float t = Math.Clamp(1f - d, 0f, 1f); return t * t; }

        Check("the centre is the maximum", At(0f) > 0.99f, At(0f).ToString("0.000"));
        Check("the edge is 0", At(1f) < 0.02f, At(1f).ToString("0.000"));
        Check("monotonically decreasing", At(0.2f) > At(0.4f) && At(0.4f) > At(0.6f) && At(0.6f) > At(0.8f));

        float mid = At(0.5f), legacyMid = Legacy(0.5f);
        Check($"★ the middle band is wider than the old curve (d=0.5: {mid:0.000} > {legacyMid:0.000})", mid > legacyMid + 0.15f,
            $"{mid:0.000} vs {legacyMid:0.000}");
        Check("★ control: with the old curve this check would be red (0.25 cannot exceed 0.25+0.15)",
            !(Legacy(0.5f) > Legacy(0.5f) + 0.15f));

        Check("★ flat near the centre (a wide core)", At(0.1f) > 0.95f && Legacy(0.1f) < 0.85f,
            $"{At(0.1f):0.000} vs old {Legacy(0.1f):0.000}");
    }

    private static void TestRectBake()
    {
        Console.WriteLine("--- Rect bake (corners and core) ---");

        const float F = 0.35f;
        var plain = LightingRenderer.BakeRect(F, 0f, 0f);
        int size = (int)MathF.Sqrt(plain.Length);
        float half = size / 2f;

        Color[] Legacy(float feather)
        {
            var a = new Color[size * size];
            float f = MathF.Max(feather, 0.02f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float nx = MathF.Abs(x + 0.5f - half) / half;
                    float ny = MathF.Abs(y + 0.5f - half) / half;
                    float m = MathF.Max(nx, ny);
                    float s = Math.Clamp((1f - m) / f, 0f, 1f);
                    float v = s * s * (3f - 2f * s);
                    a[y * size + x] = new Color(v, v, v, v);
                }
            return a;
        }
        static int Diff(Color[] a, Color[] b)
        {
            int n = 0;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) n++;
            return n;
        }

        Check("★ with softening 0 the pixels match the old formula exactly (existing scenes unchanged)",
            Diff(plain, Legacy(F)) == 0, Diff(plain, Legacy(F)).ToString());
        Check("★ also at a different feather (not tuned to the default alone)",
            Diff(LightingRenderer.BakeRect(0.1f, 0f, 0f), Legacy(0.1f)) == 0);

        var rounded = LightingRenderer.BakeRect(F, 0.8f, 0f);
        int corner = (int)(half + half * 0.93f), mid = size / 2, edge = (int)(half + half * 0.93f);
        float cPlain = plain[corner * size + corner].R / 255f;
        float cRound = rounded[corner * size + corner].R / 255f;
        Check($"★ the corner darkens ({cPlain:0.000} → {cRound:0.000})", cRound < cPlain - 0.05f,
            $"{cPlain:0.000} → {cRound:0.000}");
        float sPlain = plain[mid * size + edge].R / 255f, sRound = rounded[mid * size + edge].R / 255f;
        Check($"★ control: the middle of an edge does not change (only the corners are shaved; {sPlain:0.000} = {sRound:0.000})",
            MathF.Abs(sPlain - sRound) < 0.004f, $"{sPlain:0.000} vs {sRound:0.000}");
        static long Energy(Color[] a) { long n = 0; foreach (var p2 in a) n += p2.R; return n; }
        long e0 = Energy(plain), e8 = Energy(rounded), e10 = Energy(LightingRenderer.BakeRect(F, 1f, 0f));
        Check($"★ a larger corner radius shaves more (energy {e0} > {e8} > {e10})", e0 > e8 && e8 > e10);

        var cored = LightingRenderer.BakeRect(F, 0f, 1f);
        int band = (int)(half + half * 0.55f);
        float bPlain = plain[mid * size + band].R / 255f, bCore = cored[mid * size + band].R / 255f;
        Check($"   precondition: that spot is flat under the old formula ({bPlain:0.000} = 1)", bPlain > 0.99f);
        Check($"★ raising the core tilts the flat area ({bPlain:0.000} → {bCore:0.000})", bCore < bPlain - 0.2f,
            $"{bPlain:0.000} → {bCore:0.000}");
        Check($"the centre is still the maximum ({cored[mid * size + mid].R}/255); it cannot go brighter than 1 (that is the additive pass's job)",
            cored[mid * size + mid].R >= 253 && cored[mid * size + mid].R == cored.Max(px2 => px2.R),
            cored[mid * size + mid].R.ToString());
        Check("★ a larger core tilts more (monotonic)",
            LightingRenderer.BakeRect(F, 0f, 0.5f)[mid * size + band].R > cored[mid * size + band].R
            && LightingRenderer.BakeRect(F, 0f, 0.5f)[mid * size + band].R < plain[mid * size + band].R);
        int rim = (int)(half + half * 0.85f);
        Check("★ the core changes the centre-to-rim ratio (something intensity cannot do)",
            (cored[mid * size + band].R / 255f) / MathF.Max(cored[mid * size + rim].R / 255f, 0.001f)
            != (plain[mid * size + band].R / 255f) / MathF.Max(plain[mid * size + rim].R / 255f, 0.001f));
        Check("★ control: at core 0 not a single pixel changes", Diff(LightingRenderer.BakeRect(F, 0f, 0f), plain) == 0);

        var k0 = LightingRenderer.RectKey(F, 0f, 0f);
        Check("★ the cache key includes the corner", LightingRenderer.RectKey(F, 0.5f, 0f) != k0);
        Check("★ the cache key includes the core", LightingRenderer.RectKey(F, 0f, 0.5f) != k0);
        Check("the cache key includes the feather too (as before)", LightingRenderer.RectKey(0.9f, 0f, 0f) != k0);
        Check("equal values give equal keys (the cache actually hits)", LightingRenderer.RectKey(F, 0.5f, 0.5f)
            == LightingRenderer.RectKey(F, 0.5f, 0.5f));
        Check("★ quantized to 0.05: tiny differences share a key (no rebake every frame while dragging a slider)",
            LightingRenderer.RectKey(F, 0.501f, 0f) == LightingRenderer.RectKey(F, 0.499f, 0f));
    }

    private static void TestRectSofteningRoundTrip()
    {
        Console.WriteLine("--- Rect softening round trip ---");

        var plain = new Scene("PlainRect");
        plain.CreateEntity("StripLight").AddComponent<Light2D>().Shape = LightShape.Rect;
        plain.Update(0f);
        RoundTrip(plain,
            text => Check("★ with defaults, no cornerRadius/core keys are written (content diff 0)",
                !text.Contains("\"cornerRadius\"") && !text.Contains("\"core\"")),
            back => Check("   with no keys, loading gives the previous behaviour (0 / 0)",
                back.CornerRadius == 0f && back.Core == 0f));

        var soft = new Scene("RoundedRect");
        var l = soft.CreateEntity("CeilingLight").AddComponent<Light2D>();
        l.Shape = LightShape.Rect; l.CornerRadius = 0.6f; l.Core = 0.25f;
        soft.Update(0f);
        RoundTrip(soft,
            text => Check("★ authored values are written to the file",
                text.Contains("\"cornerRadius\": 0.6") && text.Contains("\"core\": 0.25")),
            back => Check("★ the values come back unchanged", back.CornerRadius == 0.6f && back.Core == 0.25f,
                $"{back.CornerRadius} / {back.Core}"));

        var one = new Scene("CornerOnly");
        var l2 = one.CreateEntity("Lamp").AddComponent<Light2D>();
        l2.Shape = LightShape.Rect; l2.CornerRadius = 1f;
        one.Update(0f);
        RoundTrip(one,
            text => Check("★ authoring only one side writes only that key",
                text.Contains("\"cornerRadius\"") && !text.Contains("\"core\"")),
            back => Check("   partial omission round-trips too", back.CornerRadius == 1f && back.Core == 0f));
    }

    private static void WithTable(string json, Action body)
    {
        var root = Path.Combine(Path.GetTempPath(), "pc_lp_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "Lighting"));
        File.WriteAllText(Path.Combine(root, LightingProfiles.FileName), json);
        var prev = LightingProfiles.ContentRoot;
        try
        {
            LightingProfiles.ContentRoot = root;
            LightingProfiles.Load();
            body();
        }
        finally
        {
            LightingProfiles.ContentRoot = prev;
            LightingProfiles.Clear();
            LightingProfiles.ResetRuntime();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void TestProfileTable()
    {
        Console.WriteLine("--- profile table ---");

        LightingProfiles.Clear();
        var none = LightingProfiles.Resolve("5a1c7e20", "Day", 8f);
        Check("★ with no table there is no modulation (remove the table and the screen is as before)", none.IsNeutral, none.ToString());
        Check("   precondition: Neutral really does nothing (multiplying leaves the colour unchanged)",
            ProfileFrame.Neutral.ModulateAmbient(new Color(110, 110, 130)) == new Color(110, 110, 130));

        const string json = """
        { "version": 1, "rows": [
          { "scene": "*",        "tag": "Day",      "ambientScale": 1.2 },
          { "scene": "5a1c7e20", "tag": "Day",      "ambientScale": 0.8, "ambientTintB": 200 },
          { "scene": "5a1c7e20", "tag": "Day.Rain", "ambientScale": 0.5 }
        ] }
        """;
        WithTable(json, () =>
        {
            Check("precondition: the table loaded (3 rows)", LightingProfiles.Loaded && LightingProfiles.RowCount == 3,
                LightingProfiles.RowCount.ToString());

            var scene = LightingProfiles.Resolve("5a1c7e20", "Day", 8f);
            Check("★ the scene row beats the global row (the more specific wins)", scene.AmbientScale == 0.8f,
                scene.AmbientScale.ToString());
            Check("   that row's other columns come along too (one row is the whole answer)", scene.AmbientTint.B == 200);

            var other = LightingProfiles.Resolve("b83f0d4e", "Day", 8f);
            Check("★ with no scene row it falls back to the global row", other.AmbientScale == 1.2f, other.AmbientScale.ToString());
            Check("   control: the two are different values (if equal, the check above is vacuous)",
                scene.AmbientScale != other.AmbientScale);

            Check("★ a weather suffix uses its own row when there is one (Day.Rain)",
                LightingProfiles.Resolve("5a1c7e20", "Day.Rain", 8f).AmbientScale == 0.5f);
            Check("★ with no weather row, one level of fallback (Snow → Day)",
                LightingProfiles.Resolve("5a1c7e20", "Day.Snow", 8f).AmbientScale == 0.8f);
            Check("★ one level is stripped (Day.Rain.Heavy → Day.Rain)",
                LightingProfiles.Resolve("5a1c7e20", "Day.Rain.Heavy", 8f).AmbientScale == 0.5f);
            Check("★ control: two levels are not stripped (Day.Snow.Heavy stops at Day.Snow and does not go to Day)",
                LightingProfiles.Resolve("5a1c7e20", "Day.Snow.Heavy", 8f).IsNeutral);

            var bark = CaptureStderr(() =>
            {
                LightingProfiles.Resolve("5a1c7e20", "Midnight", 8f);
                LightingProfiles.Resolve("5a1c7e20", "Midnight", 8f);
            });
            Check("★ a missing tag complains", bark.Contains("Midnight"), bark);
            Check("★ the same pair complains only once", CountOccurrences(bark, "Midnight") == 1,
                CountOccurrences(bark, "Midnight").ToString());
            var bark2 = CaptureStderr(() => LightingProfiles.Resolve("b83f0d4e", "Midnight", 8f));
            Check("★ a different scene complains again (the latch is per pair; with a bool a second typo is silent)",
                bark2.Contains("Midnight"), bark2);
            Check("   a missing tag goes to no modulation (it complains without ruining the screen)",
                LightingProfiles.Resolve("5a1c7e20", "Midnight", 8f).IsNeutral);
        });

        var broken = CaptureStderr(() => WithTable("{ this is not json", () =>
            Check("a broken table falls back to no modulation (it does not kill boot)",
                LightingProfiles.Resolve("x", "Day", 8f).IsNeutral)));
        Check("★ a broken table complains", broken.Contains("✘"), broken);
    }

    private static void TestProfileCurve()
    {
        Console.WriteLine("--- hour curves ---");

        Check("★ no curve gives 1.0 (a row that leaves the column unused costs nothing)", LightingProfiles.EvaluateCurve(null, 13f) == 1f);
        Check("   an empty array gives 1.0 too", LightingProfiles.EvaluateCurve(Array.Empty<float[]>(), 13f) == 1f);

        var stops = new[] { new[] { 6f, 1f }, new[] { 12f, 0.5f }, new[] { 18f, 1f } };
        Check("a value on a stop is used as is", LightingProfiles.EvaluateCurve(stops, 12f) == 0.5f);
        Check("★ linear interpolation between stops (9:00 = 0.75)",
            MathF.Abs(LightingProfiles.EvaluateCurve(stops, 9f) - 0.75f) < 1e-5f,
            LightingProfiles.EvaluateCurve(stops, 9f).ToString());
        Check("★ clamped outside the range (0:00 → first value, 23:00 → last value)",
            LightingProfiles.EvaluateCurve(stops, 0f) == 1f && LightingProfiles.EvaluateCurve(stops, 23f) == 1f);
        Check("24-hour wrap (25:00 = 1:00)",
            LightingProfiles.EvaluateCurve(stops, 25f) == LightingProfiles.EvaluateCurve(stops, 1f));

        const string json = """
        { "rows": [ { "scene": "*", "tag": "Day",
            "lightScaleStops": [[6,1.0],[12,0.4],[18,1.0]],
            "grainScaleStops": [[0,2.0],[24,2.0]] } ] }
        """;
        WithTable(json, () =>
        {
            var noon = LightingProfiles.Resolve("*", "Day", 12f);
            var morning = LightingProfiles.Resolve("*", "Day", 6f);
            Check("★ the same row at a different hour gives a different light multiplier (continuous within a phase)",
                MathF.Abs(noon.LightScale - 0.4f) < 1e-5f && morning.LightScale == 1f,
                $"{noon.LightScale} / {morning.LightScale}");
            Check("★ the grain curve comes through too", noon.GrainScale == 2f, noon.GrainScale.ToString());
        });
    }

    private static void TestProfileBlend()
    {
        Console.WriteLine("--- boundary crossfade ---");

        const string json = """
        { "rows": [
          { "scene": "A", "tag": "Day",  "ambientScale": 1.0 },
          { "scene": "B", "tag": "Day",  "ambientScale": 2.0 }
        ] }
        """;
        WithTable(json, () =>
        {
            LightingProfiles.ResetRuntime();

            LightingProfiles.Update("A", "Day", 8f, 0.016f);
            Check("★ the first frame applies immediately (no afterimage)", LightingProfiles.Current.AmbientScale == 1f
                && !LightingProfiles.IsBlending, LightingProfiles.Current.AmbientScale.ToString());

            LightingProfiles.Update("B", "Day", 8f, 0f, blendSeconds: 1f);
            Check("★ changing room starts a transition", LightingProfiles.IsBlending);
            Check("   the transition starts at the old room's value (no jump)", LightingProfiles.Current.AmbientScale == 1f);

            LightingProfiles.Update("B", "Day", 8f, 0.5f, blendSeconds: 1f);
            var mid = LightingProfiles.Current.AmbientScale;
            Check($"★ the intermediate value is between the two ends ({mid:0.00})", mid > 1f && mid < 2f, mid.ToString());

            LightingProfiles.Update("B", "Day", 8f, 0.6f, blendSeconds: 1f);
            Check("★ at the end it is exactly the target", LightingProfiles.Current.AmbientScale == 2f && !LightingProfiles.IsBlending,
                LightingProfiles.Current.AmbientScale.ToString());

            LightingProfiles.Update("A", "Day", 8f, 0f, blendSeconds: 1f);
            LightingProfiles.Update("A", "Day", 8f, 0.3f, blendSeconds: 1f);
            Check("   precondition: a transition is in progress and it is not neutral", LightingProfiles.IsBlending);
            LightingProfiles.ResetRuntime();
            Check("★ ResetRuntime rewinds the transition", !LightingProfiles.IsBlending
                && LightingProfiles.Current.IsNeutral);

            LightingProfiles.Update("B", "Day", 8f, 0.016f, blendSeconds: 1f);
            Check("★ the first Update after a rewind is immediate (a new day does not fade in)",
                !LightingProfiles.IsBlending && LightingProfiles.Current.AmbientScale == 2f,
                $"blending={LightingProfiles.IsBlending} scale={LightingProfiles.Current.AmbientScale}");
        });
    }

    private static void TestLightSwitching()
    {
        Console.WriteLine("--- when lights are on, and off-camera switching ---");

        var always = new Light2D();
        var night = new Light2D { ActiveWhen = LightActiveWhen.NightOnly };
        var sw = new Light2D { ActiveWhen = LightActiveWhen.Switch };

        Check("Always is always on", LightingRenderer.ShouldLightBeOn(always, 3f)
            && LightingRenderer.ShouldLightBeOn(always, 13f));
        Check("★ NightOnly only at night (on at 3:00, off at 13:00)",
            LightingRenderer.ShouldLightBeOn(night, 3f) && !LightingRenderer.ShouldLightBeOn(night, 13f));
        Check("★ the night decision uses the same threshold as TimeOfDay (no new number)",
            LightingRenderer.IsDaylight(12f) && !LightingRenderer.IsDaylight(23f)
            && LightingRenderer.IsDaylight(Systems.TimeOfDay.SunriseHour)
            && LightingRenderer.IsDaylight(Systems.TimeOfDay.SunsetHour));
        Check("Switch follows the flag", !LightingRenderer.ShouldLightBeOn(sw, 3f));
        sw.SwitchOn = true;
        Check("   switching it on turns it on", LightingRenderer.ShouldLightBeOn(sw, 3f));

        var view = Matrix.CreateTranslation(-100f, -50f, 0f);
        var rect = LightingRenderer.ViewRectFromMatrix(view, 320, 180);
        Check($"★ the visible rectangle is derived from the matrix ({rect})",
            rect.Left == 100 && rect.Top == 50 && rect.Width == 320 && rect.Height == 180, rect.ToString());
        var zoomed = LightingRenderer.ViewRectFromMatrix(Matrix.CreateScale(2f, 2f, 1f), 320, 180);
        Check("★ with zoom the visible world shrinks", zoomed.Width == 160 && zoomed.Height == 90, zoomed.ToString());
        Check("★ a degenerate matrix falls back to the whole screen (defers the switch: the safe direction)",
            LightingRenderer.ViewRectFromMatrix(new Matrix(), 320, 180).Width > 1000000);

        var lightAt = new Light2D { Radius = 10f };
        var onScreen = lightAt.WorldBounds(new Vector2(150f, 100f));
        var offScreen = lightAt.WorldBounds(new Vector2(9000f, 9000f));
        Check("★ on screen it cannot commit", !LightingRenderer.CanCommitSwitch(onScreen, rect));
        Check("★ control: off screen it commits", LightingRenderer.CanCommitSwitch(offScreen, rect));

        var lamp = new Light2D { ActiveWhen = LightActiveWhen.NightOnly, OnlyOffCamera = true, Radius = 10f };
        var pos = new Vector2(150f, 100f);
        LightingRenderer.TickSwitch(lamp, pos, 23f, rect, allowDefer: true);
        Check("★ the first decision is immediate (it does not start in the wrong state)", lamp.IsOn == true && lamp.PendingOn == null);

        LightingRenderer.TickSwitch(lamp, pos, 13f, rect, allowDefer: true);
        Check("★ a state change on screen is deferred", lamp.IsOn == true && lamp.PendingOn == false,
            $"{lamp.IsOn} / {lamp.PendingOn}");
        LightingRenderer.TickSwitch(lamp, new Vector2(9000f, 9000f), 13f, rect, allowDefer: true);
        Check("★ leaving the screen commits it", lamp.IsOn == false && lamp.PendingOn == null);

        var plain = new Light2D { ActiveWhen = LightActiveWhen.NightOnly, Radius = 10f };
        LightingRenderer.TickSwitch(plain, pos, 23f, rect, allowDefer: true);
        LightingRenderer.TickSwitch(plain, pos, 13f, rect, allowDefer: true);
        Check("★ control: with OnlyOffCamera off it switches immediately even on screen", plain.IsOn == false);

        var edit = new Light2D { ActiveWhen = LightActiveWhen.NightOnly, OnlyOffCamera = true, Radius = 10f };
        LightingRenderer.TickSwitch(edit, pos, 23f, rect, allowDefer: false);
        LightingRenderer.TickSwitch(edit, pos, 13f, rect, allowDefer: false);
        Check("★ control: while editing (deferral off) it switches immediately even on screen (the slider must show at once)",
            edit.IsOn == false);

        var scene = new Scene("SwitchScene");
        var off = scene.CreateEntity("StreetLamp").AddComponent<Light2D>();
        off.Additive = 1f; off.IsOn = false;
        scene.Update(0f);
        Check("★ a switched-off light does not call for the additive pass either (one door with the draw filter)",
            !LightingRenderer.ResolveScenePasses(scene).Additive);
        off.IsOn = true;
        Check("   control: switching it on calls for it again", LightingRenderer.ResolveScenePasses(scene).Additive);
    }

    private static void TestActiveWhenRoundTrip()
    {
        Console.WriteLine("--- when-on round trip ---");

        var plain = new Scene("Plain");
        plain.CreateEntity("Lamp").AddComponent<Light2D>();
        plain.Update(0f);
        RoundTrip(plain,
            text => Check("★ with defaults, no activeWhen/switchOn/onlyOffCamera keys are written (content diff 0)",
                !text.Contains("\"activeWhen\"") && !text.Contains("\"switchOn\"")
                && !text.Contains("\"onlyOffCamera\"")),
            back => Check("   with no keys, the previous behaviour (Always)",
                back.ActiveWhen == LightActiveWhen.Always && !back.OnlyOffCamera));

        var lamp = new Scene("StreetLamp");
        var l = lamp.CreateEntity("Lamp").AddComponent<Light2D>();
        l.ActiveWhen = LightActiveWhen.NightOnly; l.OnlyOffCamera = true;
        lamp.Update(0f);
        RoundTrip(lamp,
            text => Check("★ authored values are written by name (not by number, so inserting one in the middle does not shift them)",
                text.Contains("\"activeWhen\": \"NightOnly\"") && text.Contains("\"onlyOffCamera\": true")),
            back => Check("★ the values come back unchanged",
                back.ActiveWhen == LightActiveWhen.NightOnly && back.OnlyOffCamera && !back.SwitchOn));

        var runtime = new Scene("Runtime");
        var r = runtime.CreateEntity("Lamp").AddComponent<Light2D>();
        r.ActiveWhen = LightActiveWhen.Switch; r.IsOn = true; r.PendingOn = false;
        runtime.Update(0f);
        RoundTrip(runtime,
            text => Check("★ IsOn/PendingOn are not written to the file (runtime only)",
                !text.Contains("isOn") && !text.Contains("pendingOn")),
            back => Check("   loading starts in the undecided state (null)", back.IsOn == null && back.PendingOn == null));
    }

    private static void Check(string name, bool ok, string? detail = null)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name +
            (ok || detail == null ? "" : $"   [{detail}]"));
        if (ok) _pass++; else _fail++;
    }
}
