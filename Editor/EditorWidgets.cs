using System;
using System.Numerics;
using ImGuiNET;

namespace PixelCore.Editor;

public static class EditorWidgets
{
    public static bool SliderFloat(string label, ref float value, float min, float max, int decimals = 2)
    {
        float frameW = ImGui.CalcItemWidth();
        var pos = ImGui.GetCursorScreenPos();
        float frameH = ImGui.GetFrameHeight();

        var transparent = new Vector4(0, 0, 0, 0);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, transparent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, transparent);
        bool changed = ImGui.SliderFloat(label, ref value, min, max, "");
        ImGui.PopStyleColor(2);

        bool hovered = ImGui.IsItemHovered();
        bool active = ImGui.IsItemActive();

        var dl = ImGui.GetWindowDrawList();
        float t = max > min ? Math.Clamp((value - min) / (max - min), 0f, 1f) : 0f;
        float rounding = Math.Max(0f, ImGui.GetStyle().FrameRounding - 1f);
        var a = EditorTheme.Accent;

        float fillW = t * (frameW - 4f);
        if (fillW > 1f)
        {
            float alpha = active ? 0.55f : hovered ? 0.45f : 0.35f;
            dl.AddRectFilled(
                new Vector2(pos.X + 2f, pos.Y + 2f),
                new Vector2(pos.X + 2f + fillW, pos.Y + frameH - 2f),
                ImGui.GetColorU32(new Vector4(a.X, a.Y, a.Z, alpha)), rounding);
        }

        float edgeX = pos.X + 2f + fillW;
        dl.AddRectFilled(
            new Vector2(edgeX - 1f, pos.Y + 3f),
            new Vector2(edgeX + 1f, pos.Y + frameH - 3f),
            ImGui.GetColorU32(a));

        string txt = value.ToString("F" + decimals);
        var ts = ImGui.CalcTextSize(txt);
        dl.AddText(new Vector2(pos.X + frameW - ts.X - 7f, pos.Y + (frameH - ts.Y) * 0.5f),
            ImGui.GetColorU32(ImGuiCol.Text), txt);

        return changed;
    }

    public static void AxisTint()
    {
        var min = ImGui.GetItemRectMin();
        float frameH = ImGui.GetFrameHeight();
        float innerX = ImGui.GetStyle().ItemInnerSpacing.X;
        float w = ImGui.GetItemRectSize().X;
        float sub = MathF.Floor((w - innerX) / 2f);

        var dl = ImGui.GetWindowDrawList();
        uint cx = ImGui.GetColorU32(EditorTheme.AxisX);
        uint cy = ImGui.GetColorU32(EditorTheme.AxisY);
        DrawAxisBar(dl, new Vector2(min.X, min.Y), frameH, cx);
        DrawAxisBar(dl, new Vector2(min.X + sub + innerX, min.Y), frameH, cy);
    }

    public static void AxisTintOne(Vector4 color)
    {
        var min = ImGui.GetItemRectMin();
        DrawAxisBar(ImGui.GetWindowDrawList(), min, ImGui.GetFrameHeight(), ImGui.GetColorU32(color));
    }

    private static void DrawAxisBar(ImDrawListPtr dl, Vector2 frameMin, float frameH, uint color)
    {
        dl.AddRectFilled(
            new Vector2(frameMin.X + 2f, frameMin.Y + 3f),
            new Vector2(frameMin.X + 4.5f, frameMin.Y + frameH - 3f),
            color, 1.5f);
    }

    public static void PanelTitle(string icon, string label, bool focused = false)
    {
        ImGui.PushFont(ImGuiRenderer.SmallFont);
        if (focused) ImGui.TextColored(EditorTheme.Accent, icon);
        else ImGui.TextDisabled(icon);
        ImGui.SameLine(0, 6);
        ImGui.TextDisabled(label);
        ImGui.PopFont();
        ImGui.Dummy(new Vector2(0, 2));
    }

    public static bool PanelFocused()
        => ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows
                               | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);

    public static readonly Vector2 DocTabPadding = new(12f, 6f);

    public static void PushSelectionBg(float height = 0f)
    {
        var min = ImGui.GetCursorScreenPos();
        float h = height > 0f ? height : ImGui.GetFrameHeight();
        ImGui.GetWindowDrawList().AddRectFilled(min,
            new Vector2(min.X + ImGui.GetContentRegionAvail().X, min.Y + h),
            ImGui.GetColorU32(EditorTheme.AccentSoft), 6f);
        var clear = new Vector4(0, 0, 0, 0);
        ImGui.PushStyleColor(ImGuiCol.Header, clear);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, clear);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, clear);
    }

    public static void PopSelectionBg() => ImGui.PopStyleColor(3);

    public static bool TinyCheckbox(string id, ref bool value, float size = 14f)
    {
        var pos = ImGui.GetCursorScreenPos();
        float lift = MathF.Floor((ImGui.GetFrameHeight() - size) * 0.5f);
        ImGui.InvisibleButton(id, new Vector2(size, ImGui.GetFrameHeight()));
        bool changed = ImGui.IsItemClicked();
        if (changed) value = !value;

        var min = new Vector2(pos.X, pos.Y + lift);
        var max = new Vector2(pos.X + size, pos.Y + lift + size);
        var dl = ImGui.GetWindowDrawList();
        bool hovered = ImGui.IsItemHovered();
        float r = MathF.Min(3f, size * 0.25f);

        dl.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.FrameBg), r);
        dl.AddRect(min, max, ImGui.GetColorU32(hovered ? ImGuiCol.Text : ImGuiCol.Border), r,
            ImDrawFlags.None, 1f);

        if (value)
        {
            dl.PathLineTo(new Vector2(min.X + size * 0.24f, min.Y + size * 0.52f));
            dl.PathLineTo(new Vector2(min.X + size * 0.42f, min.Y + size * 0.72f));
            dl.PathLineTo(new Vector2(min.X + size * 0.78f, min.Y + size * 0.30f));
            dl.PathStroke(ImGui.GetColorU32(EditorTheme.Accent),
                ImDrawFlags.None, MathF.Max(1.7f, size * 0.14f));
        }
        return changed;
    }

    public static bool KebabButton(string id, float size = 16f)
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, new Vector2(size, ImGui.GetFrameHeight()));
        bool clicked = ImGui.IsItemClicked();
        bool hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        uint col = ImGui.GetColorU32(hovered ? ImGuiCol.Text : ImGuiCol.TextDisabled);
        float cx = pos.X + size * 0.5f;
        float cy = pos.Y + ImGui.GetFrameHeight() * 0.5f;
        float gap = MathF.Max(3f, size * 0.22f);
        for (int i = -1; i <= 1; i++)
            dl.AddCircleFilled(new Vector2(cx, cy + i * gap), 1.35f, col, 8);
        return clicked;
    }

    public static bool SoftShadow = false;

    public static void DropShadow(ImDrawListPtr dl, Vector2 min, Vector2 max, float rounding)
    {
        if (!SoftShadow)
        {
            dl.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.TableBorderStrong),
                rounding, ImDrawFlags.None, 1f);
            return;
        }

        for (int i = 0; i < 4; i++)
        {
            float grow = 2f + i * 3f;
            float alpha = 0.09f - i * 0.02f;
            dl.AddRectFilled(
                new Vector2(min.X - grow, min.Y - grow + 3f),
                new Vector2(max.X + grow, max.Y + grow + 5f),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, alpha)), rounding + grow);
        }
    }

    public static unsafe void DocWindowClass()
    {
        var wc = new ImGuiWindowClass
        {
            DockNodeFlagsOverrideSet = (ImGuiDockNodeFlags)(1 << 15),
            DockingAllowUnclassed = 1,
        };
        ImGui.SetNextWindowClass(&wc);
    }

    public static void FieldLabel(string label)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(label);
        ImGui.SameLine(108f);
        ImGui.SetNextItemWidth(-1);
    }

    public static string EllipsizeHead(string text, float maxW)
    {
        if (maxW <= 0f || text.Length == 0) return "";
        if (ImGui.CalcTextSize(text).X <= maxW) return text;

        string dots = HasGlyph('…') ? "…" : "...";

        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (ImGui.CalcTextSize(dots + text.Substring(text.Length - mid)).X <= maxW) lo = mid;
            else hi = mid - 1;
        }
        return lo <= 0 ? "" : dots + text.Substring(text.Length - lo);
    }

    public static string EllipsizeHeadPath(string path, float maxW)
    {
        if (maxW <= 0f || path.Length == 0) return "";
        if (ImGui.CalcTextSize(path).X <= maxW) return path;
        string dots = HasGlyph('…') ? "…" : "...";
        for (int i = 0; i < path.Length; i++)
        {
            if (path[i] != '/') continue;
            var cand = dots + path.Substring(i);
            if (ImGui.CalcTextSize(cand).X <= maxW) return cand;
        }
        return EllipsizeHead(path, maxW);
    }

    public static unsafe bool HasGlyph(char c)
    {
        var glyph = ImGui.GetFont().FindGlyphNoFallback(c);
        return glyph.NativePtr != null;
    }

    public static void Hint(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }
}
