using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using PixelCore.Runtime.Components;
using XnaV2 = Microsoft.Xna.Framework.Vector2;

namespace PixelCore.Editor;

public static class DebugOverlay
{
    public static bool ShowCone = true;
    public static bool ShowDeadzone;
    public static bool ShowSortLines;
    public static bool ShowFrameGraph = true;
    public static bool ShowAudio;
    public static bool ShowCameraTuning;
    public static bool ShowNav;

    public static EditorPrefs? Prefs;

    public static (int vw, int vh, int canvasW, int canvasH, float scale, bool fractional, float destX, float destY)? ComposeInfo;

    public static void LoadFromPrefs(EditorPrefs prefs)
    {
        Prefs = prefs;
        ShowCone = prefs.DebugCone;
        ShowDeadzone = prefs.DebugDeadzone;
        ShowSortLines = prefs.DebugSortLines;
        ShowFrameGraph = prefs.DebugFrameGraph;
        ShowAudio = prefs.DebugAudio;
        ShowCameraTuning = prefs.DebugCameraTuning;
        ShowNav = prefs.DebugNav;

        var audio = Runtime.Audio.AudioManager.Instance;
        audio.Muted = prefs.AudioMuted;
        audio.MasterVolume = prefs.AudioMaster;
        if (prefs.AudioBuses is { Length: Runtime.Audio.AudioBusRules.Count })
            for (int i = 0; i < prefs.AudioBuses.Length; i++)
                audio.SetBusVolume((Runtime.Audio.AudioBus)i, prefs.AudioBuses[i]);
    }

    private static void SavePrefs()
    {
        if (Prefs == null) return;
        Prefs.DebugCone = ShowCone;
        Prefs.DebugDeadzone = ShowDeadzone;
        Prefs.DebugSortLines = ShowSortLines;
        Prefs.DebugFrameGraph = ShowFrameGraph;
        Prefs.DebugAudio = ShowAudio;
        Prefs.DebugCameraTuning = ShowCameraTuning;
        Prefs.DebugNav = ShowNav;
        Prefs.Save();
    }

    private static void SaveVolumePrefs()
    {
        if (Prefs == null) return;
        var audio = Runtime.Audio.AudioManager.Instance;
        Prefs.AudioMaster = audio.MasterVolume;
        var buses = new float[Runtime.Audio.AudioBusRules.Count];
        for (int i = 0; i < buses.Length; i++)
            buses[i] = audio.GetBusVolume((Runtime.Audio.AudioBus)i);
        Prefs.AudioBuses = buses;
        Prefs.Save();
    }

    private const int FrameSamples = 120;
    private static readonly float[] _frameMs = new float[FrameSamples];
    private static int _frameHead;

    public static void Draw(EditorState state, Runtime.Core.Camera? camera,
        Func<XnaV2, XnaV2>? worldToScreen = null, float uiScale = 0f)
    {
        if (!state.ShowDebugOverlay || state.CurrentScene == null) return;

        var io = ImGui.GetIO();
        _frameMs[_frameHead] = io.DeltaTime * 1000f;
        _frameHead = (_frameHead + 1) % FrameSamples;

        var scene = state.CurrentScene;
        var player = scene.FindPlayer();
        var pt = player?.GetComponent<Transform>();
        var interactor = player?.GetComponent<Interactor>();

        if (worldToScreen != null && uiScale > 0f)
        {
            var dl = ImGui.GetForegroundDrawList();
            if (ShowCone && pt != null && interactor != null)
                DrawInteractionCone(dl, worldToScreen, uiScale, pt, interactor);
            if (state.ShowColliders)
                DrawColliders(dl, worldToScreen, scene);
            if (ShowDeadzone && camera != null)
                DrawDeadzone(dl, worldToScreen, uiScale, camera);
            if (ShowSortLines)
                DrawSortLines(dl, worldToScreen, uiScale, scene);
            if (ShowNav)
                DrawNavGrid(dl, worldToScreen, scene);
        }

        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(vp.Pos.X + 10, vp.Pos.Y + 34));
        ImGui.SetNextWindowBgAlpha(0.62f);
        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove;

        if (!ImGui.Begin("##debugOverlay", flags)) { ImGui.End(); return; }

        bool changed = false;
        changed |= ImGui.Checkbox("Cone", ref ShowCone); ImGui.SameLine();
        bool col = state.ShowColliders;
        if (ImGui.Checkbox("Colliders", ref col)) state.ShowColliders = col;
        ImGui.SameLine();
        changed |= ImGui.Checkbox("Dead zone", ref ShowDeadzone); ImGui.SameLine();
        changed |= ImGui.Checkbox("Sort lines", ref ShowSortLines); ImGui.SameLine();
        changed |= ImGui.Checkbox("Graph", ref ShowFrameGraph); ImGui.SameLine();
        changed |= ImGui.Checkbox("Audio", ref ShowAudio); ImGui.SameLine();
        changed |= ImGui.Checkbox("Camera", ref ShowCameraTuning); ImGui.SameLine();
        changed |= ImGui.Checkbox("Nav", ref ShowNav);
        if (changed) SavePrefs();

        if (camera != null)
        {
            ImGui.SameLine(); ImGui.TextDisabled("|"); ImGui.SameLine();
            bool pp = camera.PixelPerfect;
            if (ImGui.Checkbox("PP", ref pp)) camera.PixelPerfect = pp;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Pixel perfect - session only (a restart always turns it back on)");
        }

        if (worldToScreen == null && (ShowCone || state.ShowColliders || ShowDeadzone || ShowSortLines || ShowNav))
            ImGui.TextDisabled("(world gizmos appear in the game view, `)");

        ImGui.PushFont(ImGuiRenderer.MonoFont);

        Header("FRAME");
        ImGui.Text($"{io.Framerate,5:0.0} fps  ({1000f / MathF.Max(io.Framerate, 0.01f):0.00} ms)");
        if (ShowFrameGraph)
        {
            ImGui.PlotLines("##frameGraph", ref _frameMs[0], FrameSamples, _frameHead,
                null, 0f, 33.3f, new Vector2(220, 36));
        }

        if (player != null && pt != null)
        {
            Header("PLAYER");
            ImGui.Text($"pos  {pt.Position.X,7:0.0}, {pt.Position.Y,7:0.0}   tile ({(int)(pt.Position.X / 16)},{(int)(pt.Position.Y / 16)})");

            var rb = player.GetComponent<Rigidbody2D>();
            if (rb != null)
                ImGui.Text($"vel  {rb.Velocity.X,7:0.0}, {rb.Velocity.Y,7:0.0}   |v| {rb.Velocity.Length():0.0} px/s");

            var anim = player.GetComponent<Animator>();
            if (anim != null)
                ImGui.Text($"anim {anim.CurrentClip?.Name ?? "(none)"}{(anim.IsPlaying ? "" : "  [paused]")}");

            if (interactor != null)
            {
                Header("INTERACT");
                ImGui.Text($"facing {Dir(interactor.Facing)} ({interactor.Facing.X:0.00},{interactor.Facing.Y:0.00})   reach {interactor.Reach:0}px  cone {interactor.FacingDot:0.00}");

                var cur = interactor.Current;
                var tt = cur?.Entity.GetComponent<Transform>();
                if (cur != null && tt != null)
                {
                    float dist = (tt.Position - pt.Position).Length();
                    ImGui.TextColored(EditorTheme.Accent,
                        $"target {cur.Entity.Name} \"{cur.Prompt}\"   dist {dist:0.0} / {interactor.Reach + cur.Range:0.0}px");
                }
                else
                {
                    ImGui.TextDisabled("target (none)");
                }
            }
        }

        if (camera != null)
        {
            Header("CAMERA");
            ImGui.Text($"pos  {camera.Position.X,7:0.0}, {camera.Position.Y,7:0.0}   zoom {camera.Zoom:0.00}{(camera.PixelPerfect ? "  [PP]" : "")}");
            if (camera.Deadzone != XnaV2.Zero)
                ImGui.Text($"deadzone {camera.Deadzone.X:0}x{camera.Deadzone.Y:0}px");

            if (ShowCameraTuning) DrawCameraTuning(camera);
        }

        if (ComposeInfo is { } ci)
        {
            Header("RENDER");
            bool integerScale = MathF.Abs(ci.scale - MathF.Round(ci.scale)) < 1e-4f;
            ImGui.Text($"window {ci.vw}x{ci.vh}  canvas {ci.canvasW}x{ci.canvasH}  (ViewZoom {(float)ci.canvasW / 320f:0.00})");
            ImGui.TextColored(integerScale ? EditorTheme.Accent : new Vector4(1f, 0.6f, 0.3f, 1f),
                $"scale {ci.scale:0.000}  {(integerScale ? "[integer - filter is a no-op]" : "[fractional - PixZoom interpolates]")}"
                + $"  letterbox {ci.destX:0}, {ci.destY:0}");
        }

        Header("SCENE");
        ImGui.Text($"{scene.Name}   entities {scene.Entities.Count}   {(state.IsPlayMode ? "PLAY" : "EDIT")}");

        if (ShowAudio) DrawAudio(state);

        ImGui.PopFont();
        ImGui.End();
    }

    private static void DrawCameraTuning(Runtime.Core.Camera camera)
    {
        ImGui.Separator();

        ImGui.SetNextItemWidth(150);
        float k = camera.FollowSpeed;
        if (ImGui.SliderFloat("FollowSpeed", ref k, 0.5f, 12f, "%.2f")) camera.FollowSpeed = k;
        ImGui.SameLine();
        ImGui.TextDisabled($"τ99 {4.605f / MathF.Max(k, 0.01f):0.00}s");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("99% settling time = 4.605/k.");

        ImGui.SetNextItemWidth(150);
        float dz = camera.Deadzone.X;
        if (ImGui.SliderFloat("Deadzone", ref dz, 0f, 48f, "%.0f px"))
            camera.Deadzone = new XnaV2(dz, dz);
        ImGui.SameLine();
        ImGui.TextDisabled(dz == 0f ? "(no dead zone)" : "X and Y together");

        ImGui.SetNextItemWidth(150);
        float sd = camera.SettleDeadband;
        if (ImGui.SliderFloat("Settle", ref sd, 0f, 12f, "%.1f px")) camera.SettleDeadband = sd;
        ImGui.SameLine();
        ImGui.TextDisabled(sd == 0f ? "(default - settling off)" : "residual from the edge");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("0 turns settling off (the default, an authoring decision).\nThe juddering tail acts as a braking signal, which is why this side was chosen.\nRaising it to 3-8 cuts the tail - at 3 it goes from 1883ms to 483ms.");

        ImGui.SetNextItemWidth(150);
        float oy = camera.FollowOffset.Y;
        if (ImGui.SliderFloat("FollowOffsetY", ref oy, -48f, 12f, "%.0f px"))
            camera.FollowOffset = new XnaV2(camera.FollowOffset.X, MathF.Round(oy));
        ImGui.SameLine();
        ImGui.TextDisabled(oy == -18f ? "(default - body centre)" : oy == 0f ? "(the feet)" : "above (-) or below (+) the feet");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The follow target is the feet plus this value. -18 is the centre of a 34-36px body; -24 (the cell centre) passes through the shoulders.\n0 aims at the feet.\nIt snaps to integers.");

        bool rounding = Runtime.Core.Camera.StateRoundingInDeadzone;
        if (ImGui.Checkbox("State rounding", ref rounding))
            Runtime.Core.Camera.StateRoundingInDeadzone = rounding;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("On, the camera position snaps to integers inside the dead zone (the old behaviour).\nOff by default - keeping the fractional position makes the pan fine at screen-pixel resolution.\nWARNING: it does not affect the feel (confirmed by hand). Settle decides the judder.\nIt only differs when reversing direction (walking in a straight line never takes this branch).");

        bool subpixel = Game1.SubpixelPan;
        if (ImGui.Checkbox("Subpixel pan", ref subpixel))
            Game1.SubpixelPan = subpixel;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Off, the world is pinned to the game pixel grid (the pan quantum goes from a screen pixel to an art pixel).\nA direct test of the \"sliding on ice\" hypothesis.");

        ImGui.SameLine(); ImGui.TextDisabled("|"); ImGui.SameLine();
        if (ImGui.Checkbox("Show box", ref ShowDeadzone)) SavePrefs();

        if (ImGui.SmallButton("Defaults"))
        {
            camera.FollowDamping = Runtime.Core.Camera.DefaultFollowDamping;
            camera.Deadzone = new XnaV2(16f, 16f);
            camera.SettleDeadband = 0f;
            camera.FollowOffset = new XnaV2(0f, -18f);
            Runtime.Core.Camera.StateRoundingInDeadzone = false;
            Game1.SubpixelPan = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Back to the current code defaults (FollowSpeed 2.3, Deadzone 24, Settle 0, FollowOffsetY -18, rounding off, subpixel on)");

        ImGui.SameLine();
        if (ImGui.SmallButton("Print combination"))
            Console.WriteLine("[Camera] " + TuningLine(camera));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Prints one line to the console - copy it verbatim to report it");

        ImGui.TextDisabled(TuningLine(camera));
    }

    private static string TuningLine(Runtime.Core.Camera camera) =>
        $"FollowSpeed {camera.FollowSpeed:0.00} ({4.605f / MathF.Max(camera.FollowSpeed, 0.01f):0.00}s) · " +
        $"Deadzone {camera.Deadzone.X:0} · Settle {camera.SettleDeadband:0.#} · FollowOffsetY {camera.FollowOffset.Y:0} · " +
        $"rounding {(Runtime.Core.Camera.StateRoundingInDeadzone ? "on" : "off")} · " +
        $"subpixel {(Game1.SubpixelPan ? "on" : "off")}";

    private static readonly List<Runtime.Audio.AudioManager.PlayingInfo> _playing = new();
    private static readonly string[] BusNames = { "Music", "SFX", "Ambient", "Voice", "UI" };
    private static string[]? _bgmFiles;
    private static int _bgmPick;
    private static float _testFade = 1.5f;

    private static void DrawAudio(EditorState state)
    {
        var audio = Runtime.Audio.AudioManager.Instance;

        Header("AUDIO");

        bool muted = audio.Muted;
        if (ImGui.Checkbox("Mute (the same switch as the scene view toolbar)", ref muted))
        {
            audio.Muted = muted;
            if (Prefs != null) { Prefs.AudioMuted = muted; Prefs.Save(); }
        }

        ImGui.SetNextItemWidth(150);
        float master = audio.MasterVolume;
        if (ImGui.SliderFloat("master", ref master, 0f, 1f, "%.2f")) audio.MasterVolume = master;
        if (ImGui.IsItemDeactivatedAfterEdit()) SaveVolumePrefs();

        for (int i = 0; i < BusNames.Length; i++)
        {
            var bus = (Runtime.Audio.AudioBus)i;
            float v = audio.GetBusVolume(bus);
            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderFloat(BusNames[i], ref v, 0f, 1f, "%.2f")) audio.SetBusVolume(bus, v);
            if (ImGui.IsItemDeactivatedAfterEdit()) SaveVolumePrefs();
        }

        if (!audio.ListenerActive)
            ImGui.TextDisabled("(editing - audio stopped. Press ` for the game view, or play)");

        DrawFootstepProbe(state);

        ImGui.Spacing();
        ImGui.Text($"bgm  {audio.CurrentBGM ?? "(none)"}");
        if (audio.CurrentBGM != null)
            ImGui.Text($"     {(audio.IsBGMPlaying ? "playing" : "stopped")}  gain {audio.BGMGain:0.00}");

        audio.SnapshotPlaying(_playing);
        ImGui.Text($"tracked {_playing.Count}{(audio.SoloKey != null ? "   [SOLO]" : "")}");

        for (int i = 0; i < _playing.Count; i++)
        {
            var p = _playing[i];
            bool solo = audio.SoloKey == p.Key;
            var col = p.FadingOut ? new Vector4(1f, 1f, 1f, 0.45f)
                    : solo ? EditorTheme.Accent
                    : EditorTheme.Success;

            ImGui.PushID(i);
            ImGui.PushStyleColor(ImGuiCol.Text, col);
            if (ImGui.Selectable(
                    $"{(solo ? "◀" : " ")}{(p.Spatial ? "◉" : "▬")} {Trim(p.Key, 20),-20} {BusNames[(int)p.Bus],-7} " +
                    $"g{p.Gain:0.00} v{p.Volume:0.00}{(p.Spatial ? $" {PanTag(p.Pan)}" : "")}{(p.FadingOut ? " ↓" : "")}",
                    solo))
                audio.SoloKey = solo ? null : p.Key;
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(solo ? "Clear solo" : "Listen to this alone (solo)");
            ImGui.PopID();
        }
        if (_playing.Count == 0) ImGui.TextDisabled("  (none)");

        if (audio.SoloKey != null && ImGui.SmallButton("Clear solo")) audio.SoloKey = null;

        _bgmFiles ??= ScanBGM();
        if (_bgmFiles.Length > 0)
        {
            ImGui.Spacing();
            ImGui.SetNextItemWidth(190);
            ImGui.Combo("##bgmpick", ref _bgmPick, _bgmFiles, _bgmFiles.Length);
            ImGui.SetNextItemWidth(60);
            ImGui.DragFloat("fade", ref _testFade, 0.05f, 0f, 6f, "%.2fs");

            string path = "Audio/BGM/" + _bgmFiles[_bgmPick];
            if (ImGui.Button("PlayBGM")) audio.PlayBGM(path, _testFade);
            ImGui.SameLine();
            if (ImGui.Button("PlayOnce")) audio.PlayOnce(path, _testFade);
            ImGui.SameLine();
            if (ImGui.Button("Stop")) audio.StopBGM(_testFade);
        }
        else
        {
            ImGui.TextDisabled("  (Content/Audio/BGM is empty)");
        }
    }

    private static void DrawFootstepProbe(EditorState state)
    {
        var player = state.CurrentScene?.FindPlayer();
        var emitter = player?.GetComponent<Runtime.Components.FootstepEmitter>();
        if (emitter == null) return;

        ImGui.Spacing();

        var surface = emitter.CurrentSurface;
        if (surface == null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Accent);
            ImGui.Text("underfoot  (none) - silent footsteps");
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("No SurfaceArea covers this spot.\nAttach a trigger collider plus a Surface Area to the floor entity.");
            return;
        }

        var area = emitter.CurrentArea;
        string source = area != null ? $"area: {area.Entity.Name}" : "scene default";
        ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Success);
        ImGui.Text($"underfoot  {surface.Name}  ({source})");
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"volume {surface.Volume:0.00}  pitch {surface.Pitch:+0.000;-0.000;0}"
                           + $"  speed ×{surface.SpeedMultiplier:0.00}"
                           + (area != null ? $"\npriority {area.Priority}" : ""));

        if (emitter.LastStep is { } step)
            ImGui.TextDisabled($"      last {(step.Left ? "L" : "R")} {Trim(step.ClipPath, 26)}  "
                             + $"v{step.Volume:0.00} p{step.Pitch:+0.00;-0.00;0.00}  ({emitter.StepCount} steps)");
        else
            ImGui.TextDisabled("      nothing yet");
    }

    private static string[] ScanBGM()
    {
        try
        {
            const string dir = "Content/Audio/BGM";
            if (!System.IO.Directory.Exists(dir)) return Array.Empty<string>();
            var files = System.IO.Directory.GetFiles(dir, "*.ogg");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return Array.ConvertAll(files, System.IO.Path.GetFileNameWithoutExtension)!;
        }
        catch { return Array.Empty<string>(); }
    }

    private static string Trim(string s, int max)
        => s.Length <= max ? s : "…" + s[^(max - 1)..];

    private static string PanTag(float pan)
        => MathF.Abs(pan) < 0.05f ? "C" : (pan < 0 ? $"L{-pan:0.0}" : $"R{pan:0.0}");

    private static void DrawInteractionCone(ImDrawListPtr dl, Func<XnaV2, XnaV2> w2s,
        float uiScale, Transform pt, Interactor it)
    {
        var pw = w2s(pt.Position);
        var p = new Vector2(pw.X, pw.Y);
        float r = (it.Reach + 12f) * uiScale;
        float theta = MathF.Atan2(it.Facing.Y, it.Facing.X);
        float half = MathF.Acos(Math.Clamp(it.FacingDot, -1f, 1f));

        uint amber = ImGui.GetColorU32(EditorTheme.Accent with { W = 0.9f });
        uint amberDim = ImGui.GetColorU32(EditorTheme.Accent with { W = 0.25f });

        dl.PathLineTo(p);
        dl.PathArcTo(p, r, theta - half, theta + half, 28);
        dl.PathFillConvex(amberDim);
        dl.PathLineTo(p);
        dl.PathArcTo(p, r, theta - half, theta + half, 28);
        dl.PathLineTo(p);
        dl.PathStroke(amber, ImDrawFlags.None, 1.5f);

        var dir = new Vector2(MathF.Cos(theta), MathF.Sin(theta));
        var tip = p + dir * (r * 0.55f);
        dl.AddLine(p, tip, amber, 2f);
        var side = new Vector2(-dir.Y, dir.X);
        dl.AddTriangleFilled(tip + dir * 8f, tip + side * 5f, tip - side * 5f, amber);

        var cur = it.Current;
        var tt = cur?.Entity.GetComponent<Transform>();
        if (cur != null && tt != null)
        {
            uint green = ImGui.GetColorU32(EditorTheme.Success);
            var tw = w2s(tt.Position);
            var t = new Vector2(tw.X, tw.Y);
            dl.AddLine(p, t, green, 2f);

            var sizeW = VisualSize(cur.Entity);
            var halfPx = new Vector2(sizeW.X, sizeW.Y) * uiScale * 0.5f;
            dl.AddRect(t - halfPx, t + halfPx, green, 2f, ImDrawFlags.None, 2f);
        }
    }

    private static void DrawNavGrid(ImDrawListPtr dl, Func<XnaV2, XnaV2> w2s, Runtime.Core.Scene scene)
    {
        var grid = Runtime.Nav.Nav.For(scene);
        if (grid.IsEmpty) return;

        uint blocked = ImGui.GetColorU32(new Vector4(1f, 0.25f, 0.25f, 0.28f));
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                var c = new Microsoft.Xna.Framework.Point(x, y);
                var mn = w2s(grid.CellCenter(c) - new XnaV2(Runtime.Nav.NavGrid.CellSize * 0.5f));
                var mx = w2s(grid.CellCenter(c) + new XnaV2(Runtime.Nav.NavGrid.CellSize * 0.5f));
                var a = new Vector2(mn.X, mn.Y);
                var b = new Vector2(mx.X, mx.Y);

                if (grid.IsBlocked(x, y)) dl.AddRectFilled(a, b, blocked);
                else dl.AddRectFilled(a, b, RegionColor(grid.RegionOf(c)));
            }
        }

        DrawChasePaths(dl, w2s, scene);
    }

    private static void DrawChasePaths(ImDrawListPtr dl, Func<XnaV2, XnaV2> w2s, Runtime.Core.Scene scene)
    {
        var target = scene.FindPlayer()?.GetComponent<Transform>()?.Position;
        uint line = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.95f));
        uint sight = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.22f));
        var points = new List<XnaV2>();

        foreach (var entity in scene.Entities)
        {
            var chaser = entity.GetComponent<Gameplay.Combat.Slime>();
            var from = entity.GetComponent<Transform>()?.Position;
            if (chaser == null || from == null) continue;

            dl.AddCircle(Screen(w2s, from.Value), WorldToPixels(w2s, from.Value, chaser.SightRange), sight, 0, 1f);
            if (!chaser.IsChasing) continue;

            points.Clear();
            points.Add(from.Value);
            for (int i = chaser.PathIndex; i < chaser.Path.Count; i++) points.Add(chaser.Path[i]);
            if (target is { } goal) points.Add(goal);

            for (int i = 0; i + 1 < points.Count; i++)
                dl.AddLine(Screen(w2s, points[i]), Screen(w2s, points[i + 1]), line, 2f);
            for (int i = 1; i + 1 < points.Count; i++)
                dl.AddCircleFilled(Screen(w2s, points[i]), 3f, line);
        }
    }

    private static Vector2 Screen(Func<XnaV2, XnaV2> w2s, XnaV2 world)
    {
        var p = w2s(world);
        return new Vector2(p.X, p.Y);
    }

    private static float WorldToPixels(Func<XnaV2, XnaV2> w2s, XnaV2 centre, float distance)
    {
        var a = w2s(centre);
        var b = w2s(centre + new XnaV2(distance, 0f));
        return MathF.Abs(b.X - a.X);
    }

    private static uint RegionColor(int id)
    {
        if (id == 0) return 0;
        float h = (id * 0.61803f) % 1f;
        float f = h * 6f, k = f - MathF.Floor(f);
        float q = 1f - 0.65f * k, t = 1f - 0.65f * (1f - k), p = 1f - 0.65f;
        (float r, float g, float b) c = ((int)f % 6) switch
        {
            0 => (1f, t, p), 1 => (q, 1f, p), 2 => (p, 1f, t),
            3 => (p, q, 1f), 4 => (t, p, 1f), _ => (1f, p, q),
        };
        return ImGui.GetColorU32(new Vector4(c.r, c.g, c.b, 0.16f));
    }

    private static void DrawColliders(ImDrawListPtr dl, Func<XnaV2, XnaV2> w2s, Runtime.Core.Scene scene)
    {
        uint green = ImGui.GetColorU32(new Vector4(0.31f, 1f, 0.47f, 0.85f));
        foreach (var e in scene.Entities)
        {
            if (!e.ActiveInHierarchy) continue;
            var col = e.GetComponent<Collider2D>();
            if (col == null || !col.Enabled) continue;

            switch (col)
            {
                case CircleCollider2D circle:
                {
                    var cw = w2s(circle.Center);
                    var rw = w2s(circle.Center + new XnaV2(circle.Radius, 0));
                    dl.AddCircle(new Vector2(cw.X, cw.Y), MathF.Abs(rw.X - cw.X), green, 24);
                    break;
                }
                case CapsuleCollider2D cap:
                {
                    var (sa, sb) = cap.GetSegment();
                    var aw = w2s(sa); var bw = w2s(sb);
                    var rw = w2s(sa + new XnaV2(cap.Radius, 0));
                    float r = MathF.Abs(rw.X - aw.X);
                    var a = new Vector2(aw.X, aw.Y); var b2 = new Vector2(bw.X, bw.Y);
                    var dir = b2 - a;
                    if (dir.LengthSquared() < 1e-4f)
                    {
                        dl.AddCircle(a, r, green, 24);
                        break;
                    }
                    float ang = MathF.Atan2(dir.Y, dir.X);
                    dl.PathArcTo(b2, r, ang - MathF.PI / 2f, ang + MathF.PI / 2f, 12);
                    dl.PathArcTo(a, r, ang + MathF.PI / 2f, ang + 3f * MathF.PI / 2f, 12);
                    dl.PathStroke(green, ImDrawFlags.Closed, 1f);
                    break;
                }
                default:
                {
                    var b = col.GetBounds();
                    var mn = w2s(new XnaV2(b.Min.X, b.Min.Y));
                    var mx = w2s(new XnaV2(b.Min.X + b.Width, b.Min.Y + b.Height));
                    dl.AddRect(new Vector2(mn.X, mn.Y), new Vector2(mx.X, mx.Y), green);
                    break;
                }
            }
        }
    }

    private static void DrawDeadzone(ImDrawListPtr dl, Func<XnaV2, XnaV2> w2s,
        float uiScale, Runtime.Core.Camera cam)
    {
        uint cyan = ImGui.GetColorU32(new Vector4(0.35f, 0.8f, 1f, 0.9f));
        var cw = w2s(cam.Position);
        var c = new Vector2(cw.X, cw.Y);

        dl.AddLine(c - new Vector2(6, 0), c + new Vector2(6, 0), cyan, 1.5f);
        dl.AddLine(c - new Vector2(0, 6), c + new Vector2(0, 6), cyan, 1.5f);

        if (cam.Deadzone != XnaV2.Zero)
        {
            var half = new Vector2(cam.Deadzone.X, cam.Deadzone.Y) * uiScale;
            dl.AddRect(c - half, c + half, cyan, 0f, ImDrawFlags.None, 1.5f);
        }
    }

    private static void DrawSortLines(ImDrawListPtr dl, Func<XnaV2, XnaV2> w2s,
        float uiScale, Runtime.Core.Scene scene)
    {
        uint magenta = ImGui.GetColorU32(new Vector4(1f, 0.4f, 0.85f, 0.9f));
        foreach (var e in scene.Entities)
        {
            if (!e.ActiveInHierarchy) continue;
            var t = e.GetComponent<Transform>();
            if (t == null) continue;

            var sr = e.GetComponent<SpriteRenderer>();
            if (sr == null || !sr.Enabled) continue;
            float sortY = sr.SortY;
            float halfW = sr.GetDrawSize().X / 2f;
            if (halfW <= 0f) continue;

            var a = w2s(new XnaV2(t.Position.X - halfW, sortY));
            var b = w2s(new XnaV2(t.Position.X + halfW, sortY));
            dl.AddLine(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), magenta, 1.5f);
        }
    }

    private static XnaV2 VisualSize(Runtime.Core.Entity e)
    {
        var sr = e.GetComponent<SpriteRenderer>();
        if (sr != null) { var s = sr.GetDrawSize(); if (s != XnaV2.Zero) return s; }
        var col = e.GetComponent<Collider2D>();
        if (col != null)
        {
            var b = col.GetBounds();
            return new XnaV2(b.Width, b.Height);
        }
        return new XnaV2(16, 16);
    }

    private static void Header(string label)
    {
        ImGui.Spacing();
        ImGui.TextColored(EditorTheme.Accent with { W = 0.85f }, label);
    }

    private static string Dir(XnaV2 f) =>
        MathF.Abs(f.X) > MathF.Abs(f.Y)
            ? (f.X > 0 ? "R >" : "L <")
            : (f.Y > 0 ? "D v" : "U ^");
}
