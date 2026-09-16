using System;
using System.IO;
using Microsoft.Xna.Framework;
using PixelCore.Editor.Panels;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Rendering;

namespace PixelCore.Editor;

public static class ShadowGizmoSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        _pass = _fail = 0;
        Console.WriteLine("=== Shadow gizmo self-test ===");

        TestRectFollowsAuthoredFields();
        TestDragCenter();
        TestDragEdges();
        TestDragRoundTripsExactly();
        TestShapeDropdown();
        TestAutoCloseContract();

        Console.WriteLine($"=== Shadow gizmo: {_pass} passed, {_fail} failed ===");
    }

    private static SpriteRenderer MakeCaster(Scene scene, out Transform transform)
    {
        var e = scene.CreateEntity("Prop");
        var sr = e.AddComponent<SpriteRenderer>();
        sr.CastShadow = true;
        sr.DrawSize = new Vector2(48, 48);
        transform = e.GetComponent<Transform>()!;
        transform.Position = new Vector2(200, 300);
        scene.Update(0f);
        return sr;
    }

    private static void TestRectFollowsAuthoredFields()
    {
        Console.WriteLine("--- the plate rectangle follows the authored values ---");

        var scene = new Scene("Gizmo");
        var sr = MakeCaster(scene, out var t);

        var auto = SceneViewPanel.ShadowRect(sr);
        Check($"★ automatic size = cell width 48 · centred at the feet (measured {auto.Width}×{auto.Height} @{auto.Center})",
            auto.Width == 48 && auto.Height == (int)MathF.Round(48 * 0.62f)
            && auto.Center.X == 200);

        var renderer = ShadowRenderer.ShadowQuad(
            ShadowRenderer.PlateSize(sr, (int)sr.GetDrawSize().X), t.Position, sr.ShadowOffset);
        Check("★ equals the rectangle the renderer draws (the gizmo does not measure its own way)", auto == renderer);

        sr.ShadowWidth = 30;
        Check("★ follows an explicit width", SceneViewPanel.ShadowRect(sr).Width == 30);
        sr.ShadowHeight = 7;
        Check("★ follows an explicit height", SceneViewPanel.ShadowRect(sr).Height == 7);

        sr.ShadowOffset = new Point(-5, 4);
        var moved = SceneViewPanel.ShadowRect(sr);
        Check("★ follows the offset", moved.Center.X == 195 && MathF.Abs(moved.Center.Y - 304) <= 1,
            moved.Center.ToString());

        t.Position = new Vector2(210, 300);
        Check("   the plate follows when the entity moves", SceneViewPanel.ShadowRect(sr).Center.X == 205);
    }

    private static void TestDragCenter()
    {
        Console.WriteLine("--- centre handle (offset) ---");

        var scene = new Scene("Gizmo");
        var sr = MakeCaster(scene, out var t);
        var rect = SceneViewPanel.ShadowRect(sr);

        var next = SceneViewPanel.DragShadowHandle(
            (sr.ShadowWidth, sr.ShadowHeight, sr.ShadowOffset), rect, t.Position, 0,
            new Vector2(206.4f, 297.6f));
        Check("★ offset = mouse − foot point (rounded to integers)", next.Offset == new Point(6, -2),
            next.Offset.ToString());
        Check("★ the size stays automatic (dragging the centre must not freeze the size)",
            next.W == null && next.H == null);

        sr.ShadowWidth = 30; sr.ShadowHeight = 7;
        var kept = SceneViewPanel.DragShadowHandle((30, 7, Point.Zero), rect, t.Position, 0,
            new Vector2(200, 300));
        Check("   an explicit size is preserved", kept.W == 30 && kept.H == 7);
        Check("   control: placing it on the foot point gives offset 0", kept.Offset == Point.Zero);
    }

    private static void TestDragEdges()
    {
        Console.WriteLine("--- edge handles (size) ---");

        var scene = new Scene("Gizmo");
        var sr = MakeCaster(scene, out var t);
        var rect = SceneViewPanel.ShadowRect(sr);
        int right = rect.Right, bottom = rect.Bottom;

        var narrower = SceneViewPanel.DragShadowHandle(
            (null, null, Point.Zero), rect, t.Position, 1, new Vector2(rect.Left + 10, t.Position.Y));
        Check($"★ dragging the left edge narrows the width by that much (48 → 38) - measured {narrower.W}", narrower.W == 38);
        Check("★ the automatic size materialises into explicit values (otherwise the scale recomputes it and it will not drag)",
            narrower.W.HasValue && narrower.H.HasValue);
        Check("   the height does not change (only the dragged edge moves)", narrower.H == rect.Height);

        var reRect = ShadowRenderer.ShadowQuad(new Point(narrower.W!.Value, narrower.H!.Value),
                                               t.Position, narrower.Offset);
        Check($"★ the right edge stays fixed (it grows in one direction only) - measured {reRect.Right} vs {right}",
            reRect.Right == right, $"{reRect.Right}/{right}");

        var taller = SceneViewPanel.DragShadowHandle(
            (null, null, Point.Zero), rect, t.Position, 4, new Vector2(t.Position.X, bottom + 6));
        var tallRect = ShadowRenderer.ShadowQuad(new Point(taller.W!.Value, taller.H!.Value),
                                                 t.Position, taller.Offset);
        Check($"★ dragging the bottom edge increases the height ({rect.Height} → {taller.H})", taller.H == rect.Height + 6);
        Check("★ the top edge stays fixed", tallRect.Top == rect.Top, $"{tallRect.Top}/{rect.Top}");

        var collapsed = SceneViewPanel.DragShadowHandle(
            (null, null, Point.Zero), rect, t.Position, 1, new Vector2(right + 999, t.Position.Y));
        Check("★ dragging past the opposite side still leaves 1px (it does not flip)", collapsed.W == 1, collapsed.W.ToString());

        var untouched = SceneViewPanel.DragShadowHandle(
            (12, 5, new Point(1, 2)), Rectangle.Empty, t.Position, 2, new Vector2(999, 999));
        Check("   an empty plate leaves the values alone", untouched == (12, 5, new Point(1, 2)));

        var unknown = SceneViewPanel.DragShadowHandle(
            (12, 5, new Point(1, 2)), rect, t.Position, 9, new Vector2(999, 999));
        Check("   an unknown handle number is safe too", unknown == (12, 5, new Point(1, 2)));
    }

    private static void TestDragRoundTripsExactly()
    {
        Console.WriteLine("--- the dragged edge lands exactly where released (exhaustive property) ---");

        var scene = new Scene("Inverse");
        var sr = MakeCaster(scene, out var t);

        int cases = 0, edgeOff = 0, oppositeMoved = 0;
        string firstBad = "";
        foreach (float footX in new[] { 100f, 100.5f, 137f })
        foreach (int w in new[] { 1, 2, 3, 7, 8, 11, 12 })
        foreach (int h in new[] { 1, 3, 4, 7, 12 })
        {
            var foot = new Vector2(footX, 300f);
            var rect = ShadowRenderer.ShadowQuad(new Point(w, h), foot, Point.Zero);

            for (int handle = 1; handle <= 4; handle++)
            {
                float target = handle switch
                {
                    1 => rect.Left - 3f,
                    2 => rect.Right + 3f,
                    3 => rect.Top - 3f,
                    _ => rect.Bottom + 3f,
                };
                var mouse = handle <= 2 ? new Vector2(target, foot.Y) : new Vector2(foot.X, target);
                var next = SceneViewPanel.DragShadowHandle((w, h, Point.Zero), rect, foot, handle, mouse);
                var got = ShadowRenderer.ShadowQuad(new Point(next.W!.Value, next.H!.Value), foot, next.Offset);

                cases++;
                (int gotEdge, int wantEdge, int gotOpp, int wantOpp) = handle switch
                {
                    1 => (got.Left, (int)target, got.Right, rect.Right),
                    2 => (got.Right, (int)target, got.Left, rect.Left),
                    3 => (got.Top, (int)target, got.Bottom, rect.Bottom),
                    _ => (got.Bottom, (int)target, got.Top, rect.Top),
                };
                if (gotEdge != wantEdge)
                {
                    edgeOff++;
                    if (firstBad.Length == 0)
                        firstBad = $"foot {footX} {w}×{h} handle {handle}: dragged edge {gotEdge} ≠ {wantEdge}";
                }
                if (gotOpp != wantOpp)
                {
                    oppositeMoved++;
                    if (firstBad.Length == 0)
                        firstBad = $"foot {footX} {w}×{h} handle {handle}: opposite edge {gotOpp} ≠ {wantOpp}";
                }
            }
        }

        Check($"premise: the exhaustive set is not empty ({cases} cases)", cases > 300);
        Check($"★ the dragged edge lands exactly where it was released (off {edgeOff}/{cases})", edgeOff == 0, firstBad);
        Check($"★ the opposite edge does not move (moved {oppositeMoved}/{cases})", oppositeMoved == 0, firstBad);
    }

    private static void TestShapeDropdown()
    {
        Console.WriteLine("--- inspector shape dropdown (derived) ---");

        const int Ellipse = InspectorPanel.ShadowShapeEllipse, Square = InspectorPanel.ShadowShapeSquare;
        const int Rounded = InspectorPanel.ShadowShapeRounded, Tex = InspectorPanel.ShadowShapeTexture;

        Check("★ radius 1 = ellipse", InspectorPanel.ShadowShapeIndexOf(1f, false) == Ellipse);
        Check("★ radius 0 = rectangle", InspectorPanel.ShadowShapeIndexOf(0f, false) == Square);
        Check("★ in between = rounded rectangle", InspectorPanel.ShadowShapeIndexOf(0.35f, false) == Rounded);
        Check("★ with art it is texture regardless of the radius",
            InspectorPanel.ShadowShapeIndexOf(0.35f, true) == Tex
            && InspectorPanel.ShadowShapeIndexOf(1f, true) == Tex);
        Check("   hand-edited out-of-range values are safe too (1.5 → ellipse · −3 → rectangle)",
            InspectorPanel.ShadowShapeIndexOf(1.5f, false) == Ellipse
            && InspectorPanel.ShadowShapeIndexOf(-3f, false) == Square);

        var scene = new Scene("Shape");
        var sr = MakeCaster(scene, out _);

        sr.ShadowRadius = 0.35f;
        InspectorPanel.ApplyShadowShapeTo(sr, Ellipse);
        Check("★ choosing ellipse sets the radius to 1", sr.ShadowRadius == 1f);
        InspectorPanel.ApplyShadowShapeTo(sr, Square);
        Check("★ choosing rectangle sets the radius to 0", sr.ShadowRadius == 0f);
        InspectorPanel.ApplyShadowShapeTo(sr, Rounded);
        Check("★ going from either end to rounded rectangle gives the middle (0.5)", sr.ShadowRadius == 0.5f);
        sr.ShadowRadius = 0.2f;
        InspectorPanel.ApplyShadowShapeTo(sr, Rounded);
        Check("★ a value already in between is the author's and is left alone", sr.ShadowRadius == 0.2f);

        sr.ShadowTexturePath = "Sprites/Test/WindowPool.png";
        Check("premise: with art attached it reads as texture",
            InspectorPanel.ShadowShapeIndexOf(sr.ShadowRadius, true) == Tex);
        InspectorPanel.ApplyShadowShapeTo(sr, Square);
        Check("★ choosing rectangle removes the art (otherwise the choice does not take)",
            sr.ShadowTexturePath == null && sr.ShadowTexture == null);

        const string inspector = "Editor/Panels/InspectorPanel.cs";
        Check($"premise: actually reads the source ({inspector})", File.Exists(inspector));
        if (!File.Exists(inspector)) return;
        var insp = Strip(File.ReadAllText(inspector));

        Check("★ texture mode draws only the art field instead of size and corners",
            insp.Contains("if (shape == ShadowShapeTexture)")
            && insp.Contains("DrawShadowTextureField(sr);"));
        Check("★ corner radius is not drawn for an ellipse",
            insp.Contains("if (shape != ShadowShapeEllipse)"));
        Check("★ not left greyed out (BeginDisabled) - unused handles are hidden",
            !insp.Contains("ImGui.BeginDisabled(shadowTex)"));
        Check("★ changing the shape is one undo step (radius and art together)",
            insp.Contains("v => { sr.ShadowRadius = v.Item1; sr.SetShadowTexture(v.Item2); }, \"Shadow Shape\""));
        Check("★ a Floor layer caster shows a warning (the renderer skips it silently)",
            insp.Contains("The Floor layer cannot cast shadows"));
    }

    private static void TestAutoCloseContract()
    {
        Console.WriteLine("--- auto-close · mutual exclusion (contract) ---");

        const string sceneView = "Editor/Panels/SceneViewPanel.cs";
        const string inspector = "Editor/Panels/InspectorPanel.cs";
        Check($"premise: actually reads the source ({sceneView})", File.Exists(sceneView));
        Check($"premise: actually reads the source ({inspector})", File.Exists(inspector));
        if (!File.Exists(sceneView) || !File.Exists(inspector)) return;

        var sv = Strip(File.ReadAllText(sceneView));
        var insp = Strip(File.ReadAllText(inspector));

        Check("★ editing closes when the selection changes, the component goes away or CastShadow is switched off",
            sv.Contains("esv.Entity != _state.SelectedEntity")
            && sv.Contains("!esv.Entity.Components.Contains(esv)")
            && sv.Contains("!esv.CastShadow"));

        Check("★ the handles take the mouse before entity dragging (input consumed)",
            sv.Contains("HandleShadowEdit(editShadow, mouseWorldPos)"));

        Check("★ one drag = one undo step (size and offset in one command)",
            sv.Contains("new PropertyCommand<(int? W, int? H, Point Offset)>"));

        Check("★ switching shadow editing on switches collider editing off (two sets of handles never overlap)",
            insp.Contains("if (_state.EditingShadow != null) _state.EditingCollider = null;"));
        Check("★ and the other way round",
            insp.Contains("if (_state.EditingCollider != null) _state.EditingShadow = null;"));
    }

    private static string Strip(string text)
    {
        bool inBlock = false;
        var sb = new System.Text.StringBuilder();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            sb.Append(SourceScan.StripComments(line, ref inBlock)).Append('\n');
        return sb.ToString();
    }

    private static void Check(string label, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  PASS  {label}"); }
        else { _fail++; Console.WriteLine($"  FAIL  {label}" + (detail != null ? $"   [{detail}]" : "")); }
    }
}
