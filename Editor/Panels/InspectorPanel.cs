using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Animation;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Audio;
using PixelCore.Runtime.Particles;
using PixelCore.Runtime.Serialization;
using PixelCore.Editor.Commands;

namespace PixelCore.Editor.Panels;

public class InspectorPanel
{
    private readonly EditorState _state;
    private readonly GraphicsDevice _graphicsDevice;

    private object? _dragStart;

    private string _addQuery = "";
    private int _addSelected;

    public InspectorPanel(EditorState state, GraphicsDevice graphicsDevice)
    {
        _state = state;
        _graphicsDevice = graphicsDevice;
    }

    public void DrawContent()
    {
        if (_state.SelectedEntity != null)
        {
            DrawEntityInspector(_state.SelectedEntity);
        }
        else if (_state.CurrentScene != null)
        {
            DrawSceneSettings(_state.CurrentScene);
        }
    }

    private static readonly string[] LightShapeNames =
        { "Point (circle)", "Cone", "Rect", "Texture (art)" };
    private static readonly string[] LightActiveWhenNames = { "Always", "Night Only", "Switch" };

    private void DrawLight2D(Light2D light)
    {
        int shape = (int)light.Shape;
        EditorWidgets.FieldLabel("Shape");
        if (ImGui.Combo("##Shape", ref shape, LightShapeNames, LightShapeNames.Length)
            && shape != (int)light.Shape)
        {
            var old = light.Shape;
            light.Shape = (LightShape)shape;
            TrackInstant(old, light.Shape, v => light.Shape = v, "Light Shape");
        }

        var c = light.Color;
        var color = new System.Numerics.Vector3(c.R / 255f, c.G / 255f, c.B / 255f);
        EditorWidgets.FieldLabel("Color");
        if (ImGui.ColorEdit3("##Color", ref color, ColorFieldFlags))
            light.Color = new Microsoft.Xna.Framework.Color(color.X, color.Y, color.Z);
        TrackDrag(light.Color, v => light.Color = v, "Light Color");

        var intensity = light.Intensity;
        EditorWidgets.FieldLabel("Intensity");
        if (ImGui.DragFloat("##Intensity", ref intensity, 0.05f, 0f, 8f))
            light.Intensity = intensity;
        TrackDrag(light.Intensity, v => light.Intensity = v, "Light Intensity");

        ImGui.BeginDisabled(light.Negative);
        var additive = light.Additive;
        EditorWidgets.FieldLabel("Additive");
        if (EditorWidgets.SliderFloat("##Additive", ref additive, 0f, 1f))
            light.Additive = additive;
        TrackDrag(light.Additive, v => light.Additive = v, "Light Additive");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("0 = multiply only (default) · 1 = additive only - additive makes things brighter than the original\n" +
                "For neon, headlights and light through door gaps only. Lighting a room is the multiply pass's job");

        var negative = light.Negative;
        EditorWidgets.FieldLabel("Negative");
        if (EditorWidgets.TinyCheckbox("##Negative", ref negative))
        {
            light.Negative = negative;
            TrackInstant(!negative, negative, v => light.Negative = v, "Light Negative");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("A darkness brush - subtracts from the lightmap instead of lighting it (intensity ×0.4)\n" +
                "A tool for painting \"that corner is dark\" without any shadow computation");
        if (light.Negative)
        {
            ImGui.SameLine(0, 6);
            ImGui.TextDisabled("(Additive unused)");
        }

        switch (light.Shape)
        {
            case LightShape.Point:
            {
                var radius = light.Radius;
                EditorWidgets.FieldLabel("Radius");
                if (ImGui.DragFloat("##Radius", ref radius, 1f, 4f, 2048f))
                    light.Radius = radius;
                TrackDrag(light.Radius, v => light.Radius = v, "Light Radius");
                break;
            }
            case LightShape.Cone:
            {
                var rotation = light.Rotation;
                EditorWidgets.FieldLabel("Rotation");
                if (ImGui.DragFloat("##Rotation", ref rotation, 1f, -360f, 360f, "%.0f°"))
                    light.Rotation = rotation;
                TrackDrag(light.Rotation, v => light.Rotation = v, "Light Rotation");

                var length = light.Length;
                EditorWidgets.FieldLabel("Length");
                if (ImGui.DragFloat("##Length", ref length, 1f, 8f, 2048f))
                    light.Length = length;
                TrackDrag(light.Length, v => light.Length = v, "Light Length");

                var spread = light.SpreadAngle;
                EditorWidgets.FieldLabel("Spread");
                if (ImGui.DragFloat("##Spread", ref spread, 0.5f, 2f, 170f, "%.0f°"))
                    light.SpreadAngle = spread;
                TrackDrag(light.SpreadAngle, v => light.SpreadAngle = v, "Light Spread");

                var followSun = light.FollowSun;
                EditorWidgets.FieldLabel("Follow Sun");
                if (EditorWidgets.TinyCheckbox("##Follow Sun", ref followSun))
                {
                    light.FollowSun = followSun;
                    TrackInstant(!followSun, followSun, v => light.FollowSun = v, "Follow Sun");
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Tilts (±35°), lengthens and takes its strength from the sun as the clock moves (off at night)\nThe authored Rotation and Length are relative to noon");
                break;
            }
            case LightShape.Rect:
            {
                var rotation = light.Rotation;
                EditorWidgets.FieldLabel("Rotation");
                if (ImGui.DragFloat("##Rotation", ref rotation, 1f, -360f, 360f, "%.0f°"))
                    light.Rotation = rotation;
                TrackDrag(light.Rotation, v => light.Rotation = v, "Light Rotation");

                var size = new System.Numerics.Vector2(light.RectSize.X, light.RectSize.Y);
                EditorWidgets.FieldLabel("Size");
                if (ImGui.DragFloat2("##Size", ref size, 1f, 2f, 2048f))
                    light.RectSize = new Vector2(MathF.Max(2f, size.X), MathF.Max(2f, size.Y));
                EditorWidgets.AxisTint();
                TrackDrag(light.RectSize, v => light.RectSize = v, "Light Size");

                var feather = light.Feather;
                EditorWidgets.FieldLabel("Feather");
                if (EditorWidgets.SliderFloat("##Feather", ref feather, 0f, 1f))
                    light.Feather = feather;
                TrackDrag(light.Feather, v => light.Feather = v, "Light Feather");

                var corner = light.CornerRadius;
                EditorWidgets.FieldLabel("Corner");
                if (EditorWidgets.SliderFloat("##Corner", ref corner, 0f, 1f))
                    light.CornerRadius = corner;
                TrackDrag(light.CornerRadius, v => light.CornerRadius = v, "Light Corner");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Corner rounding (0 = square corners · 1 = ellipse)\n" +
                        "A ratio of the rectangle's half-extents, so when width and height differ the corners are elliptical too");

                var core = light.Core;
                EditorWidgets.FieldLabel("Core");
                if (EditorWidgets.SliderFloat("##Core", ref core, 0f, 1f))
                    light.Core = core;
                TrackDrag(light.Core, v => light.Core = v, "Light Core");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Gathers brightness towards the centre (0 = even falloff)");
                break;
            }
            case LightShape.Texture:
            {
                DrawLightTextureField(light);

                var texRotation = light.Rotation;
                EditorWidgets.FieldLabel("Rotation");
                if (ImGui.DragFloat("##Rotation", ref texRotation, 1f, -360f, 360f, "%.0f°"))
                    light.Rotation = texRotation;
                TrackDrag(light.Rotation, v => light.Rotation = v, "Light Rotation");

                var scale = new System.Numerics.Vector2(light.Scale.X, light.Scale.Y);
                EditorWidgets.FieldLabel("Scale");
                if (ImGui.DragFloat2("##Scale", ref scale, 0.01f, 0.01f, 64f, "%.2f"))
                    light.Scale = new Vector2(MathF.Max(0.01f, scale.X), MathF.Max(0.01f, scale.Y));
                EditorWidgets.AxisTint();
                TrackDrag(light.Scale, v => light.Scale = v, "Light Scale");

                if (light.Texture != null)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled($"({light.Texture.Width * light.Scale.X:0}×{light.Texture.Height * light.Scale.Y:0}px)");
                }
                else if (!string.IsNullOrEmpty(light.UnresolvedTextureRef))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Accent);
                    ImGui.TextWrapped($"Asset id '{light.UnresolvedTextureRef}' was not found - please pick it again");
                    ImGui.PopStyleColor();
                }
                break;
            }
        }

        int when = (int)light.ActiveWhen;
        EditorWidgets.FieldLabel("Active");
        if (ImGui.Combo("##ActiveWhen", ref when, LightActiveWhenNames, LightActiveWhenNames.Length)
            && when != (int)light.ActiveWhen)
        {
            var old = light.ActiveWhen;
            light.ActiveWhen = (LightActiveWhen)when;
            TrackInstant(old, light.ActiveWhen, v => light.ActiveWhen = v, "Light Active When");
        }

        if (light.ActiveWhen == LightActiveWhen.Switch)
        {
            var switchOn = light.SwitchOn;
            EditorWidgets.FieldLabel("Switch On");
            if (EditorWidgets.TinyCheckbox("##SwitchOn", ref switchOn))
            {
                light.SwitchOn = switchOn;
                TrackInstant(!switchOn, switchOn, v => light.SwitchOn = v, "Light Switch On");
            }
            ImGui.SameLine(0, 6);
            ImGui.TextDisabled("(nothing switches it yet - authored initial value)");
        }

        if (light.ActiveWhen != LightActiveWhen.Always)
        {
            var offCam = light.OnlyOffCamera;
            EditorWidgets.FieldLabel("Off-cam only");
            if (EditorWidgets.TinyCheckbox("##OnlyOffCamera", ref offCam))
            {
                light.OnlyOffCamera = offCam;
                TrackInstant(!offCam, offCam, v => light.OnlyOffCamera = v, "Light Only Off Camera");
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Commits the switch only while off screen - stops a lamp from snapping on in front of the player\n" +
                    "Not deferred while editing (dragging a slider has to show at once)");
        }

        if (_state.CurrentScene is { LightingEnabled: false })
        {
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Accent);
            ImGui.TextWrapped("Scene lighting is off - click empty space and enable it in the scene settings");
            ImGui.PopStyleColor();
        }
    }

    private void DrawSceneSettings(PixelCore.Runtime.Core.Scene scene)
    {
        bool isPrefab = scene.Kind == PixelCore.Runtime.Core.SceneKind.Prefab;
        ImGui.PushFont(ImGuiRenderer.SemiBoldFont);
        if (isPrefab)
            ImGui.TextColored(EditorTheme.Prefab, Icons.Cubes + "  " + scene.Name);
        else
            ImGui.TextDisabled(Icons.Cube + "  " + scene.Name);
        ImGui.PopFont();
        if (isPrefab)
            ImGui.TextDisabled("A prefab - a reusable part placed as instances");

        ImGui.Separator();
        ImGui.Spacing();

        if (SectionHeader(Icons.Camera + "  Camera"))
        {
            ImGui.Indent(8);

            {
                var bc = scene.BackColor;
                var back = new System.Numerics.Vector3(bc.R / 255f, bc.G / 255f, bc.B / 255f);
                EditorWidgets.FieldLabel("Background");
                if (ImGui.ColorEdit3("##backColor", ref back, ColorFieldFlags))
                {
                    scene.BackColor = new Microsoft.Xna.Framework.Color(back.X, back.Y, back.Z);
                    _state.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(
                        "The colour that clears outside the room art (the border) and the letterbox.\n" +
                        "Pure black by default - lighting multiplies over the whole screen,\n" +
                        "so any non-zero value makes the border drift with the time of day.\n" +
                        "Black is not written into the scene file.");
                if (scene.BackColor != Microsoft.Xna.Framework.Color.Black)
                    EditorWidgets.Hint("Not black - it takes lighting and post, so it changes with the time of day");
                ImGui.Spacing();
            }

            var viewZoom = scene.ViewZoom;
            EditorWidgets.FieldLabel("View zoom");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderFloat("##viewZoom", ref viewZoom, 0.8f, 1.5f, "%.2fx"))
            {
                scene.ViewZoom = MathF.Round(viewZoom * 100f) / 100f;
                _state.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(
                    "Changes the internal resolution to adjust the field of view (the art size is unchanged).\n" +
                    "1.00x = 320x180 - an integer scale, the existing path\n" +
                    "Above 1.00x is wider, below it is closer\n" +
                    "Anything but 1 gives a fractional scale and goes through PixZoom (sharp bilinear)\n" +
                    "Keep interiors at 1.00 and raise it only for wide scenes such as a street");
            {
                int vzW = (int)MathF.Floor(320 * scene.ViewZoom * 0.5f + 0.5f) * 2;
                int vzH = (int)MathF.Floor(180 * scene.ViewZoom * 0.5f + 0.5f) * 2;
                EditorWidgets.Hint($"Internal resolution {vzW}x{vzH}" +
                    (MathF.Abs(scene.ViewZoom - 1f) < 0.001f ? " · integer scale" : " · through PixZoom"));
            }

            {
                ImGui.Spacing();
                bool usesPixZoom = MathF.Abs(scene.ViewZoom - 1f) >= 0.001f;
                var sharp = Game1.PixZoomSharpness;
                EditorWidgets.FieldLabel("Sharpness (global)");
                ImGui.BeginDisabled(!usesPixZoom);
                ImGui.SetNextItemWidth(-1);
                if (ImGui.SliderFloat("##sharpness", ref sharp, 1f, 4f, "%.2f"))
                    Game1.PixZoomSharpness = sharp;
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(
                        "How wide a band PixZoom blends pixel boundaries over. It only has an effect at fractional scales.\n" +
                        "1.00 = one screen pixel wide (the original) - the softest\n" +
                        "2.00 = half a pixel wide - crisper. Larger values converge on point sampling\n" +
                        "The cost: the higher it goes, the more the texel-boundary crawl shows while the camera moves\n" +
                        "Global and unsaved - once decided, bake it into Game1.PixZoomSharpness's default");
                EditorWidgets.Hint(usesPixZoom
                    ? $"Ramp width {1f / Game1.PixZoomSharpness:0.00} screen px" +
                      (Game1.PixZoomSharpness <= 1.001f ? " · the original" : "")
                    : "Integer scale - PixZoom does not intervene, so this has no effect");

                ImGui.Spacing();
                var contCompose = Game1.PixZoomContinuousCompose;
                EditorWidgets.FieldLabel("Continuous compose (global)");
                ImGui.BeginDisabled(!usesPixZoom);
                if (EditorWidgets.TinyCheckbox("##continuousCompose", ref contCompose))
                    Game1.PixZoomContinuousCompose = contCompose;
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(
                        "Skips the integer screen-pixel rounding of the final blit. Only has an effect at fractional scales.\n" +
                        "Off = every time the camera crosses an art-pixel boundary the whole screen's pixel edges\n" +
                        "     reblend at once, giving about 20 pulses a second while walking = the shimmer\n" +
                        "On = the same amount is spread into a continuous slide and the pulse disappears\n" +
                        "Integer scales always round (on that path a fractional position is a whole-screen judder)");
                EditorWidgets.Hint(usesPixZoom
                    ? (Game1.PixZoomContinuousCompose ? "Continuous - walking crawl becomes a slide instead of a pulse" : "One-screen-pixel ticks (the old behaviour)")
                    : "Integer scale - always rounded");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawSceneFraming(scene);

            ImGui.Unindent(8);
            ImGui.Spacing();
        }

        if (SectionHeader(Icons.Play + "  Audio"))
        {
            ImGui.Indent(8);
            DrawSceneAudio(scene);
            ImGui.Unindent(8);
            ImGui.Spacing();
        }

        if (SectionHeader(Icons.Lightbulb + "  Lighting"))
        {
            ImGui.Indent(8);

            var enabled = scene.LightingEnabled;
            EditorWidgets.FieldLabel("Enable lighting");
            if (EditorWidgets.TinyCheckbox("##enableLighting", ref enabled))
            {
                scene.LightingEnabled = enabled;
                _state.MarkDirty();
            }

            if (scene.LightingEnabled)
            {
                var exterior = scene.Exterior;
                EditorWidgets.FieldLabel("Exterior (sun)");
                if (EditorWidgets.TinyCheckbox("##exterior", ref exterior))
                {
                    scene.Exterior = exterior;
                    _state.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("The ambient is replaced by the TimeOfDay sun gradient\nCastShadow sprites throw a shadow at the sun angle");

                if (scene.Exterior)
                {
                    EditorWidgets.Hint("Ambient: decided by the clock (the menu bar " + Icons.Sun + " scrubber)");
                }
                else
                {
                    var a = scene.AmbientLight;
                    var ambient = new System.Numerics.Vector3(a.R / 255f, a.G / 255f, a.B / 255f);
                    EditorWidgets.FieldLabel("Ambient");
                    if (ImGui.ColorEdit3("##Ambient", ref ambient, ColorFieldFlags))
                    {
                        scene.AmbientLight = new Microsoft.Xna.Framework.Color(ambient.X, ambient.Y, ambient.Z);
                        _state.MarkDirty();
                    }
                }
                ImGui.Spacing();
                EditorWidgets.Hint("To place a light: right-click in the hierarchy > Create Light");
            }

            ImGui.Unindent(8);
        }

        ImGui.Spacing();
        if (SectionHeader(Icons.Sun + "  Sky"))
        {
            ImGui.Indent(8);
            DrawSkyPresets(scene);
            ImGui.Unindent(8);
        }

        ImGui.Spacing();
        if (SectionHeader(Icons.Film + "  Post-processing"))
        {
            ImGui.Indent(8);
            DrawPostPresets(scene);
            ImGui.Spacing();
            DrawPostProfile(scene.Post);
            ImGui.Unindent(8);
        }
    }

    private int _postPresetSel = -1;
    private string _postPresetName = "";
    private string _postRenameBuf = "";

    private string _skyPresetName = "";

    private void DrawSkyPresets(PixelCore.Runtime.Core.Scene scene)
    {
        var all = new List<PixelCore.Runtime.Rendering.SkyAsset>(PixelCore.Runtime.Rendering.SkyPresetCache.All);
        all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

        var active = PixelCore.Runtime.Rendering.SkyPresetCache.Get(scene.SkyId);
        string current = scene.SkyId == null ? "None (sky off)"
            : active != null ? active.Name : $"(missing: {scene.SkyId})";
        EditorWidgets.FieldLabel("Preset");
        if (ImGui.BeginCombo("##skyPreset", current))
        {
            if (ImGui.Selectable("None (sky off)", scene.SkyId == null)) { scene.ClearSky(); _state.MarkDirty(); }
            ImGui.Separator();
            for (int i = 0; i < all.Count; i++)
            {
                if (ImGui.Selectable(Icons.File + "  " + all[i].Name + "##sky" + i, all[i].Id == scene.SkyId))
                {
                    scene.ApplySkyPreset(all[i].Id);
                    _state.MarkDirty();
                }
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("A screen plate laid behind the world - visible where there are no tiles (beyond a rooftop, outside the map).\nJudge it in the game view (integer scale) - the editor viewport has a non-integer scale and shimmers");
        if (all.Count == 0)
            EditorWidgets.Hint("Create the first .sky with 'Add' below (Content/Sky/)");

        var sky = scene.Sky;
        if (sky != null && active != null)
        {
            bool edited = sky.ValueHash() != active.Sky.ToProfile().ValueHash();
            if (edited)
            {
                ImGui.TextColored(EditorTheme.Accent, "· modified - it reverts unless saved");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("The sliders below differ from the preset values.\nUse 'Save current values' to keep them or 'Revert' to discard them.");
            }
            if (!edited) ImGui.BeginDisabled();
            if (ImGui.SmallButton("Revert##sky")) { scene.ApplySkyPreset(active.Id); _state.MarkDirty(); }
            if (!edited) ImGui.EndDisabled();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Discard the modified values and go back to the preset");
            ImGui.SameLine();
            if (ImGui.SmallButton("Save current values##sky"))
            {
                active.Sky = PixelCore.Runtime.Rendering.SkyData.From(sky);
                PixelCore.Runtime.Rendering.SkyPresetCache.SaveToDisk(active);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Overwrite the .sky file with the values on screen\n- this affects every scene using this preset");

            var rel = PixelCore.Runtime.Assets.AssetRegistry.Instance.GetPath(active.Id);
            EditorWidgets.Hint(Icons.File + "  Shared: " + (rel ?? "(not in the registry)"));

            ImGui.Spacing();
            DrawSkyProfile(sky);
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 60f);
        ImGui.InputTextWithHint("##newSky", "New preset name", ref _skyPresetName, 64);
        ImGui.SameLine();
        bool canAdd = _skyPresetName.Trim().Length > 0;
        if (!canAdd) ImGui.BeginDisabled();
        if (ImGui.SmallButton("Add##sky"))
        {
            var data = PixelCore.Runtime.Rendering.SkyData.From(scene.Sky ?? new PixelCore.Runtime.Rendering.SkyProfile());
            var created = PixelCore.Runtime.Rendering.SkyPresetCache.Create(_skyPresetName.Trim(), data);
            if (created != null) { scene.ApplySkyPreset(created.Id); _state.MarkDirty(); }
            _skyPresetName = "";
        }
        if (!canAdd) ImGui.EndDisabled();
    }

    private void DrawSkyProfile(PixelCore.Runtime.Rendering.SkyProfile s)
    {
        if (ImGui.TreeNodeEx("Base", ImGuiTreeNodeFlags.DefaultOpen))
        {
            PostColor("Top", s.SkyTop, c => s.SkyTop = c);
            PostColor("Bottom", s.SkyBottom, c => s.SkyBottom = c);
            SkyInt("Bands (0 = continuous)", s.SkyBands, 0, 64, v => s.SkyBands = v == 1 ? 0 : v);
            var dither = s.Dither;
            EditorWidgets.FieldLabel("Dither");
            if (EditorWidgets.TinyCheckbox("##skydither", ref dither)) { s.Dither = dither; _state.MarkDirty(); }
            ImGui.TreePop();
        }
        if (ImGui.TreeNodeEx("Clouds", ImGuiTreeNodeFlags.DefaultOpen))
        {
            PostColor("Light", s.CloudLight, c => s.CloudLight = c);
            PostColor("Body", s.CloudBody, c => s.CloudBody = c);
            PostColor("Shade", s.CloudShadow, c => s.CloudShadow = c);
            EditorWidgets.FieldLabel("Shape");
            int shape = Math.Clamp(s.Shape, 0, 1);
            if (ImGui.Combo("##skyShape", ref shape, "Noise (blotches)\0Puffy (union of circles)\0"))
            {
                s.Shape = shape;
                _state.MarkDirty();
            }
            PostSlider("Fluff", s.Fluff, 0f, 12f, v => s.Fluff = v);
            PostSlider("Coverage", s.Coverage, 0f, 1f, v => s.Coverage = v);
            PostSlider("Size", s.NearScale, 40f, 800f, v => s.NearScale = v);
            PostSlider("Horizontal stretch", s.Stretch, 1f, 3f, v => s.Stretch = v);
            PostSlider("Flat base", s.FlatBase, 0f, 1f, v => s.FlatBase = v);
            PostSlider("Base row height", s.RowHeight, 16f, 200f, v => s.RowHeight = v);
            PostSlider("Puffiness", s.Lumpy, 0f, 1f, v => s.Lumpy = v);
            SkyInt("Layers", s.Layers, 1, 3, v => s.Layers = v);
            PostSlider("Back layer scale", s.LayerFalloff, 0.3f, 0.9f, v => s.LayerFalloff = v);
            PostSlider("Haze", s.Haze, 0f, 1f, v => s.Haze = v);
            PostSlider("Variation", s.Detail, 0f, 1f, v => s.Detail = v);
            PostSlider("Smoothness", s.Smooth, 0f, 1f, v => s.Smooth = v);
            EditorWidgets.FieldLabel("Shading");
            int shade = Math.Clamp(s.Shade, 0, 2);
            if (ImGui.Combo("##skyShading", ref shade, "Flat\0Shaded base\0" + "3 tones\0"))
            {
                s.Shade = shade;
                _state.MarkDirty();
            }
            PostSlider("Shadow depth", s.ShadowDepth, 1f, 12f, v => s.ShadowDepth = v);
            PostSlider("Light angle", s.LightAngle, 0f, 360f, v => s.LightAngle = v);
            PostSlider("Relief", s.Relief, 0f, 0.2f, v => s.Relief = v);
            SkyInt("Seed", s.Seed, 0, 9999, v => s.Seed = v);
            ImGui.TreePop();
        }
        if (ImGui.TreeNodeEx("Motion", ImGuiTreeNodeFlags.DefaultOpen))
        {
            PostSlider("Wind speed", s.WindSpeed, 0f, 60f, v => s.WindSpeed = v);
            PostSlider("Wind angle", s.WindAngle, 0f, 360f, v => s.WindAngle = v);
            PostSlider("Morph", s.Morph, 0f, 1f, v => s.Morph = v);
            PostSlider("Update Hz", s.TickHz, 1f, 60f, v => s.TickHz = v);
            PostSlider("Parallax", s.Parallax, 0f, 1f, v => s.Parallax = v);
            ImGui.TreePop();
        }
    }

    private void SkyInt(string label, int value, int min, int max, Action<int> set)
    {
        EditorWidgets.FieldLabel(label);
        if (ImGui.SliderInt("##sky" + label, ref value, min, max)) { set(value); _state.MarkDirty(); }
    }

    private const string PostDeletePopup = "Delete preset##postdel";
    private const string PostRenamePopup = "Rename preset##postren";

    private void DrawSceneFraming(PixelCore.Runtime.Core.Scene scene)
    {
        var view = SceneViewCenterProvider?.Invoke() ?? Vector2.Zero;
        Vector2 RoundV(Vector2 v) => new(MathF.Round(v.X), MathF.Round(v.Y));

        var fixedCam = scene.CameraFixed;
        EditorWidgets.FieldLabel("Fix the camera");
        if (EditorWidgets.TinyCheckbox("##fixCamera", ref fixedCam))
        {
            scene.CameraFixed = fixedCam;
            if (fixedCam && scene.CameraFixedPos == Vector2.Zero)
                scene.CameraFixedPos = RoundV(view);
            _state.MarkDirty();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "In this room the camera stops following the player and stands in one place.\n" +
                "Switching it on puts the scene view's current centre there (adjustable below).\n" +
                "For rooms smaller than the screen, such as a bathroom, and rooms with a set composition.");

        if (scene.CameraFixed)
        {
            var fp = new System.Numerics.Vector2(scene.CameraFixedPos.X, scene.CameraFixedPos.Y);
            EditorWidgets.FieldLabel("Fixed position");
            if (ImGui.DragFloat2("##fixedPos", ref fp, 1f, 0f, 0f, "%.0f"))
            {
                scene.CameraFixedPos = RoundV(new Vector2(fp.X, fp.Y));
                _state.MarkDirty();
            }
            EditorWidgets.AxisTint();
            if (ImGui.Button("Put it here (the scene view centre)", new System.Numerics.Vector2(-1, 0)))
            {
                scene.CameraFixedPos = RoundV(view);
                _state.MarkDirty();
            }
        }

        ImGui.Spacing();

        var boundsOn = scene.CameraBoundsEnabled;
        EditorWidgets.FieldLabel("Camera bounds");
        if (EditorWidgets.TinyCheckbox("##cameraBounds", ref boundsOn))
        {
            scene.CameraBoundsEnabled = boundsOn;
            if (boundsOn && scene.CameraBounds.Width <= 0)
            {
                int w = (int)MathF.Floor(320 * scene.ViewZoom * 0.5f + 0.5f) * 2;
                int h = (int)MathF.Floor(180 * scene.ViewZoom * 0.5f + 0.5f) * 2;
                var c = RoundV(view);
                scene.CameraBounds = new Rectangle((int)c.X - w, (int)c.Y - h, w * 2, h * 2);
            }
            _state.MarkDirty();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "Confines the camera inside this rectangle (blocking the void beyond the map).\n" +
                "When one axis is smaller than the screen, that axis becomes centre-locked.\n" +
                "Ignored in a fixed-camera room - the position is already decided.");

        if (scene.CameraBoundsEnabled)
        {
            var b = scene.CameraBounds;
            var min = new int[] { b.X, b.Y };
            var size = new int[] { b.Width, b.Height };
            EditorWidgets.FieldLabel("Top left");
            if (ImGui.DragInt2("##boundsMin", ref min[0]))
            {
                scene.CameraBounds = new Rectangle(min[0], min[1], b.Width, b.Height);
                _state.MarkDirty();
            }
            EditorWidgets.AxisTint();
            EditorWidgets.FieldLabel("Size");
            if (ImGui.DragInt2("##boundsSize", ref size[0], 1f, 1, 100000))
            {
                scene.CameraBounds = new Rectangle(b.X, b.Y, Math.Max(1, size[0]), Math.Max(1, size[1]));
                _state.MarkDirty();
            }
            EditorWidgets.AxisTint();

            {
                b = scene.CameraBounds;
                int vw = (int)MathF.Floor(320 * scene.ViewZoom * 0.5f + 0.5f) * 2;
                int vh = (int)MathF.Floor(180 * scene.ViewZoom * 0.5f + 0.5f) * 2;
                bool narrowX = b.Width < vw, narrowY = b.Height < vh;
                if (narrowX && narrowY)
                    EditorWidgets.Hint($"{Icons.Warning}  Smaller than the screen ({vw}x{vh}) - the camera locks to the bounds centre");
                else if (narrowX)
                    EditorWidgets.Hint($"{Icons.Warning}  Narrower than the screen ({vw}px) - X is centre-locked and only Y follows");
                else if (narrowY)
                    EditorWidgets.Hint($"{Icons.Warning}  Shorter than the screen ({vh}px) - Y is centre-locked and only X follows");
            }
        }

        ImGui.Spacing();

        var dampOn = scene.CameraDampingEnabled;
        EditorWidgets.FieldLabel("Camera damping");
        if (EditorWidgets.TinyCheckbox("##cameraDamping", ref dampOn))
        {
            scene.CameraDampingEnabled = dampOn;
            _state.MarkDirty();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "This room's own camera follow speed, per axis.\n" +
                "Higher is faster, and 0 makes that axis snap instantly.\n" +
                "Off means the game default of 2.3 / 2.3.\n\n" +
                "Examples: 4/4 (floaty), 0.4/0.4 (snappy), 2/0 (vertical instant).\n" +
                "Differing per room stops it reading as one game, so switch it on only with a reason.");

        if (scene.CameraDampingEnabled)
        {
            var d = new System.Numerics.Vector2(scene.CameraDamping.X, scene.CameraDamping.Y);
            EditorWidgets.FieldLabel("Damping X/Y");
            if (ImGui.DragFloat2("##dampingValues", ref d, 0.05f, 0f, 20f, "%.2f"))
            {
                scene.CameraDamping = new Vector2(MathF.Max(0f, d.X), MathF.Max(0f, d.Y));
                _state.MarkDirty();
            }
            EditorWidgets.AxisTint();
            if (scene.CameraDamping.X == 0f || scene.CameraDamping.Y == 0f)
                EditorWidgets.Hint($"{Icons.Warning}  An axis at 0 follows <b>instantly</b> (no damping)"
                    .Replace("<b>", "").Replace("</b>", ""));
        }

        if (scene.CameraFixed || scene.CameraBoundsEnabled)
        {
            ImGui.Spacing();
            bool editing = _state.EditingCameraFraming;
            if (editing)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, EditorTheme.AccentSoft);
                ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Accent);
            }
            string label = editing ? Icons.Crosshair + "  Editing - drag the handles in the scene view"
                                   : Icons.Crosshair + "  Edit the camera";
            if (ImGui.Button(label, new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, 0)))
                _state.EditingCameraFraming = !editing;
            if (editing) ImGui.PopStyleColor(2);
        }

        EditorWidgets.Hint(scene.CameraFixed
            ? "Fixed - entering play goes to this position (free panning while editing)"
            : scene.CameraBoundsEnabled
                ? "Follow plus bounds (not applied while editing - only when the game holds the camera)"
                : "Follow - unrestricted");
    }

    public Func<Vector2>? SceneViewCenterProvider { get; set; }

    private void DrawSceneAudio(PixelCore.Runtime.Core.Scene scene)
    {
        EditorWidgets.FieldLabel("Ambience");
        ImGui.TextDisabled($"{scene.Ambients.Count} layers");

        int removeAt = -1;
        for (int i = 0; i < scene.Ambients.Count; i++)
        {
            var track = scene.Ambients[i];
            ImGui.PushID(i);

            DrawAudioField($"amb{i}", track.SoundId, -96f, v =>
            {
                track.SoundId = v;
                _state.MarkDirty();
            });

            ImGui.SameLine(0, 4);
            var vol = track.Volume;
            ImGui.SetNextItemWidth(58f);
            if (ImGui.SliderFloat("##vol", ref vol, 0f, 1f, "%.2f"))
            {
                track.Volume = vol;
                _state.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("The strength in this room - the same sound can differ per room\n(rain indoors 0.3, under the eaves 0.6)");

            ImGui.SameLine(0, 4);
            ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0, 0, 0, 0));
            if (ImGui.Button(Icons.Xmark, new System.Numerics.Vector2(20, 0))) removeAt = i;
            ImGui.PopStyleColor();

            ImGui.PopID();
        }

        if (removeAt >= 0)
        {
            scene.Ambients.RemoveAt(removeAt);
            _state.MarkDirty();
        }

        if (ImGui.Button(Icons.Plus + "  Add ambience", new System.Numerics.Vector2(-1, 0)))
        {
            scene.Ambients.Add(new PixelCore.Runtime.Audio.AmbientTrack(""));
            _state.MarkDirty();
        }
        EditorWidgets.Hint("A sound present in both rooms is not interrupted when moving between them (a list diff)");
    }

    private void DrawPostPresets(PixelCore.Runtime.Core.Scene scene)
    {
        var presets = scene.PostPresets;

        _postPresetSel = presets.FindIndex(p => p.Key == scene.ActivePostKey);

        string current = _postPresetSel >= 0 ? presets[_postPresetSel].Key : "None (post off)";
        EditorWidgets.FieldLabel("Preset");
        if (ImGui.BeginCombo("##postPreset", current))
        {
            if (ImGui.Selectable("None (post off)", _postPresetSel < 0))
            {
                scene.ClearPost();
                _state.MarkDirty();
            }
            ImGui.Separator();
            for (int i = 0; i < presets.Count; i++)
            {
                var label = (presets[i].IsShared ? Icons.File + "  " : "") + presets[i].Key;
                if (ImGui.Selectable(label + "##pp" + i, i == _postPresetSel))
                {
                    scene.ApplyPostPreset(presets[i].Key);
                    _state.MarkDirty();
                }
            }
            ImGui.EndCombo();
        }

        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("POST_FILE");
                if (payload.NativePtr != null)
                {
                    var dragged = DraggedPostPathProvider?.Invoke();
                    if (!string.IsNullOrEmpty(dragged)) OnPostAssetDropped?.Invoke(dragged);
                }
            }
            ImGui.EndDragDropTarget();
        }
        if (presets.Count == 0)
            EditorWidgets.Hint("Drag a .post from Content/Post here");

        if (_postPresetSel >= 0 && presets[_postPresetSel].IsShared)
        {
            var postRel = Shell.AssetPing.FromId(presets[_postPresetSel].AssetId);
            EditorWidgets.FieldLabel("File");
            DrawAssetRefField("postFile", Icons.File, postRel != null ? Path.GetFileName(postRel) : "(not in the registry)",
                postRel, tooltip: "A shared .post - editing it affects every other scene using this preset", showPicker: false);
        }

        if (_postPresetSel >= 0)
        {
            var preset = presets[_postPresetSel];

            bool edited = scene.Post.ValueHash() != preset.Profile.ValueHash();
            if (edited)
            {
                ImGui.TextColored(EditorTheme.Accent, "· modified - it reverts unless saved");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("The sliders below differ from the preset values.\nUse 'Save current values' to keep them or 'Revert' to discard them.");
            }

            if (!edited) ImGui.BeginDisabled();
            if (ImGui.SmallButton("Revert")) { scene.ApplyPostPreset(preset.Key); _state.MarkDirty(); }
            if (!edited) ImGui.EndDisabled();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Discard the modified values and go back to the preset");
            ImGui.SameLine();
            if (ImGui.SmallButton("Blend")) scene.BlendPostPreset(preset.Key);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("A 0.7 second crossfade preview (SetVolumeLerp)");
            ImGui.SameLine();
            if (ImGui.SmallButton("Save current values")) SavePostPreset(preset, scene);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(preset.IsShared
                    ? "Overwrite the .post file with the values on screen\n- this affects every scene using this preset"
                    : "Overwrite this preset with the values on screen (this scene only)");

            if (ImGui.SmallButton("Rename"))
            {
                _postRenameBuf = preset.Key;
                ImGui.OpenPopup(PostRenamePopup);
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Delete")) ImGui.OpenPopup(PostDeletePopup);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Scene settings have no undo - this cannot be reversed");

            if (!preset.IsShared)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Extract as an asset")) PromotePostPreset(preset);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Split it into a Content/Post/*.post file to share with other scenes\n(the reference survives moving folders, because it is by id)");
            }
            else
            {
                var path = PixelCore.Runtime.Assets.AssetRegistry.Instance.GetPath(preset.AssetId!);
                EditorWidgets.Hint(Icons.File + "  Shared: " + (path ?? "(the asset is missing - pick it again)"));
            }

            DrawPostRenamePopup(scene);
            DrawPostDeletePopup(scene);
        }

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 60f);
        ImGui.InputTextWithHint("##newPreset", "New preset name", ref _postPresetName, 64);
        ImGui.SameLine();
        bool canAdd = _postPresetName.Trim().Length > 0;
        if (!canAdd) ImGui.BeginDisabled();
        if (ImGui.SmallButton("Add"))
        {
            var key = _postPresetName.Trim();
            scene.PostPresets.Add(new PixelCore.Runtime.Rendering.PostPreset
            {
                Key = key,
                Profile = scene.Post.Clone(),
            });
            scene.ActivePostKey = key;
            _postPresetName = "";
            _state.MarkDirty();
        }
        if (!canAdd) ImGui.EndDisabled();
    }

    private void SavePostPreset(PixelCore.Runtime.Rendering.PostPreset preset,
        PixelCore.Runtime.Core.Scene scene)
    {
        preset.Profile = scene.Post.Clone();
        if (preset.IsShared)
        {
            var asset = PixelCore.Runtime.Assets.PostAssetCache.Get(preset.AssetId);
            if (asset != null)
            {
                asset.Profile = PixelCore.Runtime.Serialization.PostData.From(preset.Profile);
                PixelCore.Runtime.Assets.PostAssetCache.SaveToDisk(asset);
            }
        }
        _state.MarkDirty();
    }

    private void PromotePostPreset(PixelCore.Runtime.Rendering.PostPreset preset)
    {
        var asset = PixelCore.Runtime.Assets.PostAssetCache.Create(
            preset.Key, PixelCore.Runtime.Serialization.PostData.From(preset.Profile));
        if (asset != null)
        {
            preset.AssetId = asset.Id;
            _state.MarkDirty();
        }
    }

    private void DrawPostRenamePopup(PixelCore.Runtime.Core.Scene scene)
    {
        var presets = scene.PostPresets;
        if (!ImGui.BeginPopupModal(PostRenamePopup, ImGuiWindowFlags.AlwaysAutoResize)) return;

        ImGui.SetNextItemWidth(240f);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        bool submitted = ImGui.InputText("##name", ref _postRenameBuf, 64, ImGuiInputTextFlags.EnterReturnsTrue);

        var name = _postRenameBuf.Trim();
        bool valid = name.Length > 0;
        if (valid && !ImGui.IsWindowAppearing())
        {
            for (int i = 0; i < presets.Count; i++)
                if (i != _postPresetSel && presets[i].Key == name) { valid = false; break; }
            if (!valid) ImGui.TextColored(EditorTheme.Danger, "A preset with that name already exists");
        }

        if (!valid) ImGui.BeginDisabled();
        if (ImGui.Button("OK", new System.Numerics.Vector2(96, 0)) || (submitted && valid))
        {
            bool wasActive = scene.ActivePostKey == presets[_postPresetSel].Key;
            presets[_postPresetSel].Key = name;
            if (wasActive) scene.ActivePostKey = name;
            _state.MarkDirty();
            ImGui.CloseCurrentPopup();
        }
        if (!valid) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new System.Numerics.Vector2(96, 0))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void DrawPostDeletePopup(PixelCore.Runtime.Core.Scene scene)
    {
        if (!ImGui.BeginPopupModal(PostDeletePopup, ImGuiWindowFlags.AlwaysAutoResize)) return;

        var presets = scene.PostPresets;
        var preset = presets[_postPresetSel];
        ImGui.Text($"Delete the preset '{preset.Key}'?");
        ImGui.Spacing();
        ImGui.TextDisabled(preset.IsShared
            ? "The .post file is not deleted - it only leaves this scene's list"
            : "Scene settings have no undo - this cannot be reversed");
        ImGui.Spacing();

        if (ImGui.Button("Delete", new System.Numerics.Vector2(96, 0)))
        {
            if (scene.ActivePostKey == preset.Key) scene.ActivePostKey = null;
            presets.RemoveAt(_postPresetSel);
            _postPresetSel = -1;
            _state.MarkDirty();
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new System.Numerics.Vector2(96, 0))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void DrawPostProfile(PixelCore.Runtime.Rendering.PostProfile post)
    {
        if (ImGui.TreeNodeEx("Colour grading", ImGuiTreeNodeFlags.DefaultOpen))
        {
            PostSlider("Exposure (EV)", post.Exposure, -3f, 3f, v => post.Exposure = v);
            PostSlider("Contrast", post.Contrast, -100f, 100f, v => post.Contrast = v);
            PostColor("Colour filter", post.ColorFilter, c => post.ColorFilter = c);
            PostSlider("Hue shift", post.HueShift, -180f, 180f, v => post.HueShift = v);
            PostSlider("Saturation", post.Saturation, -100f, 100f, v => post.Saturation = v);
            PostSlider("Temperature", post.Temperature, -100f, 100f, v => post.Temperature = v);
            PostSlider("Temperature tint", post.TempTint, -100f, 100f, v => post.TempTint = v);

            EditorWidgets.FieldLabel("Tonemapping");
            int tone = (int)post.Tonemap;
            if (ImGui.Combo("##tonemapping", ref tone, "None\0Neutral\0ACES\0"))
            {
                post.Tonemap = (PixelCore.Runtime.Rendering.ToneMode)tone;
                _state.MarkDirty();
            }
            ImGui.TreePop();
        }

        if (ImGui.TreeNodeEx("Lift · Gamma · Gain"))
        {
            PostWheel("Lift (shadows)", post.Lift, v => post.Lift = v);
            PostWheel("Gamma (midtones)", post.Gamma, v => post.Gamma = v);
            PostWheel("Gain (highlights)", post.Gain, v => post.Gain = v);
            ImGui.TreePop();
        }

        if (ImGui.TreeNodeEx("Shadows · Midtones · Highlights"))
        {
            PostWheel("Shadows", post.SmhShadows, v => post.SmhShadows = v);
            PostWheel("Midtones", post.SmhMidtones, v => post.SmhMidtones = v);
            PostWheel("Highlights", post.SmhHighlights, v => post.SmhHighlights = v);
            ImGui.TreePop();
        }

        if (ImGui.TreeNodeEx("Vignette", ImGuiTreeNodeFlags.DefaultOpen))
        {
            PostColor("Colour", post.VignetteColor, c => post.VignetteColor = c);
            PostSlider("Intensity", post.Vignette, 0f, 1f, v => post.Vignette = v);
            PostSlider("Smoothness", post.VignetteSmooth, 0.01f, 1f, v => post.VignetteSmooth = v);
            var rounded = post.VignetteRounded;
            EditorWidgets.FieldLabel("Rounded");
            if (EditorWidgets.TinyCheckbox("##vignetteRounded", ref rounded))
            {
                post.VignetteRounded = rounded;
                _state.MarkDirty();
            }
            ImGui.TreePop();
        }

        if (ImGui.TreeNodeEx("Bloom", ImGuiTreeNodeFlags.DefaultOpen))
        {
            PostSlider("Intensity", post.BloomIntensity, 0f, 3f, v => post.BloomIntensity = v);
            PostSlider("Threshold", post.BloomThreshold, 0f, 1.5f, v => post.BloomThreshold = v);
            PostSlider("Scatter", post.BloomScatter, 0f, 1f, v => post.BloomScatter = v);
            PostColor("Tint", post.BloomTint, c => post.BloomTint = c);
            ImGui.TreePop();
        }

        if (ImGui.TreeNodeEx("Grain"))
        {
            PostSlider("Intensity", post.GrainIntensity, 0f, 1f, v => post.GrainIntensity = v);
            PostSlider("Cells", post.GrainCells, 480f, 1080f, v => post.GrainCells = v);
            ImGui.TreePop();
        }
    }

    private void PostSlider(string label, float value, float min, float max, Action<float> set)
    {
        EditorWidgets.FieldLabel(label);
        if (EditorWidgets.SliderFloat("##pp" + label, ref value, min, max))
        {
            set(value);
            _state.MarkDirty();
        }
    }

    private void PostColor(string label, Microsoft.Xna.Framework.Color color, Action<Microsoft.Xna.Framework.Color> set)
    {
        var v = new System.Numerics.Vector3(color.R / 255f, color.G / 255f, color.B / 255f);
        EditorWidgets.FieldLabel(label);
        if (ImGui.ColorEdit3("##pp" + label, ref v, ColorFieldFlags))
        {
            set(new Microsoft.Xna.Framework.Color(v.X, v.Y, v.Z));
            _state.MarkDirty();
        }
    }

    private void PostWheel(string label, Microsoft.Xna.Framework.Vector4 value, Action<Microsoft.Xna.Framework.Vector4> set)
    {
        var rgb = new System.Numerics.Vector3(value.X, value.Y, value.Z);
        EditorWidgets.FieldLabel(label);
        float avail = ImGui.GetContentRegionAvail().X;
        ImGui.SetNextItemWidth(avail * 0.45f);
        bool changed = ImGui.ColorEdit3("##ppw" + label, ref rgb,
            ColorFieldFlags | ImGuiColorEditFlags.Float | ImGuiColorEditFlags.HDR);
        ImGui.SameLine();
        float w = value.W;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        changed |= ImGui.SliderFloat("##ppww" + label, ref w, -0.5f, 0.5f, "%.3f");
        if (changed)
        {
            set(new Microsoft.Xna.Framework.Vector4(rgb.X, rgb.Y, rgb.Z, w));
            _state.MarkDirty();
        }
    }

    private static bool SectionHeader(string label)
    {
        ImGui.PushFont(ImGuiRenderer.SemiBoldFont);
        bool open = ImGui.CollapsingHeader(label, ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.PopFont();
        return open;
    }

    private const ImGuiColorEditFlags ColorFieldFlags = ImGuiColorEditFlags.DisplayHex;

    private void TrackDrag<T>(T valueNow, Action<T> setter, string label)
    {
        if (ImGui.IsItemActivated())
            _dragStart = valueNow;

        if (ImGui.IsItemDeactivatedAfterEdit() && _dragStart is T start)
        {
            if (!EqualityComparer<T>.Default.Equals(start, valueNow))
            {
                _state.CommandHistory.AddExecuted(new PropertyCommand<T>(this, label, start, valueNow, setter, "Change " + label, _state.SelectedEntity));
                _state.MarkDirty();
                _state.KeepSelectionThroughStop();
            }
            _dragStart = null;
        }
    }

    private void TrackInstant<T>(T oldValue, T newValue, Action<T> setter, string label)
    {
        _state.CommandHistory.AddExecuted(new PropertyCommand<T>(this, label, oldValue, newValue, setter, "Change " + label, _state.SelectedEntity));
        _state.MarkDirty();
        _state.KeepSelectionThroughStop();
    }

    private void DrawEntityInspector(Entity entity)
    {
        int selCount = _state.Selection.Count;
        bool multi = selCount > 1 && _state.IsSelected(entity);

        var active = entity.Active;
        if (EditorWidgets.TinyCheckbox("##active", ref active))
        {
            if (multi) SetActiveOnSelection(active);
            else
            {
                entity.Active = active;
                TrackInstant(!active, active, v => entity.Active = v, "Active");
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(multi ? $"Active - applied to all {selCount} selected" : "Active");
        ImGui.SameLine();

        var name = entity.Name;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##name", ref name, 64))
            entity.Name = name;
        TrackDrag(entity.Name, v => entity.Name = v, "Name");

        if (multi)
            EditorWidgets.Hint($"{entity.Name} and {selCount - 1} more selected - Active and Position apply to all");

        ImGui.Spacing();

        HashSet<Type>? commonTypes = null;
        if (multi)
        {
            commonTypes = new HashSet<Type>();
            foreach (var c in entity.Components) commonTypes.Add(c.GetType());
            foreach (var other in _state.Selection)
            {
                if (other == entity) continue;
                var his = new HashSet<Type>();
                foreach (var c in other.Components) his.Add(c.GetType());
                commonTypes.IntersectWith(his);
            }
        }

        if (!multi) WarnMultipleRenderers(entity);

        foreach (var component in entity.Components)
        {
            if (commonTypes != null && !commonTypes.Contains(component.GetType())) continue;
            DrawComponent(component, multiLocked: multi && component is not Transform);
        }

        if (multi && commonTypes!.Count == 0)
            EditorWidgets.Hint("The selected entities share no components.");

        ImGui.Spacing();
        if (multi) ImGui.BeginDisabled();
        if (ImGui.Button("Add Component", new System.Numerics.Vector2(-1, 0)))
        {
            ImGui.OpenPopup("AddComponentPopup");
        }
        if (multi) ImGui.EndDisabled();
        if (multi && ImGui.IsItemHovered())
            ImGui.SetTooltip("Cannot add while several are selected - select just one");

        if (ImGui.BeginPopup("AddComponentPopup", ImGuiWindowFlags.NoNavInputs))
        {
            DrawAddComponentPopup(entity);
            ImGui.EndPopup();
        }

        FlushComponentAction();
    }

    private void DrawAddComponentPopup(Entity entity)
    {
        const float Width = 280f;

        if (ImGui.IsWindowAppearing())
        {
            _addQuery = "";
            _addSelected = 0;
            ImGui.SetKeyboardFocusHere();
        }
        ImGui.SetNextItemWidth(Width);
        bool enter = ImGui.InputTextWithHint("##addQuery", Icons.Search + "  Search components...", ref _addQuery, 64,
            ImGuiInputTextFlags.EnterReturnsTrue);
        if (ImGui.IsItemEdited()) _addSelected = 0;

        if (ComponentClipboard.HasValue)
        {
            bool ok = CanPasteAsNew(entity);
            if (ImGui.MenuItem($"Paste: {ComponentClipboard.Label}", "", false, ok))
                PasteAsNew(entity);
            if (!ok && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Already attached - to change only the values use the component overflow menu, Paste values");
        }

        var groups = ComponentCatalog.Grouped(_addQuery);

        var addable = new List<ComponentCatalog.Entry>();
        foreach (var g in groups)
            foreach (var e in g.Items)
                if (CanAdd(entity, e.Type)) addable.Add(e);

        int dir = ImGui.IsKeyPressed(ImGuiKey.DownArrow, true) ? 1
                : ImGui.IsKeyPressed(ImGuiKey.UpArrow, true) ? -1 : 0;
        if (addable.Count > 0) _addSelected = Math.Clamp(_addSelected + dir, 0, addable.Count - 1);
        ComponentCatalog.Entry? cursor = addable.Count > 0 ? addable[_addSelected] : null;

        float rowH = ImGui.GetTextLineHeightWithSpacing();
        int rows = 0; foreach (var g in groups) rows += g.Items.Count;
        float wanted = rows * rowH + groups.Count * (rowH + 8f) + 6f;
        ImGui.BeginChild("##addList", new System.Numerics.Vector2(Width, MathF.Min(wanted, 14f * rowH)));

        foreach (var (category, items) in groups)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.SeparatorText(category);
            ImGui.PopStyleColor();

            foreach (var e in items)
            {
                bool exists = !CanAdd(entity, e.Type);
                bool on = cursor.HasValue && cursor.Value.Type == e.Type;

                {
                    var rowPos = ImGui.GetCursorScreenPos();
                    float textH = ImGui.GetTextLineHeight();
                    var rowMin = new System.Numerics.Vector2(rowPos.X, rowPos.Y - 2f);
                    var rowMax = new System.Numerics.Vector2(rowPos.X + ImGui.GetContentRegionAvail().X, rowPos.Y + textH + 2f);
                    bool rowHovered = !on && !exists && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(rowMin, rowMax);
                    if (on)
                        ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(EditorTheme.AccentSoft), 5f);
                    else if (rowHovered)
                        ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(EditorTheme.ItemBg), 5f);
                }
                var clearHdr = new System.Numerics.Vector4(0, 0, 0, 0);
                ImGui.PushStyleColor(ImGuiCol.Header, clearHdr);
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, clearHdr);
                ImGui.PushStyleColor(ImGuiCol.HeaderActive, clearHdr);
                if (on) ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Accent);

                if (exists) ImGui.BeginDisabled();
                if (ImGui.Selectable("   " + e.Name, on))
                    AddFromPopup(entity, e.Type);
                if (exists) ImGui.EndDisabled();

                if (on) ImGui.PopStyleColor();
                ImGui.PopStyleColor(3);

                if (on && dir != 0) ImGui.SetScrollHereY();
                if (exists && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip("Already attached");
            }
        }
        if (rows == 0) ImGui.TextDisabled("   No matching components");
        ImGui.EndChild();

        ImGui.TextDisabled(cursor.HasValue
            ? $"\u2191\u2193 move  \u00b7  \u21b5 add {cursor.Value.Name}  \u00b7  esc close"
            : "\u2191\u2193 move  \u00b7  \u21b5 add  \u00b7  esc close");

        bool enterKey = enter || ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter);
        if (enterKey && cursor.HasValue) AddFromPopup(entity, cursor.Value.Type);
        else if (enter) ImGui.SetKeyboardFocusHere(-1);
        if (ImGui.IsKeyPressed(ImGuiKey.Escape)) ImGui.CloseCurrentPopup();
    }

    private static bool CanAdd(Entity entity, Type type)
        => typeof(Collider2D).IsAssignableFrom(type) || !HasComponentOfType(entity, type);

    private void AddFromPopup(Entity entity, Type type)
    {
        _state.ExecuteCommand(BuildAddCommand(entity, type));
        ImGui.CloseCurrentPopup();
    }

    internal static ICommand BuildAddCommandForTest(Entity entity, Type type) => BuildAddCommand(entity, type);

    private static ICommand BuildAddCommand(Entity entity, Type type)
    {
        var add = new AddComponentCommand(entity, type);
        if (type != typeof(Animator) || entity.GetComponent<SpriteRenderer>() != null)
            return add;

        return new CompositeCommand($"Add Animator (+SpriteRenderer) to '{entity.Name}'",
            new AddComponentCommand(entity, typeof(SpriteRenderer)),
            add);
    }

    private static readonly HashSet<Type> _collapsedTypes = new();

    private Action? _pendingComponentAction;

    private void FlushComponentAction()
    {
        var action = _pendingComponentAction;
        _pendingComponentAction = null;
        action?.Invoke();
    }

    private void DrawComponentMenu(Component component, bool canRemove, bool multiLocked)
    {
        var entity = component.Entity;
        multiLocked |= _state.Selection.Count > 1 && _state.IsSelected(entity);
        bool copyable = ComponentClipboard.CanCopy(component);
        bool dupeOk = component is Collider2D;

        ImGui.TextDisabled(component.GetType().Name);
        ImGui.Separator();

        if (ImGui.MenuItem("Copy", "", false, copyable))
            ComponentClipboard.Copy(component);
        if (!copyable && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("A component that is not saved into the scene - code attaches it (and it cannot be copied either)");

        bool canValues = !multiLocked && ComponentClipboard.CanPasteValues(component);
        if (ImGui.MenuItem("Paste values", "", false, canValues))
        {
            var data = ComponentClipboard.Data!;
            Defer(() =>
            {
                _state.ExecuteCommand(new WriteComponentValuesCommand(component, data, "Paste values"));
                _state.KeepSelectionThroughStop();
            });
        }
        if (!canValues && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(ComponentClipboard.HasValue
                ? $"The clipboard holds {ComponentClipboard.Label} - it only overwrites the same type"
                : "Copy a component first");

        string pasteLabel = ComponentClipboard.HasValue
            ? $"Paste as new: {ComponentClipboard.Label}"
            : "Paste as new";
        bool canNew = !multiLocked && CanPasteAsNew(entity);
        if (ImGui.MenuItem(pasteLabel, "", false, canNew))
            PasteAsNew(entity);

        ImGui.Separator();

        if (ImGui.MenuItem("Duplicate", "", false, copyable && dupeOk && !multiLocked))
        {
            var data = ComponentDataRegistry.CreateFor(component)!;
            data.CaptureInstance(component);
            Defer(() =>
            {
                _state.ExecuteCommand(new AddComponentDataCommand(entity, data, "Duplicate component"));
                _state.KeepSelectionThroughStop();
            });
        }
        if (!dupeOk && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Only one per entity - colliders are the exception");

        if (ImGui.MenuItem("Reset", "", false, copyable && !multiLocked))
        {
            var fresh = ComponentDataRegistry.CreateFor(component)!;
            Defer(() =>
            {
                _state.ExecuteCommand(new WriteComponentValuesCommand(component, fresh, "Reset"));
                _state.KeepSelectionThroughStop();
            });
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Remove", "", false, canRemove && !multiLocked))
            Defer(() =>
            {
                _state.ExecuteCommand(new RemoveComponentCommand(entity, component));
                _state.KeepSelectionThroughStop();
            });
        if (!canRemove && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Transform cannot be removed");
    }

    private void Defer(Action action) => _pendingComponentAction = action;

    private static bool CanPasteAsNew(Entity entity)
    {
        var t = ComponentClipboard.Type;
        if (t == null) return false;
        if (typeof(Collider2D).IsAssignableFrom(t)) return true;
        return !HasComponentOfType(entity, t);
    }

    private void PasteAsNew(Entity entity)
    {
        var data = ComponentClipboard.Data!;
        Defer(() =>
        {
            _state.ExecuteCommand(new AddComponentDataCommand(entity, data, $"Paste {ComponentClipboard.Label}"));
            _state.KeepSelectionThroughStop();
        });
    }

    private void DrawComponent(Component component, bool multiLocked = false)
    {
        var type = component.GetType();
        string typeName = type.Name;
        ImGui.PushID(component.GetHashCode());

        bool canRemove = component is not Transform;
        bool collapsed = _collapsedTypes.Contains(type);

        float rowH = ImGui.GetFrameHeight();
        var rowMin = ImGui.GetCursorScreenPos();
        float rowW = ImGui.GetContentRegionAvail().X;
        ImGui.GetWindowDrawList().AddRectFilled(rowMin,
            new System.Numerics.Vector2(rowMin.X + rowW, rowMin.Y + rowH),
            ImGui.GetColorU32(ImGuiCol.Header), 3f);

        var enabled = component.Enabled;
        ImGui.SetCursorScreenPos(new System.Numerics.Vector2(rowMin.X + 6, rowMin.Y));
        if (multiLocked) ImGui.BeginDisabled();
        if (EditorWidgets.TinyCheckbox("##enabled", ref enabled))
        {
            component.Enabled = enabled;
            TrackInstant(!enabled, enabled, v => component.Enabled = v, "Enabled");
        }
        if (multiLocked) ImGui.EndDisabled();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Enabled");
        ImGui.SameLine(0, 7);

        float rmZone = 22f;
        string caret = collapsed ? Icons.CaretRight : Icons.CaretDown;
        string icon = EntityKinds.Icon(component);
        ImGui.PushFont(ImGuiRenderer.SemiBoldFont);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new System.Numerics.Vector4(0, 0, 0, 0));
        bool hdrClicked = ImGui.Selectable($"{caret}  {icon}  {typeName}###hdr", false, ImGuiSelectableFlags.None,
            new System.Numerics.Vector2(rowW - (ImGui.GetCursorScreenPos().X - rowMin.X) - rmZone - 6, 0));
        ImGui.PopStyleColor();
        ImGui.PopFont();
        if (hdrClicked)
        {
            if (collapsed) _collapsedTypes.Remove(type);
            else _collapsedTypes.Add(type);
            collapsed = !collapsed;
        }

        string summary = ComponentSummary(component);
        if (summary.Length > 0)
        {
            if (summary.Length > 18) summary = summary[..17] + "…";
            ImGui.PushFont(ImGuiRenderer.MonoFont);
            var sts = ImGui.CalcTextSize(summary);
            ImGui.GetWindowDrawList().AddText(
                new System.Numerics.Vector2(
                    ImGui.GetItemRectMax().X - sts.X - 4,
                    ImGui.GetItemRectMin().Y + (ImGui.GetItemRectSize().Y - sts.Y) * 0.5f),
                ImGui.GetColorU32(ImGuiCol.TextDisabled), summary);
            ImGui.PopFont();
        }

        ImGui.SameLine(0, 4);
        if (EditorWidgets.KebabButton("##menu", 18f))
            ImGui.OpenPopup("compMenu");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Component menu");

        if (ImGui.BeginPopup("compMenu"))
        {
            DrawComponentMenu(component, canRemove, multiLocked);
            ImGui.EndPopup();
        }

        if (!collapsed)
        {
            ImGui.Spacing();
            ImGui.Indent(6f);

            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X * 0.58f);

            if (multiLocked)
            {
                EditorWidgets.Hint("Multi-editing is not supported - the primary's value (read only)");
                ImGui.BeginDisabled();
            }

            switch (component)
            {
                case Transform t:
                    DrawTransform(t);
                    break;
                case Animator anim:
                    DrawAnimator(anim);
                    break;
                case SpriteRenderer sr:
                    DrawSpriteRenderer(sr);
                    break;
                case Rigidbody2D rb:
                    DrawRigidbody2D(rb);
                    break;
                case Interactable it:
                    DrawInteractable(it);
                    break;
                case CircleCollider2D cirCol:
                    DrawCircleCollider(cirCol);
                    break;
                case CapsuleCollider2D capCol:
                    DrawCapsuleCollider(capCol);
                    break;
                case BoxCollider2D col:
                    DrawBoxCollider2D(col);
                    break;
                case TilemapRenderer tr:
                    DrawTilemapRenderer(tr);
                    break;
                case Light2D light:
                    DrawLight2D(light);
                    break;
                case SceneInstance si:
                    DrawSceneInstance(si);
                    break;
                case SoundEmitter se:
                    DrawSoundEmitter(se);
                    break;
                case SortingGroup sg:
                    DrawSortingGroup(sg);
                    break;
                case SurfaceArea sa:
                    DrawSurfaceArea(sa);
                    break;
                case CutsceneTrigger ct:
                    DrawCutsceneTrigger(ct);
                    break;
                case Talker tk:
                    DrawTalker(tk);
                    break;
                case ParticleEmitter pe:
                    DrawParticleEmitter(pe);
                    break;
                default:
                    ImGui.TextDisabled("(No dedicated inspector - only the fact that it is attached is saved)");
                    break;
            }

            if (multiLocked) ImGui.EndDisabled();

            ImGui.PopItemWidth();
            ImGui.Unindent(6f);
        }

        ImGui.PopID();
        ImGui.Dummy(new System.Numerics.Vector2(0, 3));
    }

    private void DrawSceneInstance(SceneInstance si)
    {
        string baseName = si.ScenePath != null
            ? System.IO.Path.GetFileNameWithoutExtension(si.ScenePath) : "(not linked)";
        EditorWidgets.FieldLabel("Base");
        ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Prefab);
        DrawAssetRefField("siBase", Icons.Cubes, baseName,
            si.ScenePath != null ? EditorApp.ToContentRelative(si.ScenePath) : null,
            tooltip: "This instance's original prefab - editing it affects every instance", showPicker: false);
        ImGui.PopStyleColor();

        RefreshOverrideCache(si);
        var ovs = _ovCache;
        ImGui.PushFont(ImGuiRenderer.MonoFont);
        ImGui.TextDisabled($"{si.InstancedEntities.Count} children · {ovs?.Count ?? 0} entities overridden");
        ImGui.PopFont();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Editing a child records only the changed fields into this room on save - the base and other rooms are unaffected.");

        if (ovs is { Count: > 0 })
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Revert all"))
                Defer(() =>
                {
                    _state.ExecuteCommand(new RevertInstanceOverridesCommand(si.Entity, null, _state));
                    _ovCacheTarget = null;
                });
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Discard every override - this instance becomes identical to the base (undoable)");

            var dim = ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled];
            foreach (var ov in ovs)
            {
                ImGui.PushID(ov.EntityId);
                bool revert = ImGui.SmallButton("Revert");
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Return this entity to the base values (undoable)");
                ImGui.SameLine();
                ImGui.Text(_ovNames.TryGetValue(ov.EntityId, out var n) ? n : $"#{ov.EntityId}");
                ImGui.SameLine();
                ImGui.PushFont(ImGuiRenderer.MonoFont);
                ImGui.TextColored(dim, OverrideSummary(ov));
                ImGui.PopFont();
                ImGui.PopID();
                if (revert)
                {
                    int targetId = ov.EntityId;
                    Defer(() =>
                    {
                        _state.ExecuteCommand(new RevertInstanceOverridesCommand(si.Entity, targetId, _state));
                        _ovCacheTarget = null;
                    });
                }
            }
        }

        ImGui.Spacing();

        if (ImGui.Button("Open base") && si.ScenePath != null)
            _state.OpenSceneRequest = si.ScenePath;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Edit the base scene - it affects every instance");
        ImGui.SameLine();
        if (ImGui.Button("Reload"))
            Defer(() =>
            {
                _state.ClearSelection();
                si.Load();
            });
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Recreate from the base - overrides are kept and only unsaved child edits are lost\n(for applying a base edited in another tab to this instance)");
        ImGui.SameLine();
        if (ImGui.Button("Unpack"))
            Defer(() => _state.ExecuteCommand(new UnpackSceneInstanceCommand(si.Entity, _state)));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Break the link and turn it into ordinary entities (for when even the structure has to differ)");
    }

    private SceneInstance? _ovCacheTarget;
    private double _ovCacheTime = -999;
    private List<InstanceOverrideData>? _ovCache;
    private readonly Dictionary<int, string> _ovNames = new();

    private void RefreshOverrideCache(SceneInstance si)
    {
        if (_ovCacheTarget == si && ImGui.GetTime() - _ovCacheTime < 1.0) return;
        _ovCacheTarget = si;
        _ovCacheTime = ImGui.GetTime();
        _ovCache = si.IsLoaded ? InstanceOverrides.Compute(si) : si.Overrides;

        _ovNames.Clear();
        foreach (var kv in si.SourceIds) _ovNames[kv.Value] = kv.Key.Name;
        if (_ovCache == null || si.ScenePath == null) return;
        foreach (var ov in _ovCache)
            if (!_ovNames.ContainsKey(ov.EntityId))
            {
                var baseData = SceneSerializer.LoadFromFile(si.ScenePath);
                if (baseData != null)
                    foreach (var ed in baseData.Entities) _ovNames.TryAdd(ed.Id, ed.Name);
                break;
            }
    }

    private static string OverrideSummary(InstanceOverrideData ov)
    {
        if (ov.Deleted == true) return "deleted";
        var parts = new List<string>();
        if (ov.Name != null) parts.Add("name");
        if (ov.Active != null) parts.Add(ov.Active == true ? "active" : "inactive");
        if (ov.Components != null)
            foreach (var node in ov.Components)
            {
                string type = node.TryGetPropertyValue("type", out var t) ? t?.GetValue<string>() ?? "?" : "?";
                var keys = new List<string>();
                foreach (var kv in node) if (kv.Key != "type") keys.Add(kv.Key);
                parts.Add(keys.Count > 0 ? $"{type}({string.Join(", ", keys)})" : type);
            }
        if (ov.Removed != null)
            foreach (var t in ov.Removed) parts.Add($"−{t}");
        return string.Join(" · ", parts);
    }

    private static string ComponentSummary(Component c) => c switch
    {
        Animator a => a.CurrentClip?.Name ?? "",
        SceneInstance si => System.IO.Path.GetFileNameWithoutExtension(si.ScenePath ?? ""),
        SpriteRenderer sr => sr.SliceName ?? System.IO.Path.GetFileName(sr.TexturePath ?? ""),
        Light2D l => l.Shape switch
        {
            LightShape.Cone => $"cone {l.Length:0}px·{l.SpreadAngle:0}°",
            LightShape.Rect => $"rect {l.RectSize.X:0}x{l.RectSize.Y:0}",
            LightShape.Texture => System.IO.Path.GetFileNameWithoutExtension(l.TexturePath ?? "") is { Length: > 0 } n
                ? n : "(no art)",
            _ => $"r {l.Radius:0}",
        },
        BoxCollider2D b => $"{b.Size.X:0}×{b.Size.Y:0}",
        TilemapRenderer t => $"{t.TileSize}px",
        SortingGroup g => $"{CountGroupMembers(g.Entity)} members",
        _ => ""
    };

    #region Component Editors

    private void DrawTransform(Transform t)
    {
        bool multi = t.Entity != null && _state.Selection.Count > 1 && _state.IsSelected(t.Entity);
        bool mixedX = false, mixedY = false;
        if (multi)
            foreach (var e in _state.Selection)
            {
                var et = e.GetComponent<Transform>();
                if (et == null) continue;
                if (et.Position.X != t.Position.X) mixedX = true;
                if (et.Position.Y != t.Position.Y) mixedY = true;
            }

        float px = t.Position.X, py = t.Position.Y;
        EditorWidgets.FieldLabel("Position");
        {
            float innerX = ImGui.GetStyle().ItemInnerSpacing.X;
            float w = ImGui.CalcItemWidth();
            float sub = MathF.Floor((w - innerX) / 2f);

            var mixFlags = ImGuiSliderFlags.NoInput;

            ImGui.SetNextItemWidth(sub);
            bool changedX = mixedX
                ? ImGui.DragFloat("##PosX", ref px, 1f, 0f, 0f, "—", mixFlags)
                : ImGui.DragFloat("##PosX", ref px, 1f, 0f, 0f, "%.3f");
            EditorWidgets.AxisTintOne(EditorTheme.AxisX);
            TrackGroupMove();

            ImGui.SameLine(0f, innerX);
            ImGui.SetNextItemWidth(sub);
            bool changedY = mixedY
                ? ImGui.DragFloat("##PosY", ref py, 1f, 0f, 0f, "—", mixFlags)
                : ImGui.DragFloat("##PosY", ref py, 1f, 0f, 0f, "%.3f");
            EditorWidgets.AxisTintOne(EditorTheme.AxisY);
            TrackGroupMove();

            if (changedX || changedY)
            {
                var delta = new Vector2(px - t.Position.X, py - t.Position.Y);
                if (delta != Vector2.Zero) MoveSelectionBy(t, delta);
            }
        }

        var rotation = t.Rotation;
        EditorWidgets.FieldLabel("Rotation");
        if (multi) ImGui.BeginDisabled();
        if (ImGui.DragFloat("##Rotation", ref rotation, 0.01f))
            t.Rotation = rotation;
        if (multi) ImGui.EndDisabled();
        else TrackDrag(t.Rotation, v => t.Rotation = v, "Rotation");
    }

    private void DrawSpriteRenderer(SpriteRenderer sr)
    {
        DrawSpriteField(sr);

        if (!string.IsNullOrEmpty(sr.AtlasPath))
        {
            var atlas = LoadAtlas(sr.AtlasPath);
            if (atlas != null && atlas.Slices.Count > 0)
            {
                var sliceNames = atlas.Slices.Select(s => s.Name).ToArray();
                int currentSliceIndex = string.IsNullOrEmpty(sr.SliceName) ? 0 : Array.IndexOf(sliceNames, sr.SliceName);
                if (currentSliceIndex < 0) currentSliceIndex = 0;

                EditorWidgets.FieldLabel("Slice");
                if (ImGui.Combo("##Slice", ref currentSliceIndex, sliceNames, sliceNames.Length))
                {
                    var oldSlice = sr.SliceName;
                    var newSlice = sliceNames[currentSliceIndex];
                    sr.SetSlice(newSlice);
                    TrackInstant(oldSlice, newSlice, v => { if (v != null) sr.SetSlice(v); }, "Slice");
                }
            }
        }

        bool overrideDraw = sr.DrawSize.HasValue;
        EditorWidgets.FieldLabel("Size Override");
        if (EditorWidgets.TinyCheckbox("##Size Override", ref overrideDraw))
        {
            var old = sr.DrawSize;
            Vector2? next = overrideDraw ? sr.GetNativeSize() : null;
            if (next == Vector2.Zero) next = new Vector2(16, 16);
            sr.DrawSize = next;
            TrackInstant(old, next, v => sr.DrawSize = v, "Draw Size Override");
        }

        if (sr.DrawSize.HasValue)
        {
            var draw = new System.Numerics.Vector2(sr.DrawSize.Value.X, sr.DrawSize.Value.Y);
            EditorWidgets.FieldLabel("Draw Size");
            if (ImGui.DragFloat2("##Draw Size", ref draw))
                sr.DrawSize = new Vector2(MathF.Max(1f, draw.X), MathF.Max(1f, draw.Y));
            EditorWidgets.AxisTint();
            TrackDrag(sr.DrawSize, v => sr.DrawSize = v, "Draw Size");
        }
        else
        {
            var native = sr.GetNativeSize();
            ImGui.SameLine();
            ImGui.TextDisabled($"(native: {native.X:0}×{native.Y:0})");
        }

        ImGui.Spacing();
        DrawRenderCommon(sr);
    }

    private void DrawRenderCommon(SpriteRenderer sr)
    {
        var color = new System.Numerics.Vector4(
            sr.Color.R / 255f,
            sr.Color.G / 255f,
            sr.Color.B / 255f,
            sr.Color.A / 255f
        );
        EditorWidgets.FieldLabel("Color");
        if (ImGui.ColorEdit4("##Color", ref color, ColorFieldFlags))
            sr.Color = new Color(color.X, color.Y, color.Z, color.W);
        TrackDrag(sr.Color, v => sr.Color = v, "Color");

        var flipX = sr.FlipX;
        var flipY = sr.FlipY;
        EditorWidgets.FieldLabel("Flip");
        if (EditorWidgets.TinyCheckbox("##Flip X", ref flipX))
        {
            sr.FlipX = flipX;
            TrackInstant(!flipX, flipX, v => sr.FlipX = v, "Flip X");
        }
        ImGui.SameLine(0, 6);
        ImGui.TextDisabled("X");

        ImGui.SameLine(0, 14);
        if (EditorWidgets.TinyCheckbox("##Flip Y", ref flipY))
        {
            sr.FlipY = flipY;
            TrackInstant(!flipY, flipY, v => sr.FlipY = v, "Flip Y");
        }
        ImGui.SameLine(0, 6);
        ImGui.TextDisabled("Y");

        var emissive = sr.EmissiveIntensity;
        EditorWidgets.FieldLabel("Emissive");
        if (ImGui.DragFloat("##Emissive", ref emissive, 0.02f, 0f, 4f, "%.2f"))
            sr.EmissiveIntensity = emissive;
        TrackDrag(sr.EmissiveIntensity, v => sr.EmissiveIntensity = v, "Emissive");
        if (sr.EmissiveIntensity > 0f && _state.CurrentScene is { LightingEnabled: false })
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(Lighting is off - no effect)");
        }

        var castShadow = sr.CastShadow;
        EditorWidgets.FieldLabel("Cast Shadow");
        if (EditorWidgets.TinyCheckbox("##Cast Shadow", ref castShadow))
        {
            sr.CastShadow = castShadow;
            TrackInstant(!castShadow, castShadow, v => sr.CastShadow = v, "Cast Shadow");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Ground shadow - an ellipse fixed at the feet (always there, regardless of time of day or interior/exterior)\n" +
                "It marks \"this object sits on the floor\". A shadow cast by a tree is a negative light's job");

        if (sr.CastShadow)
        {
            if (Runtime.Rendering.ShadowRenderer.ShadowBandOf(sr.RenderLayer)
                == Runtime.Rendering.ShadowBand.None)
            {
                ImGui.TextColored(EditorTheme.Accent,
                    $"{Icons.Warning}  The Floor layer cannot cast shadows (there is no floor to lay them on)");
                ImGui.Spacing();
            }

            ShadowEditToggle(sr);

            int shape = ShadowShapeIndex(sr);
            EditorWidgets.FieldLabel("Shape");
            if (ImGui.Combo("##Shadow Shape", ref shape, ShadowShapeNames, ShadowShapeNames.Length)
                && shape != ShadowShapeIndex(sr))
            {
                var before = (sr.ShadowRadius, sr.ShadowTexturePath);
                ApplyShadowShape(sr, shape);
                var after = (sr.ShadowRadius, sr.ShadowTexturePath);
                TrackInstant(before, after,
                    v => { sr.ShadowRadius = v.Item1; sr.SetShadowTexture(v.Item2); }, "Shadow Shape");
            }

            if (shape == ShadowShapeTexture)
            {
                DrawShadowTextureField(sr);
            }
            else
            {
                bool sizeOverride = sr.ShadowWidth.HasValue || sr.ShadowHeight.HasValue;
                EditorWidgets.FieldLabel("Shadow Size");
                if (EditorWidgets.TinyCheckbox("##Shadow Size Override", ref sizeOverride))
                {
                    var oldSize = (sr.ShadowWidth, sr.ShadowHeight);
                    var auto = Runtime.Rendering.ShadowRenderer.AutoShadowSize(sr, (int)sr.GetDrawSize().X);
                    var next = sizeOverride
                        ? ((int?)Math.Max(1, auto.X), (int?)Math.Max(1, auto.Y))
                        : ((int?)null, (int?)null);
                    sr.ShadowWidth = next.Item1; sr.ShadowHeight = next.Item2;
                    TrackInstant(oldSize, next,
                        v => { sr.ShadowWidth = v.Item1; sr.ShadowHeight = v.Item2; }, "Shadow Size Override");
                }

                if (sizeOverride)
                {
                    var wh = new int[] { sr.ShadowWidth ?? 1, sr.ShadowHeight ?? 1 };
                    EditorWidgets.FieldLabel("Shadow W/H");
                    if (ImGui.DragInt2("##Shadow WH", ref wh[0], 1f, 1, 4096))
                    {
                        sr.ShadowWidth = Math.Max(1, wh[0]);
                        sr.ShadowHeight = Math.Max(1, wh[1]);
                    }
                    EditorWidgets.AxisTint();
                    TrackDrag((sr.ShadowWidth, sr.ShadowHeight),
                        v => { sr.ShadowWidth = v.Item1; sr.ShadowHeight = v.Item2; }, "Shadow Size");
                }
                else
                {
                    var shadowScale = sr.ShadowScale;
                    EditorWidgets.FieldLabel("Shadow Scale");
                    if (ImGui.DragFloat("##Shadow Scale", ref shadowScale, 0.02f, 0.05f, 3f, "%.2f"))
                        sr.ShadowScale = shadowScale;
                    TrackDrag(sr.ShadowScale, v => sr.ShadowScale = v, "Shadow Scale");
                }

                if (shape != ShadowShapeEllipse)
                {
                    var radius = sr.ShadowRadius;
                    EditorWidgets.FieldLabel("Corner radius");
                    if (EditorWidgets.SliderFloat("##Shadow Radius", ref radius, 0f, 1f))
                        sr.ShadowRadius = radius;
                    TrackDrag(sr.ShadowRadius, v => sr.ShadowRadius = v, "Shadow Radius");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("0 = square-cornered rectangle · 1 = ellipse (a ratio applied to half the short side)");
                }
            }

            var off = new int[] { sr.ShadowOffset.X, sr.ShadowOffset.Y };
            EditorWidgets.FieldLabel("Shadow Offset");
            if (ImGui.DragInt2("##Shadow Offset", ref off[0], 1f, -512, 512))
                sr.ShadowOffset = new Point(off[0], off[1]);
            EditorWidgets.AxisTint();
            TrackDrag(sr.ShadowOffset, v => sr.ShadowOffset = v, "Shadow Offset");
        }

        DrawSortingFields(() => sr.RenderLayer, v => sr.RenderLayer = v,
                          () => sr.SortOffset, v => sr.SortOffset = v,
                          SortingGroup.Find(sr.Entity));
    }

    private void DrawTalker(Talker tk)
    {
        bool auto = tk.BubbleAnchor == Vector2.Zero;
        var world = Talker.AnchorFor(tk.Entity);
        var pos = tk.Entity.GetComponent<Transform>()?.Position ?? Vector2.Zero;

        var anchor = new System.Numerics.Vector2(tk.BubbleAnchor.X, tk.BubbleAnchor.Y);
        EditorWidgets.FieldLabel("Bubble Anchor");
        if (ImGui.DragFloat2("##BubbleAnchor", ref anchor, 0.5f, -512f, 512f, "%.0f"))
            tk.BubbleAnchor = new Vector2(MathF.Round(anchor.X), MathF.Round(anchor.Y));
        EditorWidgets.AxisTint();
        TrackDrag(tk.BubbleAnchor, v => tk.BubbleAnchor = v, "Bubble Anchor");

        if (auto)
        {
            ImGui.TextDisabled($"(0,0) = automatic - the feet plus height x {Talker.AutoAnchorHeightRate:0.##}");
            ImGui.TextDisabled($"Currently: {world.X - pos.X:+0;-0;0}, {world.Y - pos.Y:+0;-0;0}");
            if (ImGui.SmallButton("Bake the automatic value in"))
            {
                var old = tk.BubbleAnchor;
                var picked = world - pos;
                tk.BubbleAnchor = picked;
                TrackInstant(old, picked, v => tk.BubbleAnchor = v, "Bubble Anchor");
            }
        }
        else if (ImGui.SmallButton("Back to automatic"))
        {
            var old = tk.BubbleAnchor;
            tk.BubbleAnchor = Vector2.Zero;
            TrackInstant(old, Vector2.Zero, v => tk.BubbleAnchor = v, "Bubble Anchor");
        }
    }

    private void DrawSortingGroup(SortingGroup sg)
    {
        int members = CountGroupMembers(sg.Entity);
        ImGui.TextDisabled(members > 0
            ? $"Sorts a subtree of {members} as one mass"
            : "No members - put entities under it as children");

        DrawSortingFields(() => sg.RenderLayer, v => sg.RenderLayer = v,
                          () => sg.SortOffset, v => sg.SortOffset = v,
                          SortingGroup.Find(sg.Entity.Parent));
    }

    private static int CountGroupMembers(Entity e, bool isRoot = true)
    {
        if (!isRoot && e.GetComponent<SortingGroup>() is { Enabled: true }) return 1;
        int n = 0;
        foreach (var c in e.Components)
        {
            if (!c.Enabled) continue;
            if (c is IRenderable) n++;
            else if (c is IRenderableSource source) n += source.Renderables.Count;
        }
        foreach (var child in e.Children)
            n += CountGroupMembers(child, false);
        return n;
    }

    private static readonly string[] LayerBandNames =
        { "Floor", "Below Entities", "Entities (Y-Sort)", "Above Entities", "Overlay" };
    private static readonly int[] LayerBandValues =
        { RenderLayers.Floor, RenderLayers.BelowEntities, RenderLayers.Entities, RenderLayers.AboveEntities, RenderLayers.Overlay };

    private void DrawSortingFields(Func<int> getLayer, Action<int> setLayer,
                                   Func<float> getOffset, Action<float> setOffset,
                                   SortingGroup? inGroup = null)
    {
        ImGui.Spacing();
        ImGui.TextDisabled(inGroup != null
            ? $"Sorting - inside the group '{inGroup.Entity.Name}'"
            : "Sorting");

        int layer = getLayer();
        int bandIdx = Array.IndexOf(LayerBandValues, layer);
        bool isCustom = bandIdx < 0;
        var options = isCustom
            ? LayerBandNames.Append($"Custom ({layer})").ToArray()
            : LayerBandNames;
        int selIdx = isCustom ? options.Length - 1 : bandIdx;

        EditorWidgets.FieldLabel("Layer");
        if (inGroup != null) ImGui.BeginDisabled();
        if (ImGui.Combo("##Layer", ref selIdx, options, options.Length) && selIdx < LayerBandValues.Length)
        {
            int newLayer = LayerBandValues[selIdx];
            setLayer(newLayer);
            TrackInstant(layer, newLayer, setLayer, "Layer");
        }
        if (inGroup != null) ImGui.EndDisabled();
        if (inGroup != null && ImGui.IsItemHovered())
            ImGui.SetTooltip("Inside a group the layer is ignored - the group's Layer represents them all");

        bool fixedOrder = inGroup != null || getLayer() != RenderLayers.Entities;
        var offset = getOffset();
        EditorWidgets.FieldLabel(fixedOrder ? "Order" : "Sort Offset");
        if (ImGui.DragFloat(fixedOrder ? "##Order" : "##Sort Offset", ref offset, 0.5f))
            setOffset(offset);
        TrackDrag(getOffset(), setOffset, "Sort Offset");
        ImGui.SameLine();
        ImGui.TextDisabled(inGroup != null ? "(order within the group)"
                         : fixedOrder      ? "(order within the layer)"
                                           : "(an adjustment to the foot comparison - usually 0)");
    }

    private void DrawRigidbody2D(Rigidbody2D rb)
    {
        var vel = new System.Numerics.Vector2(rb.Velocity.X, rb.Velocity.Y);
        EditorWidgets.FieldLabel("Velocity");
        if (ImGui.DragFloat2("##Velocity", ref vel))
            rb.Velocity = new Vector2(vel.X, vel.Y);
        EditorWidgets.AxisTint();
        TrackDrag(rb.Velocity, v => rb.Velocity = v, "Velocity");

        var isKinematic = rb.IsKinematic;
        EditorWidgets.FieldLabel("Is Kinematic");
        if (EditorWidgets.TinyCheckbox("##Is Kinematic", ref isKinematic))
        {
            rb.IsKinematic = isKinematic;
            TrackInstant(!isKinematic, isKinematic, v => rb.IsKinematic = v, "Is Kinematic");
        }
    }

    private static readonly string[] InteractKindNames = { "Read", "Door", "Event" };

    private static readonly string[] AudioBusNames = { "Music", "SFX", "Ambient", "Voice", "UI" };

    private string _particlePickerQuery = "";

    private void DrawParticleEmitter(ParticleEmitter pe)
    {
        EditorWidgets.FieldLabel("Preset");
        DrawParticlePresetPicker("pePreset", pe.PresetId ?? "", -1f, v =>
        {
            var old = pe.PresetId ?? "";
            if (old == v) return;
            pe.PresetId = v;
            TrackInstant(old, v, x => pe.PresetId = x, "Particle Preset");
        });

        bool emitting = pe.Emitting;
        EditorWidgets.FieldLabel("Emitting");
        if (ImGui.Checkbox("##PeEmitting", ref emitting) && emitting != pe.Emitting)
        {
            var old = pe.Emitting;
            pe.Emitting = emitting;
            TrackInstant(old, emitting, v => pe.Emitting = v, "Particle Emitting");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Switching it off stops new particles; the ones already up live out their lifetime\n" +
                             "- snow vanishing from the sky all at once looks like a fault rather than a switch.");

        var rate = pe.RateScale;
        EditorWidgets.FieldLabel("Rate Scale");
        if (ImGui.DragFloat("##PeRate", ref rate, 0.01f, 0f, 4f, "%.2f×"))
            pe.RateScale = rate;
        TrackDrag(pe.RateScale, v => pe.RateScale = v, "Particle Rate Scale");

        var preset = pe.ResolvePreset();
        ImGui.Spacing();
        if (preset == null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Danger);
            ImGui.TextWrapped(string.IsNullOrEmpty(pe.PresetId)
                ? "There is no preset - nothing is emitted."
                : $"Preset {pe.PresetId} was not found - the .particle was deleted or has not been scanned.");
            ImGui.PopStyleColor();
            return;
        }

        ImGui.TextDisabled($"{Icons.Circle}  Preset values (read only - editing the .particle applies at once)");
        ImGui.BeginDisabled();
        ImGui.BulletText($"Emission  rate {preset.Rate:0.##}/s" +
                         (preset.BurstMax > 0 ? $" · burst {preset.BurstMin}~{preset.BurstMax}" : "") +
                         $" · max {preset.MaxParticles}");
        ImGui.BulletText($"Shape  {ParticleShapeLabel(preset.Shape)}");
        ImGui.BulletText($"Speed  {ValueLabel(preset.Speed)} · {preset.Direction:0}° ±{preset.Spread / 2f:0}°");
        ImGui.BulletText($"Lifetime  {ValueLabel(preset.Lifetime)}s");
        ImGui.BulletText($"Scale  {ValueLabel(preset.Scale)}   Alpha  {ValueLabel(preset.Alpha)}");
        if (preset.GravityX != 0f || preset.GravityY != 0f)
            ImGui.BulletText($"Gravity  ({preset.GravityX:0.##}, {preset.GravityY:0.##})");
        if (preset.SwayAmplitude != 0f)
            ImGui.BulletText($"Sway  ±{preset.SwayAmplitude:0.##}px @ {preset.SwayFrequency:0.##}Hz");
        ImGui.BulletText($"Layer  {preset.RenderLayer}" +
                         (string.IsNullOrEmpty(preset.TextureId) ? "   Art  (none = a 1px dot)" : ""));
        ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.TextDisabled(pe.AliveCount > 0 ? $"● {pe.AliveCount} alive" : "○ 0 (nothing runs while editing)");
    }

    private static string ParticleShapeLabel(EmitterShape s) => s.Kind switch
    {
        EmitterShapeKind.Box => $"box {s.BoxWidth:0.#}x{s.BoxHeight:0.#}",
        EmitterShapeKind.Circle => $"circle r{s.Radius:0.#}",
        _ => "point",
    };

    private static string ValueLabel(ParticleValue v)
    {
        bool ranged = v.Min != v.Max;
        var c = v.Curve;
        bool curved = c is { Length: > 0 };

        if (!curved) return ranged ? $"{v.Min:0.##}~{v.Max:0.##}" : $"{v.Min:0.##}";

        var curve = string.Join("→", System.Array.ConvertAll(c!, f => f.ToString("0.##")));
        return ranged ? $"{v.Min:0.##}~{v.Max:0.##} × [{curve}]" : $"[{curve}]";
    }

    private void DrawParticlePresetPicker(string id, string currentId, float width, Action<string> onPick)
    {
        bool empty = string.IsNullOrWhiteSpace(currentId);
        bool legacy = !empty && !AssetRegistry.LooksLikeId(currentId);
        var asset = legacy ? null : ParticlePresetCache.Get(currentId);
        bool lost = !empty && !legacy && asset == null;

        string label = empty ? "(no preset)"
                     : legacy ? $"A path is stored: {currentId}"
                     : lost ? $"Unknown preset {currentId}"
                     : asset!.Name;

        bool bad = legacy || lost;
        string tip = legacy ? $"A reference is always a registry id: '{currentId}'\nPicking it again saves it as an id."
                   : lost ? $"An id the registry does not know: {currentId}\nThe .particle was deleted or has not been scanned."
                   : "The values are edited in Content/Particles/*.particle (saving applies at once)" +
                     (empty ? "" : $"\n(id {currentId})");
        if (bad) ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Danger);
        bool openPicker = DrawAssetRefField(id, Icons.Brush, label, bad ? null : Shell.AssetPing.FromId(currentId),
            tooltip: tip, onClear: () => onPick(""), width: width);
        if (bad) ImGui.PopStyleColor();
        if (openPicker)
        {
            _particlePickerQuery = "";
            ImGui.OpenPopup($"###{id}_picker");
        }

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(300, 280), ImGuiCond.Appearing);
        if (ImGui.BeginPopup($"###{id}_picker"))
        {
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##particleQuery", Icons.Search + "  Search", ref _particlePickerQuery, 128);
            ImGui.Separator();
            ImGui.BeginChild("##particleList", new System.Numerics.Vector2(0, 0));

            var all = ParticlePresetCache.All;
            if (all.Count == 0)
                ImGui.TextDisabled("(There are no presets - create a .particle in Content/Particles and restart)");

            foreach (var a in all)
            {
                if (_particlePickerQuery.Length > 0 &&
                    !a.Name.Contains(_particlePickerQuery, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (ImGui.Selectable($"{a.Name}##particle_{a.Id}", a.Id == currentId))
                {
                    onPick(a.Id);
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.EndChild();
            ImGui.EndPopup();
        }
    }

    private void DrawSoundEmitter(SoundEmitter se)
    {
        EditorWidgets.FieldLabel("Sound");
        DrawAudioField("seSound", se.SoundId, 180, v =>
        {
            var old = se.SoundId;
            if (old == v) return;
            se.SoundId = v;
            TrackInstant(old, v, x => se.SoundId = x, "Emitter Sound");
        }, warnStereo: true);

        int bus = (int)se.Bus;
        ImGui.SetNextItemWidth(120);
        EditorWidgets.FieldLabel("Bus");
        if (ImGui.Combo("##SeBus", ref bus, AudioBusNames, AudioBusNames.Length)
            && bus != (int)se.Bus)
        {
            var old = se.Bus;
            se.Bus = (AudioBus)bus;
            TrackInstant(old, se.Bus, v => se.Bus = v, "Emitter Bus");
        }

        var radius = se.Radius;
        EditorWidgets.FieldLabel("Radius");
        if (ImGui.DragFloat("##SeRadius", ref radius, 1f, 4f, 2048f))
            se.Radius = radius;
        TrackDrag(se.Radius, v => se.Radius = v, "Emitter Radius");

        var volume = se.Volume;
        EditorWidgets.FieldLabel("Volume");
        if (ImGui.DragFloat("##SeVolume", ref volume, 0.01f, 0f, 1f))
            se.Volume = volume;
        TrackDrag(se.Volume, v => se.Volume = v, "Emitter Volume");

        var pan = se.PanStrength;
        EditorWidgets.FieldLabel("Pan");
        if (ImGui.DragFloat("##SePan", ref pan, 0.01f, 0f, 1f))
            se.PanStrength = pan;
        TrackDrag(se.PanStrength, v => se.PanStrength = v, "Emitter Pan");

        var fadeIn = se.FadeIn;
        EditorWidgets.FieldLabel("Fade In");
        if (ImGui.DragFloat("##SeFadeIn", ref fadeIn, 0.05f, 0f, 5f, "%.2fs"))
            se.FadeIn = fadeIn;
        TrackDrag(se.FadeIn, v => se.FadeIn = v, "Emitter Fade In");

        var fadeOut = se.FadeOut;
        EditorWidgets.FieldLabel("Fade Out");
        if (ImGui.DragFloat("##SeFadeOut", ref fadeOut, 0.05f, 0f, 5f, "%.2fs"))
            se.FadeOut = fadeOut;
        TrackDrag(se.FadeOut, v => se.FadeOut = v, "Emitter Fade Out");

        ImGui.Spacing();
        if (se.IsPlaying) ImGui.TextDisabled("● playing");
        else ImGui.TextDisabled("○ out of range (stopped)");
    }

    private void DrawSurfaceArea(SurfaceArea sa)
    {
        var surfaces = new List<SurfaceAsset>(SurfaceLibrary.All);
        surfaces.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));

        var names = new string[surfaces.Count + 1];
        names[0] = "(none)";
        int current = 0;
        for (int i = 0; i < surfaces.Count; i++)
        {
            names[i + 1] = surfaces[i].Name;
            if (surfaces[i].Id == sa.SurfaceId) current = i + 1;
        }

        ImGui.SetNextItemWidth(160);
        EditorWidgets.FieldLabel("Surface");
        if (ImGui.Combo("##SurfaceAreaId", ref current, names, names.Length))
        {
            var old = sa.SurfaceId;
            var picked = current == 0 ? "" : surfaces[current - 1].Id;
            if (old != picked)
            {
                sa.SurfaceId = picked;
                TrackInstant(old, picked, v => sa.SurfaceId = v, "Surface");
            }
        }

        int priority = sa.Priority;
        ImGui.SetNextItemWidth(80);
        EditorWidgets.FieldLabel("Priority");
        if (ImGui.DragInt("##SurfaceAreaPriority", ref priority, 1f, -100, 100))
            sa.Priority = priority;
        TrackDrag(sa.Priority, v => sa.Priority = v, "Surface Priority");

        ImGui.Spacing();
        var shapes = new List<Collider2D>(sa.Entity.GetComponents<Collider2D>());
        if (string.IsNullOrEmpty(sa.SurfaceId))
        {
            ImGui.TextDisabled("No surface chosen - this area is ignored (the room's default floor sounds)");
        }
        else if (shapes.Count == 0)
        {
            ImGui.TextDisabled("⚠ There is no collider - with no shape, nobody can step on this area");
        }
        else
        {
            int solid = shapes.FindAll(c => !c.IsTrigger).Count;
            if (solid > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Danger);
                ImGui.TextWrapped($"⚠ {solid} colliders are not triggers - this becomes a wall rather than a floor. Switch Is Trigger on");
                ImGui.PopStyleColor();
            }
        }
    }

    private void DrawCutsceneTrigger(CutsceneTrigger ct)
    {
        var id = ct.CutsceneId;
        ImGui.SetNextItemWidth(-1);
        EditorWidgets.FieldLabel("Cutscene");
        if (ImGui.InputText("##ctId", ref id, 96))
            ct.CutsceneId = id;
        TrackDrag(ct.CutsceneId, v => ct.CutsceneId = v, "Cutscene Id");

        var registered = Runtime.Cutscenes.CutsceneDirector.Ids;
        if (string.IsNullOrEmpty(ct.CutsceneId))
        {
            ImGui.TextDisabled("No cutscene id - stepping on it does nothing");
        }
        else if (registered.Count > 0 &&
                 !Runtime.Cutscenes.CutsceneDirector.IsRegistered(ct.CutsceneId))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Danger);
            ImGui.TextWrapped("⚠ An unregistered cutscene - check CutsceneDirector.Register");
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.TextDisabled("A name registered with CutsceneDirector.Register (for example village.stone)");
        }

        bool once = ct.Once;
        EditorWidgets.FieldLabel("Once");
        if (EditorWidgets.TinyCheckbox("##ctOnce", ref once))
        {
            ct.Once = once;
            TrackInstant(!once, once, v => ct.Once = v, "Cutscene Once");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Plays once. The record is not on the component but in the blackboard (cutscene.done.*),\nso it travels with the save - the \"saved and saw it again\" bug is structurally impossible");

        ImGui.Spacing();
        var shapes = new List<Collider2D>(ct.Entity.GetComponents<Collider2D>());
        if (shapes.Count == 0)
        {
            ImGui.TextDisabled("⚠ There is no collider - with no shape, nobody can step on this area");
        }
        else
        {
            int solid = shapes.FindAll(c => !c.IsTrigger).Count;
            if (solid > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Danger);
                ImGui.TextWrapped($"⚠ {solid} colliders are not triggers - they cannot be stepped on and become a wall. Switch Is Trigger on");
                ImGui.PopStyleColor();
            }
        }
    }

    private void DrawInteractable(Interactable it)
    {
        var prompt = it.Prompt;
        ImGui.SetNextItemWidth(150);
        EditorWidgets.FieldLabel("Prompt");
        if (ImGui.InputText("##Prompt", ref prompt, 32))
            it.Prompt = prompt;
        TrackDrag(it.Prompt, v => it.Prompt = v, "Prompt");

        ImGui.SameLine(0, 8);
        if (string.IsNullOrEmpty(it.Prompt))
            ImGui.TextDisabled("(empty)");
        else if (Runtime.Text.Loc.Has(it.Prompt))
            ImGui.TextDisabled($"→ {Runtime.Text.Loc.T(it.Prompt)}");
        else
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.72f, 0.30f, 1f),
                $"{Icons.Warning} An unknown key - the screen will show ⟨{it.Prompt}⟩");

        int kind = (int)it.Kind;
        ImGui.SetNextItemWidth(120);
        EditorWidgets.FieldLabel("Kind");
        if (ImGui.Combo("##Kind", ref kind, InteractKindNames, InteractKindNames.Length))
        {
            var old = it.Kind;
            it.Kind = (InteractKind)kind;
            TrackInstant(old, it.Kind, v => it.Kind = v, "Interact Kind");
        }

        if (it.Kind == InteractKind.Read)
        {
            var block = it.Block;
            ImGui.SetNextItemWidth(-1);
            EditorWidgets.FieldLabel("Block");
            if (ImGui.InputText("##itBlock", ref block, 96))
                it.Block = block;
            TrackDrag(it.Block, v => it.Block = v, "Interact Block");

            bool known = string.IsNullOrEmpty(it.Block)
                || PixelCore.Runtime.Story.StoryLibrary.Current.TryGetBlock(it.Block, out _);
            if (!known)
                ImGui.TextColored(new System.Numerics.Vector4(1f, 0.45f, 0.35f, 1f),
                    "⚠ Not present in Content/Story");
            else
                ImGui.TextDisabled("An @block in Content/Story/*.story (for example village.oldMan)");

            if (string.IsNullOrEmpty(it.Block))
            {
                var text = it.Text;
                if (ImGui.InputTextMultiline("##itText", ref text, 512, new System.Numerics.Vector2(-1, 54)))
                    it.Text = text;
                TrackDrag(it.Text, v => it.Text = v, "Interact Text");
                ImGui.TextDisabled("Raw text used only when the block is empty (a fallback for old data)");
            }
        }
        else if (it.Kind == InteractKind.Event)
        {
            var ev = it.Event;
            ImGui.SetNextItemWidth(180);
            EditorWidgets.FieldLabel("Event");
            if (ImGui.InputText("##Event", ref ev, 64))
                it.Event = ev;
            TrackDrag(it.Event, v => it.Event = v, "Interact Event");

            bool anyTable = Runtime.Cutscenes.CutsceneDirector.Ids.Count > 0
                         || Runtime.Systems.GameActions.Ids.Count > 0;
            bool known = Runtime.Cutscenes.CutsceneDirector.IsRegistered(it.Event)
                      || Runtime.Systems.GameActions.IsRegistered(it.Event);

            if (string.IsNullOrEmpty(it.Event))
                ImGui.TextDisabled("No event name - examining it does nothing");
            else if (anyTable && !known)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Danger);
                ImGui.TextWrapped("⚠ Neither a registered cutscene nor a game event - a typo, or not registered yet");
                ImGui.PopStyleColor();
            }
            else
                ImGui.TextDisabled("Game code acts on this name (for example openGate)");
        }
        else if (it.Kind == InteractKind.Door)
        {
            EditorWidgets.FieldLabel("Target Room");
            DrawRoomPicker("itTarget", it.TargetSceneId, -1f, v =>
            {
                var old = it.TargetSceneId;
                if (old == v) return;
                it.TargetSceneId = v;
                TrackInstant(old, v, x => it.TargetSceneId = x, "Door Target");
            });

            var spawn = it.Spawn;
            EditorWidgets.FieldLabel("Spawn");
            float caretW = ImGui.GetFrameHeight();
            ImGui.SetNextItemWidth(MathF.Max(60f,
                ImGui.GetContentRegionAvail().X - caretW - ImGui.GetStyle().ItemSpacing.X));
            if (ImGui.InputText("##Spawn", ref spawn, 64))
                it.Spawn = spawn;
            TrackDrag(it.Spawn, v => it.Spawn = v, "Door Spawn");

            ImGui.SameLine(0, ImGui.GetStyle().ItemSpacing.X);
            var spawns = Shell.DoorRefs.SpawnNames(it.TargetSceneId);
            ImGui.BeginDisabled(spawns == null);
            if (ImGui.Button($"{Icons.CaretDown}##spawnPick", new System.Numerics.Vector2(caretW, 0)))
                ImGui.OpenPopup("###spawnPicker");
            ImGui.EndDisabled();
            if (spawns == null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Pick the destination room first - its landing points are read from there");

            if (ImGui.BeginPopup("###spawnPicker"))
            {
                if (spawns is { Count: 0 })
                    ImGui.TextDisabled("(no spawns) - the destination room has no Spawn* entity at all");
                else if (spawns != null)
                {
                    if (ImGui.Selectable("(none) - the scene's player position"))
                    {
                        var old = it.Spawn;
                        it.Spawn = "";
                        TrackInstant(old, "", x => it.Spawn = x, "Door Spawn");
                        ImGui.CloseCurrentPopup();
                    }
                    foreach (var name in spawns)
                        if (ImGui.Selectable(name, name == it.Spawn))
                        {
                            var old = it.Spawn;
                            it.Spawn = name;
                            TrackInstant(old, name, x => it.Spawn = x, "Door Spawn");
                            ImGui.CloseCurrentPopup();
                        }
                }
                ImGui.EndPopup();
            }

            if (!string.IsNullOrEmpty(it.Spawn) && Shell.DoorRefs.CanRead(it.TargetSceneId)
                && !Shell.DoorRefs.HasEntity(it.TargetSceneId, it.Spawn))
                ImGui.TextColored(new System.Numerics.Vector4(1f, 0.72f, 0.30f, 1f),
                    $"{Icons.Warning} '{it.Spawn}' is not in that room (not placed yet, or renamed)");
            else
                ImGui.TextDisabled("Spawn is an entity name in the destination room (empty means the scene's player position)");

            EditorWidgets.FieldLabel("Facing");
            string facingLabel = string.IsNullOrEmpty(it.Facing) ? "(unset)" : it.Facing;
            ImGui.SetNextItemWidth(MathF.Max(80f, ImGui.GetContentRegionAvail().X));
            if (ImGui.BeginCombo("##DoorFacing", facingLabel))
            {
                if (ImGui.Selectable("(unset)", string.IsNullOrEmpty(it.Facing)))
                {
                    var old = it.Facing;
                    it.Facing = "";
                    TrackInstant(old, "", x => it.Facing = x, "Door Facing");
                }
                foreach (var name in Runtime.Cutscenes.Dirs.Names)
                    if (ImGui.Selectable(name, name == it.Facing))
                    {
                        var old = it.Facing;
                        it.Facing = name;
                        TrackInstant(old, name, x => it.Facing = x, "Door Facing");
                    }
                ImGui.EndCombo();
            }
            ImGui.TextDisabled("The direction to face on arrival (empty leaves it unchanged - a new room defaults to down)");
        }

        EditorWidgets.FieldLabel("Sound");
        DrawAudioField("itSound", it.SoundId, 180, v =>
        {
            var old = it.SoundId;
            if (old == v) return;
            it.SoundId = v;
            TrackInstant(old, v, x => it.SoundId = x, "Interact Sound");
        });
        ImGui.TextDisabled("A one-shot at the moment of triggering - for short sounds only (use a SoundEmitter for sustained ones)");

        var range = it.Range;
        ImGui.SetNextItemWidth(100);
        EditorWidgets.FieldLabel("Range");
        if (ImGui.DragFloat("##Range", ref range, 0.5f, 0f, 200f))
            it.Range = range;
        TrackDrag(it.Range, v => it.Range = v, "Interact Range");
        ImGui.TextDisabled("The detection radius in pixels - added to the player's Reach. About 20 for large furniture");
    }

    private void SetActiveOnSelection(bool value)
    {
        var cmds = new List<ICommand>();
        foreach (var e in _state.Selection)
        {
            if (e.Active == value) continue;
            var target = e;
            bool old = target.Active;
            target.Active = value;
            cmds.Add(new PropertyCommand<bool>(this, "Active", old, value, v => target.Active = v, "Change Active", target));
        }
        if (cmds.Count == 0) return;

        if (cmds.Count == 1) _state.CommandHistory.AddExecuted(cmds[0]);
        else _state.CommandHistory.AddExecuted(
            new CompositeCommand($"Set Active on {cmds.Count} entities", cmds.ToArray()));
        _state.MarkDirty();
    }

    private readonly List<(Entity Entity, Vector2 Start)> _groupMoveStart = new();

    private void CollectSelectionSubtrees(List<(Entity Entity, Vector2 Start)> into)
    {
        into.Clear();
        var seen = new HashSet<Entity>();
        void Add(Entity e)
        {
            if (!seen.Add(e)) return;
            var tr = e.GetComponent<Transform>();
            if (tr != null) into.Add((e, tr.Position));
            foreach (var c in e.Children) Add(c);
        }
        foreach (var e in _state.Selection) Add(e);
    }

    private void MoveSelectionBy(Transform primary, Vector2 delta)
    {
        if (primary.Entity == null || !_state.IsSelected(primary.Entity))
        {
            MoveWithChildren(primary, primary.Position + delta);
            return;
        }

        var group = new List<(Entity Entity, Vector2 Start)>();
        CollectSelectionSubtrees(group);
        foreach (var (e, _) in group)
        {
            var tr = e.GetComponent<Transform>();
            if (tr != null) tr.Position += delta;
        }
    }

    private void TrackGroupMove()
    {
        if (ImGui.IsItemActivated())
            CollectSelectionSubtrees(_groupMoveStart);

        if (ImGui.IsItemDeactivatedAfterEdit() && _groupMoveStart.Count > 0)
        {
            var moves = new List<ICommand>();
            foreach (var (e, start) in _groupMoveStart)
            {
                var tr = e.GetComponent<Transform>();
                if (tr != null && tr.Position != start)
                {
                    moves.Add(new MoveEntityCommand(e, start, tr.Position));
                    _state.KeepThroughStop(e);
                }
            }

            if (moves.Count == 1)
                _state.CommandHistory.AddExecuted(moves[0]);
            else if (moves.Count > 1)
                _state.CommandHistory.AddExecuted(
                    new CompositeCommand($"Move {moves.Count} entities", moves.ToArray()));

            if (moves.Count > 0) _state.MarkDirty();
            _groupMoveStart.Clear();
        }
    }

    private static void MoveWithChildren(Transform t, Vector2 newPos)
    {
        var delta = newPos - t.Position;
        if (delta == Vector2.Zero) return;
        t.Position = newPos;
        if (t.Entity == null) return;
        foreach (var child in t.Entity.Children)
            Commands.EntitySnapshot.MoveSubtreeBy(child, delta);
    }

    private void ColliderEditToggle(Collider2D col)
    {
        bool editing = _state.EditingCollider == col;
        if (editing)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, EditorTheme.AccentSoft);
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Accent);
        }
        string label = editing ? Icons.Crosshair + "  Editing - drag the handles in the scene view"
                               : Icons.Crosshair + "  Edit collider";
        if (ImGui.Button(label, new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, 0)))
        {
            _state.EditingCollider = editing ? null : col;
            if (_state.EditingCollider != null) _state.EditingShadow = null;
        }
        if (editing) ImGui.PopStyleColor(2);
        ImGui.Spacing();
    }

    internal const int ShadowShapeEllipse = 0, ShadowShapeSquare = 1, ShadowShapeRounded = 2, ShadowShapeTexture = 3;

    private static readonly string[] ShadowShapeNames =
        { "Ellipse (characters)", "Rectangle (standing things)", "Rounded rectangle", "Texture (art)" };

    private SpriteRenderer? _shadowTexturePick;

    private int ShadowShapeIndex(SpriteRenderer sr)
        => ShadowShapeIndexOf(sr.ShadowRadius,
               !string.IsNullOrEmpty(sr.ShadowTexturePath)
               || !string.IsNullOrEmpty(sr.UnresolvedShadowTextureRef)
               || _shadowTexturePick == sr);

    internal static int ShadowShapeIndexOf(float radius, bool hasTexture)
    {
        if (hasTexture) return ShadowShapeTexture;
        if (radius >= 1f) return ShadowShapeEllipse;
        if (radius <= 0f) return ShadowShapeSquare;
        return ShadowShapeRounded;
    }

    private void ApplyShadowShape(SpriteRenderer sr, int shape)
    {
        if (shape == ShadowShapeTexture) { _shadowTexturePick = sr; return; }
        _shadowTexturePick = null;
        ApplyShadowShapeTo(sr, shape);
    }

    internal static void ApplyShadowShapeTo(SpriteRenderer sr, int shape)
    {
        sr.SetShadowTexture(null);
        sr.ShadowRadius = shape switch
        {
            ShadowShapeEllipse => 1f,
            ShadowShapeSquare => 0f,
            _ => sr.ShadowRadius > 0f && sr.ShadowRadius < 1f ? sr.ShadowRadius : 0.5f,
        };
    }

    private void ShadowEditToggle(SpriteRenderer sr)
    {
        bool editing = _state.EditingShadow == sr;
        if (editing)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, EditorTheme.AccentSoft);
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Accent);
        }
        string label = editing ? Icons.Crosshair + "  Editing - drag the handles in the scene view"
                               : Icons.Crosshair + "  Edit shadow";
        if (ImGui.Button(label, new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, 0)))
        {
            _state.EditingShadow = editing ? null : sr;
            if (_state.EditingShadow != null) _state.EditingCollider = null;
        }
        if (editing) ImGui.PopStyleColor(2);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Centre handle = offset · four edges = size\nDragging an edge turns the automatic size into explicit values");
        ImGui.Spacing();
    }

    private void DrawCircleCollider(CircleCollider2D col)
    {
        ColliderEditToggle(col);
        var isTrigger = col.IsTrigger;
        EditorWidgets.FieldLabel("Is Trigger");
        if (EditorWidgets.TinyCheckbox("##Is Trigger", ref isTrigger))
        {
            col.IsTrigger = isTrigger;
            TrackInstant(!isTrigger, isTrigger, v => col.IsTrigger = v, "Is Trigger");
        }

        var offset = new System.Numerics.Vector2(col.Offset.X, col.Offset.Y);
        EditorWidgets.FieldLabel("Offset");
        if (ImGui.DragFloat2("##Offset", ref offset))
            col.Offset = new Vector2(offset.X, offset.Y);
        EditorWidgets.AxisTint();
        TrackDrag(col.Offset, v => col.Offset = v, "Offset");

        var radius = col.Radius;
        ImGui.SetNextItemWidth(100);
        EditorWidgets.FieldLabel("Radius");
        if (ImGui.DragFloat("##Radius", ref radius, 0.5f, 1f, 256f))
            col.Radius = radius;
        TrackDrag(col.Radius, v => col.Radius = v, "Radius");
    }

    private void DrawCapsuleCollider(CapsuleCollider2D col)
    {
        ColliderEditToggle(col);
        var isTrigger = col.IsTrigger;
        EditorWidgets.FieldLabel("Is Trigger");
        if (EditorWidgets.TinyCheckbox("##Is Trigger", ref isTrigger))
        {
            col.IsTrigger = isTrigger;
            TrackInstant(!isTrigger, isTrigger, v => col.IsTrigger = v, "Is Trigger");
        }
        var dir = col.Horizontal ? 1 : 0;
        EditorWidgets.FieldLabel("Direction");
        if (ImGui.Combo("##CapsuleDir", ref dir, "Vertical\0Horizontal\0"))
        {
            bool h = dir == 1;
            if (h != col.Horizontal)
            {
                col.Horizontal = h;
                TrackInstant(!h, h, v => col.Horizontal = v, "Horizontal");
            }
        }

        var offset = new System.Numerics.Vector2(col.Offset.X, col.Offset.Y);
        EditorWidgets.FieldLabel("Offset");
        if (ImGui.DragFloat2("##Offset", ref offset))
            col.Offset = new Vector2(offset.X, offset.Y);
        EditorWidgets.AxisTint();
        TrackDrag(col.Offset, v => col.Offset = v, "Offset");

        var radius = col.Radius;
        ImGui.SetNextItemWidth(100);
        EditorWidgets.FieldLabel("Radius");
        if (ImGui.DragFloat("##Radius", ref radius, 0.5f, 1f, 256f))
            col.Radius = radius;
        TrackDrag(col.Radius, v => col.Radius = v, "Radius");

        var length = col.Length;
        ImGui.SetNextItemWidth(100);
        EditorWidgets.FieldLabel("Length");
        if (ImGui.DragFloat("##Length", ref length, 0.5f, 0f, 512f))
            col.Length = length;
        TrackDrag(col.Length, v => col.Length = v, "Length");
        ImGui.TextDisabled("The core length - the total length is Length + Radius x 2");
    }

    private void DrawBoxCollider2D(BoxCollider2D col)
    {
        ColliderEditToggle(col);
        var isTrigger = col.IsTrigger;
        EditorWidgets.FieldLabel("Is Trigger");
        if (EditorWidgets.TinyCheckbox("##Is Trigger", ref isTrigger))
        {
            col.IsTrigger = isTrigger;
            TrackInstant(!isTrigger, isTrigger, v => col.IsTrigger = v, "Is Trigger");
        }

        var isOneWay = col.IsOneWay;
        EditorWidgets.FieldLabel("Is One-Way");
        if (EditorWidgets.TinyCheckbox("##Is One-Way", ref isOneWay))
        {
            col.IsOneWay = isOneWay;
            TrackInstant(!isOneWay, isOneWay, v => col.IsOneWay = v, "Is One-Way");
        }

        var offset = new System.Numerics.Vector2(col.Offset.X, col.Offset.Y);
        EditorWidgets.FieldLabel("Offset");
        if (ImGui.DragFloat2("##Offset", ref offset))
            col.Offset = new Vector2(offset.X, offset.Y);
        EditorWidgets.AxisTint();
        TrackDrag(col.Offset, v => col.Offset = v, "Offset");

        var size = new System.Numerics.Vector2(col.Size.X, col.Size.Y);
        EditorWidgets.FieldLabel("Size");
        if (ImGui.DragFloat2("##Size", ref size))
            col.Size = new Vector2(MathF.Max(1f, size.X), MathF.Max(1f, size.Y));
        EditorWidgets.AxisTint();
        TrackDrag(col.Size, v => col.Size = v, "Collider Size");
    }

    private void DrawTilemapRenderer(TilemapRenderer tr)
    {
        var tileSize = tr.TileSize;
        EditorWidgets.FieldLabel("Tile Size");
        if (ImGui.DragInt("##Tile Size", ref tileSize, 1, 8, 128))
            tr.TileSize = tileSize;
        TrackDrag(tr.TileSize, v => tr.TileSize = v, "Tile Size");

        ImGui.Text($"Layers: {tr.LayerCount}");
    }

    private void DrawAnimator(Animator anim)
    {
        if (anim.Entity?.GetComponent<SpriteRenderer>() == null)
        {
            ImGui.TextColored(EditorTheme.Accent,
                $"{Icons.Warning}  There is no renderer to draw with - attach a SpriteRenderer");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("An Animator chooses the art and a SpriteRenderer draws it.\n"
                    + "With no sibling, a running clip puts nothing on screen.");
            ImGui.Spacing();
        }

        ImGui.Text($"Current: {anim.CurrentClip?.Name ?? "(none)"}");
        ImGui.Text($"Playing: {anim.IsPlaying}");

        ImGui.Spacing();
        DrawAnimatorClips(anim);
        ImGui.Spacing();

        var speed = anim.Speed;
        EditorWidgets.FieldLabel("Speed");
        if (ImGui.DragFloat("##Speed", ref speed, 0.1f, 0f, 5f))
            anim.Speed = speed;
        TrackDrag(anim.Speed, v => anim.Speed = v, "Speed");
    }

    private void DrawAnimatorClips(Animator anim)
    {
        if (anim.ClipCount == 0)
            EditorWidgets.Hint("No clips - the SpriteRenderer's authored slice is what shows.");

        EditorWidgets.FieldLabel("Clips");
        ImGui.TextDisabled($"{anim.ClipCount}");

        string? removeRequest = null;
        foreach (var clip in anim.Clips)
        {
            ImGui.PushID(clip.Name);

            if (ImGui.SmallButton(Icons.Xmark)) removeRequest = clip.Name;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Remove '{clip.Name}'");
            ImGui.SameLine();

            string meta = $"{clip.Frames.Count} frames{(clip.Loop ? " · loop" : "")}";
            float metaW = ImGui.CalcTextSize(meta).X + ImGui.GetStyle().ItemSpacing.X * 2f;
            DrawAssetRefField($"clip_{clip.Name}", Icons.Film, clip.Name, Shell.AssetPing.FromId(clip.SourceId),
                showPicker: false, width: MathF.Max(60f, ImGui.GetContentRegionAvail().X - metaW));
            ImGui.SameLine();
            ImGui.TextDisabled(meta);

            if (string.IsNullOrEmpty(clip.SourceId))
            {
                ImGui.SameLine();
                ImGui.TextColored(EditorTheme.Accent, Icons.Warning);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("It has no source (.anim id) and cannot be saved - saving the scene loses this clip.");
            }

            ImGui.PopID();
        }

        if (removeRequest != null) RemoveAnimatorClip(anim, removeRequest);

        if (ImGui.Button($"{Icons.Plus}  Add clip", new System.Numerics.Vector2(-1, 0)))
        {
            ScanPickerAnims();
            _animPickerQuery = "";
            ImGui.OpenPopup("###animPicker");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Click to choose a .anim, or drag one in from the explorer");

        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("ANIM_FILE");
                if (payload.NativePtr != null)
                {
                    var dragged = DraggedAnimPathProvider?.Invoke();
                    if (!string.IsNullOrEmpty(dragged)) AddAnimatorClip(anim, dragged);
                }
            }
            ImGui.EndDragDropTarget();
        }

        DrawAnimPickerPopup(anim);

        EditorWidgets.FieldLabel("Default Clip");
        var names = anim.Clips.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        string currentDefault = string.IsNullOrEmpty(anim.DefaultClip) ? "(none)" : anim.DefaultClip!;

        if (ImGui.BeginCombo("##defaultClip", currentDefault))
        {
            if (ImGui.Selectable("(none)", string.IsNullOrEmpty(anim.DefaultClip)))
                SetAnimatorDefaultClip(anim, null);

            foreach (var n in names)
                if (ImGui.Selectable(n, n == anim.DefaultClip))
                    SetAnimatorDefaultClip(anim, n);

            ImGui.EndCombo();
        }

        if (anim.ClipCount > 0 && string.IsNullOrEmpty(anim.DefaultClip))
        {
            EditorWidgets.Hint("No default clip - the authored slice shows right after loading.");
        }
        else if (!string.IsNullOrEmpty(anim.DefaultClip) && !anim.HasClip(anim.DefaultClip!))
        {
            ImGui.TextColored(EditorTheme.Accent,
                $"{Icons.Warning}  The default clip '{anim.DefaultClip}' is not in the list");
        }
    }

    private void DrawAnimPickerPopup(Animator anim)
    {
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(300, 380), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup("###animPicker")) return;

        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##animQuery", Icons.Search + "  Search", ref _animPickerQuery, 128);
        ImGui.Separator();

        ImGui.BeginChild("##animList", new System.Numerics.Vector2(0, 0));
        foreach (var full in _pickerAnims)
        {
            var rel = EditorApp.ToContentRelative(full);
            if (_animPickerQuery.Length > 0 &&
                !rel.Contains(_animPickerQuery, StringComparison.OrdinalIgnoreCase)) continue;

            if (ImGui.Selectable(rel))
            {
                AddAnimatorClip(anim, full);
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.EndChild();
        ImGui.EndPopup();
    }

    private void AddAnimatorClip(Animator anim, string animFullPath)
    {
        var before = AnimatorClipsState.Capture(anim);
        if (!anim.LoadFromFile(animFullPath, _graphicsDevice, EditorApp.ContentRoot))
        {
            Console.WriteLine($"[Inspector] the clip could not be read: {animFullPath}");
            return;
        }

        var after = AnimatorClipsState.Capture(anim);
        if (before.SameAs(after)) { _state.MarkDirty(); return; }
        TrackInstant(before, after, s => AnimatorClipsState.Restore(anim, s), "Add Clip");
    }

    private void RemoveAnimatorClip(Animator anim, string clipName)
    {
        var before = AnimatorClipsState.Capture(anim);
        if (!AnimatorClipsState.Remove(anim, clipName)) return;
        var after = AnimatorClipsState.Capture(anim);
        TrackInstant(before, after, s => AnimatorClipsState.Restore(anim, s), "Remove Clip");
    }

    private void SetAnimatorDefaultClip(Animator anim, string? clipName)
    {
        if (anim.DefaultClip == clipName) return;
        var before = AnimatorClipsState.Capture(anim);
        AnimatorClipsState.SetDefault(anim, clipName);
        var after = AnimatorClipsState.Capture(anim);
        TrackInstant(before, after, s => AnimatorClipsState.Restore(anim, s), "Default Clip");
    }

    private string[] _pickerAnims = Array.Empty<string>();
    private string _animPickerQuery = "";

    private void ScanPickerAnims()
    {
        var root = EditorApp.ContentRoot;
        if (!Directory.Exists(root)) { _pickerAnims = Array.Empty<string>(); return; }
        _pickerAnims = Directory.GetFiles(root, "*.anim", SearchOption.AllDirectories)
            .Select(f => f.Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    #endregion

    #region Helpers

    internal static bool HasComponentOfType(Entity entity, Type type)
    {
        foreach (var comp in entity.Components)
        {
            if (comp.GetType() == type)
                return true;
        }
        return false;
    }

    private static void WarnMultipleRenderers(Entity entity)
    {
        int n = 0;
        foreach (var comp in entity.Components)
            if (comp is SpriteRenderer) n++;
        if (n < 2) return;

        ImGui.TextColored(EditorTheme.Accent,
            $"{Icons.Warning}  {n} SpriteRenderers - only one may remain");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("An entity has one renderer - the save carries only one,\n"
                + "so the rest disappear silently on the next load.\n"
                + "(An Animator is not a renderer and does not trip this - having both is normal.)");
    }

    private void AddComponentByType(Entity entity, Type type)
    {
        var method = EditorReflection.AddComponentOpen.MakeGenericMethod(type);
        method.Invoke(entity, null);

        if (type == typeof(BoxCollider2D))
        {
            var col = entity.GetComponent<BoxCollider2D>();
            var sr = entity.GetComponent<SpriteRenderer>();
            if (col != null && sr != null)
            {
                var native = sr.GetDrawSize();
                if (native != Vector2.Zero) col.Size = native;
            }
        }
    }

    public Func<string?>? DraggedTexturePathProvider { get; set; }

    public Func<string?>? DraggedPostPathProvider { get; set; }

    public Func<string?>? DraggedAudioPathProvider { get; set; }

    public Func<string?>? DraggedAnimPathProvider { get; set; }

    public Action<string>? OnPostAssetDropped { get; set; }

    public Action<string>? OnRevealAssetRequested { get; set; }

    public Action<string>? OnOpenAssetRequested { get; set; }

    private System.Numerics.Vector2 _refFieldMin, _refFieldMax;

    private bool DrawAssetRefField(string id, string icon, string label, string? relPath,
                                   string? tooltip = null, bool showPicker = true,
                                   string? dropPayload = null, Action<string>? onDrop = null,
                                   Action? onClear = null, float width = -1f)
    {
        float avail = width < 0 ? ImGui.CalcItemWidth() : width;
        float h = ImGui.GetFrameHeight();
        float pickW = showPicker ? h + ImGui.GetStyle().ItemInnerSpacing.X : 0f;
        float nameW = MathF.Max(40f, avail - pickW);

        string? full = string.IsNullOrEmpty(relPath) ? null
            : Path.GetFullPath(Path.Combine(EditorApp.ContentRoot, relPath)).Replace('\\', '/');
        bool exists = full != null && File.Exists(full);

        ImGui.Button($"{icon}  {label}###{id}", new System.Numerics.Vector2(nameW, 0));
        _refFieldMin = ImGui.GetItemRectMin();
        _refFieldMax = ImGui.GetItemRectMax();
        if (exists && ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) OnOpenAssetRequested?.Invoke(full!);
            else OnRevealAssetRequested?.Invoke(full!);
        }
        if (ImGui.IsItemHovered())
        {
            var parts = new List<string>();
            if (exists) parts.Add("click to find it in the explorer, double-click to open");
            if (showPicker) parts.Add(exists ? "the button changes it" : "the button chooses one");
            if (dropPayload != null) parts.Add("it can be dragged and dropped too");
            string gest = string.Join(" · ", parts);
            string tip = string.IsNullOrEmpty(tooltip) ? gest
                       : gest.Length > 0 ? tooltip + "\n\n" + gest : tooltip;
            if (tip.Length > 0) ImGui.SetTooltip(tip);
        }

        if (dropPayload != null && onDrop != null && ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload(dropPayload);
                if (payload.NativePtr != null)
                {
                    var dragged = DraggedPathFor(dropPayload);
                    if (!string.IsNullOrEmpty(dragged)) onDrop(dragged);
                }
            }
            ImGui.EndDragDropTarget();
        }

        if (ImGui.BeginPopupContextItem($"###{id}_ctx"))
        {
            ImGui.BeginDisabled(!exists);
            if (ImGui.MenuItem(Icons.FolderOpen + "  Show in the explorer")) OnRevealAssetRequested?.Invoke(full!);
            if (ImGui.MenuItem("Open")) OnOpenAssetRequested?.Invoke(full!);
            if (ImGui.MenuItem("Copy path")) ImGui.SetClipboardText(relPath!);
            ImGui.EndDisabled();
            if (onClear != null)
            {
                ImGui.Separator();
                if (ImGui.MenuItem("Clear")) onClear();
            }
            ImGui.EndPopup();
        }

        bool pick = false;
        if (showPicker)
        {
            ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
            pick = ImGui.Button($"###{id}_pick", new System.Numerics.Vector2(h, h));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Change - choose from a list");
            var dl = ImGui.GetWindowDrawList();
            var c = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * 0.5f;
            uint col = ImGui.GetColorU32(ImGuiCol.Text);
            dl.AddCircle(c, h * 0.26f, col, 16, 1.2f);
            dl.AddCircleFilled(c, h * 0.10f, col);
        }
        return pick;
    }

    private string? DraggedPathFor(string payload) => payload switch
    {
        "TEXTURE_FILE" => DraggedTexturePathProvider?.Invoke(),
        "AUDIO_FILE"   => DraggedAudioPathProvider?.Invoke(),
        "ANIM_FILE"    => DraggedAnimPathProvider?.Invoke(),
        "POST_FILE"    => DraggedPostPathProvider?.Invoke(),
        _ => null,
    };

    private static void DrawTrailingDir(string dir, string visibleName)
    {
        if (dir.Length == 0) return;
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        bool hovered = ImGui.IsItemHovered();

        float nameEnd = min.X + ImGui.CalcTextSize(visibleName).X + 16f;
        float avail = max.X - 6f - nameEnd;

        ImGui.PushFont(ImGuiRenderer.MonoFont);
        string shown = avail >= 40f ? EditorWidgets.EllipsizeHeadPath(dir, avail) : "";
        if (shown.Length > 0)
        {
            var ts = ImGui.CalcTextSize(shown);
            ImGui.GetWindowDrawList().AddText(
                new System.Numerics.Vector2(max.X - ts.X - 6, min.Y + (max.Y - min.Y - ts.Y) * 0.5f),
                ImGui.GetColorU32(ImGuiCol.TextDisabled), shown);
        }
        ImGui.PopFont();

        if (hovered && shown != dir) ImGui.SetTooltip(dir);
    }

    private string[] _pickerImages = Array.Empty<string>();
    private string _pickerQuery = "";

    private void DrawLightTextureField(Light2D light)
    {
        string current = !string.IsNullOrEmpty(light.TexturePath)
            ? Path.GetFileNameWithoutExtension(light.TexturePath)
            : !string.IsNullOrEmpty(light.UnresolvedTextureRef) ? "(not found)" : "(none)";

        EditorWidgets.FieldLabel("Texture");
        bool openPicker = DrawAssetRefField("lightTexField", Icons.Image, current, light.TexturePath,
            tooltip: "The light decal PNG - drawn in greyscale, with the colour coming from the tint",
            dropPayload: "TEXTURE_FILE", onDrop: dragged => ApplyLightTexturePick(light, dragged),
            onClear: () => ApplyLightTexturePick(light, null));
        if (openPicker)
        {
            ScanPickerImages();
            _pickerQuery = "";
            ImGui.OpenPopup("###lightTexPicker");
        }

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(300, 380), ImGuiCond.Appearing);
        if (ImGui.BeginPopup("###lightTexPicker"))
        {
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##lightTexQuery", Icons.Search + "  Search", ref _pickerQuery, 128);
            ImGui.Separator();

            ImGui.BeginChild("##lightTexList", new System.Numerics.Vector2(0, 0));

            if (ImGui.Selectable("(none) - remove the art"))
            {
                ApplyLightTexturePick(light, null);
                ImGui.CloseCurrentPopup();
            }

            foreach (var full in _pickerImages)
            {
                var rel = EditorApp.ToContentRelative(full);
                if (_pickerQuery.Length > 0 &&
                    !rel.Contains(_pickerQuery, StringComparison.OrdinalIgnoreCase))
                    continue;

                string bare = Path.GetFileNameWithoutExtension(full);
                if (ImGui.Selectable($"{Icons.Image}  {bare}##lightpick_{rel}"))
                {
                    ApplyLightTexturePick(light, full);
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.EndChild();
            ImGui.EndPopup();
        }
    }

    private void DrawShadowTextureField(SpriteRenderer sr)
    {
        string current = !string.IsNullOrEmpty(sr.ShadowTexturePath)
            ? Path.GetFileNameWithoutExtension(sr.ShadowTexturePath)
            : !string.IsNullOrEmpty(sr.UnresolvedShadowTextureRef) ? "(not found)" : "(default ellipse)";

        EditorWidgets.FieldLabel("Shadow Tex");
        bool openPicker = DrawAssetRefField("shadowTexField", Icons.Image, current, sr.ShadowTexturePath,
            tooltip: "Shadow PNG (optional) - leave it empty for the flat ellipse the engine makes\n" +
                     "Laying it flat and compositing overlaps are still done by the engine when art is supplied",
            dropPayload: "TEXTURE_FILE", onDrop: dragged => ApplyShadowTexturePick(sr, dragged),
            onClear: () => ApplyShadowTexturePick(sr, null));
        if (openPicker)
        {
            ScanPickerImages();
            _pickerQuery = "";
            ImGui.OpenPopup("###shadowTexPicker");
        }

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(300, 380), ImGuiCond.Appearing);
        if (ImGui.BeginPopup("###shadowTexPicker"))
        {
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##shadowTexQuery", Icons.Search + "  Search", ref _pickerQuery, 128);
            ImGui.Separator();
            ImGui.BeginChild("##shadowTexList", new System.Numerics.Vector2(0, 0));

            if (ImGui.Selectable("(default ellipse) - remove art"))
            {
                ApplyShadowTexturePick(sr, null);
                ImGui.CloseCurrentPopup();
            }

            foreach (var full in _pickerImages)
            {
                var rel = EditorApp.ToContentRelative(full);
                if (_pickerQuery.Length > 0 &&
                    !rel.Contains(_pickerQuery, StringComparison.OrdinalIgnoreCase))
                    continue;

                string bare = Path.GetFileNameWithoutExtension(full);
                if (ImGui.Selectable($"{Icons.Image}  {bare}##shadowpick_{rel}"))
                {
                    ApplyShadowTexturePick(sr, full);
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.EndChild();
            ImGui.EndPopup();
        }
    }

    private void ApplyShadowTexturePick(SpriteRenderer sr, string? pngFullPath)
    {
        var before = sr.ShadowTexturePath;
        var after = pngFullPath == null ? null : EditorApp.ToContentRelative(pngFullPath);
        if (before == after) { _state.MarkDirty(); return; }

        sr.SetShadowTexture(after);
        if (!string.IsNullOrEmpty(after)) AssetRegistry.Instance.GetOrCreateId(after);
        TrackInstant(before, after, v => sr.SetShadowTexture(v), "Shadow Texture");
    }

    private void ApplyLightTexturePick(Light2D light, string? pngFullPath)
    {
        var before = light.TexturePath;
        var after = pngFullPath == null ? null : EditorApp.ToContentRelative(pngFullPath);
        if (before == after) { _state.MarkDirty(); return; }

        light.SetTexture(after);
        if (!string.IsNullOrEmpty(after)) AssetRegistry.Instance.GetOrCreateId(after);
        TrackInstant(before, after, v => light.SetTexture(v), "Light Texture");
    }

    private void DrawSpriteField(SpriteRenderer sr)
    {
        string current = !string.IsNullOrEmpty(sr.SliceName) ? sr.SliceName!
            : !string.IsNullOrEmpty(sr.TexturePath) ? Path.GetFileNameWithoutExtension(sr.TexturePath)
            : "(none)";

        EditorWidgets.FieldLabel("Sprite");
        bool openPicker = DrawAssetRefField("spriteField", Icons.Image, current,
            Shell.AssetPing.SpriteTarget(sr.AtlasPath, sr.TexturePath),
            dropPayload: "TEXTURE_FILE", onDrop: dragged => ApplySpritePick(sr, dragged),
            onClear: () => ClearSprite(sr));
        if (openPicker)
        {
            ScanPickerImages();
            _pickerQuery = "";
            ImGui.OpenPopup("###spritePicker");
        }

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(300, 380), ImGuiCond.Appearing);
        if (ImGui.BeginPopup("###spritePicker"))
        {
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##spriteQuery", Icons.Search + "  Search", ref _pickerQuery, 128);
            ImGui.Separator();

            ImGui.BeginChild("##spriteList", new System.Numerics.Vector2(0, 0));

            if (ImGui.Selectable("(none) - remove the sprite"))
            {
                ClearSprite(sr);
                ImGui.CloseCurrentPopup();
            }

            foreach (var full in _pickerImages)
            {
                var rel = EditorApp.ToContentRelative(full);
                if (_pickerQuery.Length > 0 &&
                    !rel.Contains(_pickerQuery, StringComparison.OrdinalIgnoreCase))
                    continue;

                string bare = Path.GetFileNameWithoutExtension(full);
                string dir = Path.GetDirectoryName(rel)?.Replace('\\', '/') ?? "";
                bool hasThumb = ThumbnailCache.TryGet(full, out var thumbId, out var thumbTexSize,
                    out var thumbSlice0, out _);
                string rowLabel = hasThumb ? $"      {bare}##pick_{rel}" : $"{Icons.Image}  {bare}##pick_{rel}";
                if (ImGui.Selectable(rowLabel))
                {
                    ApplySpritePick(sr, full);
                    ImGui.CloseCurrentPopup();
                }
                if (hasThumb)
                {
                    var src = thumbSlice0 ?? new Rectangle(0, 0, thumbTexSize.X, thumbTexSize.Y);
                    float boxSz = MathF.Min(18f, ImGui.GetItemRectSize().Y);
                    var drawSz = ThumbnailCache.FitSize(src, new System.Numerics.Vector2(boxSz, boxSz));
                    var (uv0, uv1) = ThumbnailCache.Uv(src, thumbTexSize);
                    var pmin = new System.Numerics.Vector2(
                        ImGui.GetItemRectMin().X + 1 + (boxSz - drawSz.X) * 0.5f,
                        ImGui.GetItemRectMin().Y + (ImGui.GetItemRectSize().Y - drawSz.Y) * 0.5f);
                    ImGui.GetWindowDrawList().AddImage(thumbId, pmin, pmin + drawSz, uv0, uv1);
                }
                DrawTrailingDir(dir, hasThumb ? "      " + bare : Icons.Image + "  " + bare);
            }

            ImGui.EndChild();
            ImGui.EndPopup();
        }
    }

    private void ApplySpritePick(SpriteRenderer sr, string pngFullPath)
    {
        var before = SpriteState.Capture(sr);
        try
        {
            var atlasFull = Path.ChangeExtension(pngFullPath, ".atlas");
            var atlas = File.Exists(atlasFull) ? SpriteAtlas.Load(atlasFull) : null;
            if (atlas != null && atlas.Slices.Count > 0)
            {
                sr.TexturePath = null;
                sr.SetSprite(EditorApp.ToContentRelative(atlasFull), atlas.Slices[0].Name);
                AssetRegistry.Instance.GetOrCreateId(sr.AtlasPath!);
            }
            else
            {
                using var stream = File.OpenRead(pngFullPath);
                sr.Texture = Microsoft.Xna.Framework.Graphics.Texture2D.FromStream(_graphicsDevice, stream);
                sr.AtlasPath = null;
                sr.SliceName = null;
                sr.SourceRect = null;
                sr.TexturePath = EditorApp.ToContentRelative(pngFullPath);
                AssetRegistry.Instance.GetOrCreateId(sr.TexturePath);
            }

            var after = SpriteState.Capture(sr);
            if (before.SameAs(after)) { _state.MarkDirty(); return; }
            TrackInstant(before, after, s => SpriteState.Restore(sr, s), "Sprite");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Inspector] the sprite swap failed: {pngFullPath} - {ex.Message}");
        }
    }

    private void ClearSprite(SpriteRenderer sr)
    {
        var before = SpriteState.Capture(sr);
        sr.AtlasPath = null;
        sr.SliceName = null;
        sr.TexturePath = null;
        sr.Texture = null;
        sr.SourceRect = null;
        var after = SpriteState.Capture(sr);
        if (before.SameAs(after)) { _state.MarkDirty(); return; }
        TrackInstant(before, after, s => SpriteState.Restore(sr, s), "Sprite");
    }

    private List<Shell.DoorRefs.RoomEntry> _roomPickerRooms = new();
    private string _roomPickerQuery = "";

    private void DrawRoomPicker(string id, string currentId, float width, Action<string> onPick)
    {
        bool empty = string.IsNullOrWhiteSpace(currentId);

        bool legacy = !empty && !AssetRegistry.LooksLikeId(currentId);
        string? name = legacy ? null : Shell.DoorRefs.RoomName(currentId);
        bool lost = !empty && !legacy && name == null;

        string label = empty  ? "(no destination)"
                     : legacy ? $"A legacy name: {currentId}"
                     : lost   ? $"Unknown room {currentId}"
                     : name!;

        bool bad = legacy || lost;
        string tip = legacy ? $"A room name from before the id change: '{currentId}'\nPicking the room again saves it as an id."
                   : lost ? $"A scene id the registry does not know: {currentId}\nThe scene was deleted or has not been scanned."
                   : "It is saved as a scene id - renaming the room does not break it." + (empty ? "" : $"\n(id {currentId})");
        if (bad) ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Danger);
        bool openPicker = DrawAssetRefField(id, Icons.Cube, label, bad ? null : Shell.AssetPing.FromId(currentId),
            tooltip: tip, onClear: () => onPick(""), width: width);
        if (bad) ImGui.PopStyleColor();
        if (openPicker)
        {
            _roomPickerRooms = Shell.DoorRefs.Rooms();
            _roomPickerQuery = "";
            ImGui.OpenPopup($"###{id}_picker");
        }

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(330, 320), ImGuiCond.Appearing);
        if (ImGui.BeginPopup($"###{id}_picker"))
        {
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##roomQuery", Icons.Search + "  Search", ref _roomPickerQuery, 128);
            ImGui.Separator();
            ImGui.BeginChild("##roomList", new System.Numerics.Vector2(0, 0));

            if (_roomPickerRooms.Count == 0)
                ImGui.TextDisabled("(There are no rooms - create a scene in Content/Scenes, or rescan)");

            foreach (var room in _roomPickerRooms)
            {
                if (_roomPickerQuery.Length > 0 &&
                    !room.Name.Contains(_roomPickerQuery, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (ImGui.Selectable($"{room.Name}##room_{room.SceneId}", room.SceneId == currentId))
                {
                    onPick(room.SceneId);
                    ImGui.CloseCurrentPopup();
                }

                DrawTrailingDir(room.RelDir, room.Name);
            }

            ImGui.EndChild();
            ImGui.EndPopup();
        }
    }

    private string[] _audioPickerFiles = Array.Empty<string>();
    private string _audioPickerQuery = "";
    private string? _audioPreviewKey;
    private string? _audioPreviewOwner;
    private Microsoft.Xna.Framework.Audio.SoundEffectInstance? _audioPreview;

    private void DrawAudioField(string id, string current, float width, Action<string> onPick,
                                bool warnStereo = false)
    {
        bool empty = string.IsNullOrWhiteSpace(current);

        string? reference = empty ? "" : AssetRegistry.Instance.GetPath(current);

        bool lost = !empty && reference == null;
        bool broken = lost || (!empty && ResolveAudioFile(reference!) == null);
        string label = empty ? "(none)"
                     : lost ? $"Unknown asset {current}"
                     : Path.GetFileNameWithoutExtension(reference!);

        string? resolved = (!empty && !broken) ? ResolveAudioFile(reference!) : null;
        bool stereoWarn = warnStereo && resolved != null
                          && PixelCore.Runtime.Audio.AudioFileInfo.IsStereo(resolved);
        string tip = broken
            ? (lost
                ? $"An asset id the registry does not know: {current}\nThe file was deleted, or assets.json has not been scanned.\nUse the button to pick again"
                : $"The file cannot be found: {reference}\nUse the button to pick again")
            : stereoWarn
                ? "⚠ This is a stereo file - a point source has to be mono for panning to work.\n" +
                  "The position is what the scene provides, so the source material must carry no left-right information.\n" +
                  "Re-export it as mono."
                : "";

        if (broken) ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Danger);
        bool openPicker = DrawAssetRefField(id, Icons.Speaker, label,
            resolved != null ? EditorApp.ToContentRelative(resolved) : null,
            tooltip: tip.Length > 0 ? tip : null,
            dropPayload: "AUDIO_FILE", onDrop: dragged => onPick(ToAudioValue(dragged)),
            onClear: () => { StopAudioPreview(); onPick(""); }, width: width);
        if (broken) ImGui.PopStyleColor();
        if (openPicker)
        {
            ScanAudioPicker();
            _audioPickerQuery = "";
            ImGui.OpenPopup($"###{id}_picker");
        }

        if (stereoWarn)
        {
            var mn = _refFieldMin;
            var mx = _refFieldMax;
            var sz = ImGui.CalcTextSize(Icons.Warning);
            float limitX = ImGui.GetWindowPos().X + ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX();
            float badgeX = MathF.Min(mx.X, limitX) - sz.X - 6;
            ImGui.GetWindowDrawList().AddText(
                new System.Numerics.Vector2(badgeX, mn.Y + (mx.Y - mn.Y - sz.Y) * 0.5f),
                ImGui.GetColorU32(EditorTheme.Accent), Icons.Warning);
        }

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(330, 380), ImGuiCond.Appearing);
        if (ImGui.BeginPopup($"###{id}_picker"))
        {
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##audioQuery", Icons.Search + "  Search", ref _audioPickerQuery, 128);
            ImGui.Separator();
            ImGui.BeginChild("##audioList", new System.Numerics.Vector2(0, 0));

            if (ImGui.Selectable("(none) - remove the sound"))
            {
                StopAudioPreview();
                onPick("");
                ImGui.CloseCurrentPopup();
            }

            foreach (var full in _audioPickerFiles)
            {
                var rel = EditorApp.ToContentRelative(full);
                if (_audioPickerQuery.Length > 0 &&
                    !rel.Contains(_audioPickerQuery, StringComparison.OrdinalIgnoreCase))
                    continue;

                string bare = Path.GetFileNameWithoutExtension(full);
                string dir = Path.GetDirectoryName(rel)?.Replace('\\', '/') ?? "";
                bool playing = _audioPreviewKey == full;

                if (ImGui.SmallButton($"{(playing ? Icons.Stop : Icons.Play)}##prev_{rel}"))
                {
                    if (playing) StopAudioPreview(); else StartAudioPreview(full, id);
                }
                ImGui.SameLine(0, 6);

                if (ImGui.Selectable($"{bare}##pick_{rel}"))
                {
                    StopAudioPreview();
                    onPick(ToAudioValue(full));
                    ImGui.CloseCurrentPopup();
                }
                DrawTrailingDir(dir, bare);
            }

            ImGui.EndChild();
            ImGui.EndPopup();
        }
        else if (_audioPreviewOwner == id && !ImGui.IsPopupOpen($"###{id}_picker"))
        {
            StopAudioPreview();
        }
    }

    internal static string ToAudioValue(string fullPath)
        => AssetRegistry.Instance.GetOrCreateId(EditorApp.ToContentRelative(fullPath).Replace('\\', '/'));

    private static string ToAudioRef(string fullPath)
    {
        var rel = EditorApp.ToContentRelative(fullPath).Replace('\\', '/');
        int dot = rel.LastIndexOf('.');
        return dot > rel.LastIndexOf('/') && dot >= 0 ? rel[..dot] : rel;
    }

    private static string? ResolveAudioFile(string reference)
    {
        var baseName = Path.Combine(EditorApp.ContentRoot, reference).Replace('\\', '/');
        if (Path.HasExtension(baseName)) return File.Exists(baseName) ? baseName : null;
        foreach (var ext in new[] { ".ogg", ".wav" })
            if (File.Exists(baseName + ext)) return baseName + ext;
        return null;
    }

    private void ScanAudioPicker()
    {
        var root = EditorApp.ContentRoot;
        if (!Directory.Exists(root)) { _audioPickerFiles = Array.Empty<string>(); return; }
        _audioPickerFiles = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".ogg" or ".wav")
            .Select(f => f.Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void StartAudioPreview(string fullPath, string ownerId)
    {
        StopAudioPreview();
        var audio = PixelCore.Runtime.Audio.AudioManager.Instance;
        _audioPreview = audio.PlayLoop(ToAudioRef(fullPath),
                                       PixelCore.Runtime.Audio.AudioBus.UI, volume: 1f, fadeIn: 0f);
        _audioPreviewKey = _audioPreview != null ? fullPath : null;
        _audioPreviewOwner = _audioPreview != null ? ownerId : null;
        if (_audioPreview == null)
            Console.WriteLine($"[Inspector] the preview failed: {fullPath}");
    }

    private void StopAudioPreview()
    {
        if (_audioPreview != null)
            PixelCore.Runtime.Audio.AudioManager.Instance.StopLoopInstance(_audioPreview);
        _audioPreview = null;
        _audioPreviewKey = null;
        _audioPreviewOwner = null;
    }

    private void ScanPickerImages()
    {
        var root = EditorApp.ContentRoot;
        if (!Directory.Exists(root)) { _pickerImages = Array.Empty<string>(); return; }
        _pickerImages = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp")
            .Select(f => f.Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string[]? _cachedAtlasFiles;
    private DateTime _lastAtlasScan;

    private string[] GetAtlasFiles()
    {
        if (_cachedAtlasFiles == null || (DateTime.Now - _lastAtlasScan).TotalSeconds > 5)
        {
            var contentPath = EditorApp.ContentRoot;
            if (Directory.Exists(contentPath))
            {
                var files = Directory.GetFiles(contentPath, "*.atlas", SearchOption.AllDirectories);
                _cachedAtlasFiles = files.Select(f => Path.GetRelativePath(contentPath, f))
                                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                         .ToArray();
            }
            else
            {
                _cachedAtlasFiles = Array.Empty<string>();
            }
            _lastAtlasScan = DateTime.Now;
        }
        return _cachedAtlasFiles;
    }

    private SpriteAtlas? LoadAtlas(string atlasPath)
    {
        var fullPath = Path.Combine(EditorApp.ContentRoot, atlasPath);
        return SpriteAtlas.Load(fullPath);
    }

    #endregion
}
