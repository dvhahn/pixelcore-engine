using System;
using ImGuiNET;
using PixelCore.Editor.Panels;

namespace PixelCore.Editor;

public class RoomDocument
{
    private readonly EditorState _state;
    private readonly HierarchyPanel _hierarchy;
    private readonly InspectorPanel _inspector;
    private readonly SceneViewPanel _sceneView;
    private readonly EditorPrefs _prefs;

    private float _hierRatio;

    public enum DocResult { None, Activate, Close }

    public RoomDocument(EditorState state, HierarchyPanel hierarchy,
        InspectorPanel inspector, SceneViewPanel sceneView, EditorPrefs prefs)
    {
        _state = state;
        _hierarchy = hierarchy;
        _inspector = inspector;
        _sceneView = sceneView;
        _prefs = prefs;
        _hierRatio = System.Math.Clamp(prefs.SideSplitRatio, 0.15f, 0.85f);
    }

    public DocResult Draw(uint dockId, SceneDoc doc, bool isActive)
    {
        EditorWidgets.DocWindowClass();
        ImGui.SetNextWindowDockID(dockId, ImGuiCond.Always);

        var flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar
                  | ImGuiWindowFlags.NoScrollWithMouse;
        if (isActive ? _state.IsDirty : doc.Dirty)
            flags |= ImGuiWindowFlags.UnsavedDocument;

        bool isPrefab = doc.Scene.Kind == PixelCore.Runtime.Core.SceneKind.Prefab;
        string title = $"{(isPrefab ? Icons.Cubes : Icons.Cube)}  {doc.Scene.Name}###RoomDoc_{doc.Id}";

        var result = DocResult.None;
        bool open = true;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, System.Numerics.Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, EditorWidgets.DocTabPadding);
        bool visible = ImGui.Begin(title, ref open, flags);
        ImGui.PopStyleVar(2);
        if (!open) result = DocResult.Close;

        if (visible)
        {
            if (!isActive)
            {
                if (result == DocResult.None) result = DocResult.Activate;
            }
            else
            {
                ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, System.Numerics.Vector2.Zero);
                if (ImGui.BeginTable("room_layout", 2,
                        ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
                {
                    ImGui.TableSetupColumn("side", ImGuiTableColumnFlags.WidthFixed, 260f);
                    ImGui.TableSetupColumn("view", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableNextRow();
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0,
                        ImGui.GetColorU32(ImGui.GetStyle().Colors[(int)ImGuiCol.MenuBarBg]));

                    ImGui.TableNextColumn();
                    DrawSideColumn();

                    ImGui.TableNextColumn();
                    DrawViewport();

                    ImGui.EndTable();
                }
                ImGui.PopStyleVar();
            }
        }
        ImGui.End();
        return result;
    }

    private void DrawSideColumn()
    {
        float avail = ImGui.GetContentRegionAvail().Y;
        float hierHeight = System.Math.Max(60f, avail * _hierRatio);

        var padding = new System.Numerics.Vector2(8, 6);
        var cardBg = ImGui.GetStyle().Colors[(int)ImGuiCol.MenuBarBg];

        ImGui.PushStyleColor(ImGuiCol.ChildBg, cardBg);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
        ImGui.BeginChild("room_hierarchy", new System.Numerics.Vector2(0, hierHeight),
            ImGuiChildFlags.AlwaysUseWindowPadding);
        EditorWidgets.PanelTitle(Icons.Sitemap, "Hierarchy", EditorWidgets.PanelFocused());
        using (FrameProf.Measure("    ui.hierarchy")) _hierarchy.DrawContent();
        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();

        ImGui.InvisibleButton("##side_split", new System.Numerics.Vector2(-1, 12));
        bool splitHovered = ImGui.IsItemHovered();
        bool splitActive = ImGui.IsItemActive();
        if (splitHovered || splitActive) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);
        if (splitActive && avail > 1)
        {
            _hierRatio += ImGui.GetIO().MouseDelta.Y / avail;
            _hierRatio = System.Math.Clamp(_hierRatio, 0.15f, 0.85f);
        }
        if (ImGui.IsItemDeactivated() && System.MathF.Abs(_prefs.SideSplitRatio - _hierRatio) > 0.001f)
        {
            _prefs.SideSplitRatio = _hierRatio;
            _prefs.Save();
        }
        {
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            float midY = MathF.Round((min.Y + max.Y) * 0.5f);
            ImGui.GetWindowDrawList().AddLine(
                new System.Numerics.Vector2(min.X + 8, midY),
                new System.Numerics.Vector2(max.X - 8, midY),
                ImGui.GetColorU32((splitHovered || splitActive)
                    ? EditorTheme.Accent : ImGui.GetStyle().Colors[(int)ImGuiCol.Border]), 1f);
        }

        ImGui.PushStyleColor(ImGuiCol.ChildBg, cardBg);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
        ImGui.BeginChild("room_inspector", new System.Numerics.Vector2(0, 0),
            ImGuiChildFlags.AlwaysUseWindowPadding);
        EditorWidgets.PanelTitle(Icons.Gear, "Inspector", EditorWidgets.PanelFocused());
        ImGui.PushStyleColor(ImGuiCol.Header, new System.Numerics.Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, EditorTheme.ItemBg);
        using (FrameProf.Measure("    ui.inspector")) _inspector.DrawContent();
        ImGui.PopStyleColor(2);
        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private void DrawViewport()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, System.Numerics.Vector2.Zero);
        ImGui.BeginChild("room_viewport", new System.Numerics.Vector2(0, 0),
            ImGuiChildFlags.None,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        using (FrameProf.Measure("    ui.sceneview")) _sceneView.DrawContent();
        ImGui.EndChild();
        ImGui.PopStyleVar();
    }
}
