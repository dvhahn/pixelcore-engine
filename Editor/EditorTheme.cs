using System;
using System.Numerics;
using ImGuiNET;

namespace PixelCore.Editor;

public enum ThemeMode { Dark, Light }

public static class EditorTheme
{
    public static ThemeMode Mode = ThemeMode.Dark;

    public static bool PlayDim { get; private set; }

    public static Vector4 Accent { get; private set; }
    public static Vector4 AccentSoft { get; private set; }
    public static Vector4 Success { get; private set; }
    public static Vector4 Danger { get; private set; }
    public static Vector4 DangerHover { get; private set; }
    public static Vector4 ItemBg { get; private set; }
    public static Vector4 ItemHoverBg { get; private set; }
    public static Vector4 Recessed { get; private set; }
    public static Vector4 ViewportClear { get; private set; }
    public static readonly Vector4 ViewportBackdrop = new(0.220f, 0.221f, 0.227f, 1f);
    public static Vector4 AxisX { get; private set; }
    public static Vector4 AxisY { get; private set; }
    public static Vector4 Prefab => AssetKinds.Color(AssetKind.Prefab);

    public static void SetPlayDim(bool on)
    {
        if (PlayDim == on) return;
        PlayDim = on;
        Apply();
    }

    public static void Apply()
    {
        var style = ImGui.GetStyle();
        var colors = style.Colors;

        style.WindowRounding = 8f;
        style.ChildRounding = 0f;
        style.FrameRounding = 6f;
        style.PopupRounding = 8f;
        style.ScrollbarRounding = 5f;
        style.GrabRounding = 5f;
        style.TabRounding = 6f;

        style.AntiAliasedLines = true;
        style.AntiAliasedLinesUseTex = true;
        style.AntiAliasedFill = true;

        style.WindowPadding = new Vector2(10, 8);
        style.FramePadding = new Vector2(8, 4);
        style.CellPadding = new Vector2(6, 3);
        style.ItemSpacing = new Vector2(6, 4);
        style.ItemInnerSpacing = new Vector2(6, 4);
        style.IndentSpacing = 15f;
        style.ScrollbarSize = 9f;
        style.GrabMinSize = 10f;

        style.WindowBorderSize = 1f;
        style.PopupBorderSize = 1f;
        style.FrameBorderSize = 0f;
        style.TabBorderSize = 0f;
        style.WindowTitleAlign = new Vector2(0.0f, 0.5f);

        Vector4 winBg, panelBg, recessed, inputBg, item, itemHover;
        Vector4 accent, accentHover, accentActive, accentDim;
        Vector4 text, textDim, border, borderStrong, rowAlt, tableBorderLight;
        Vector4 resizeGrip, resizeGripHover, textSelected, modalDim, navHighlight;

        if (Mode == ThemeMode.Light)
        {
            winBg = panelBg  = Hex(0.953f, 0.953f, 0.957f);
            recessed         = Hex(0.922f, 0.925f, 0.929f);
            inputBg          = Hex(0.922f, 0.925f, 0.929f);
            item             = Hex(0.878f, 0.882f, 0.890f);
            itemHover        = Hex(0.827f, 0.831f, 0.843f);
            text             = Hex(0.180f, 0.192f, 0.212f);
            textDim          = Hex(0.357f, 0.373f, 0.404f);
            borderStrong     = Hex(0.769f, 0.776f, 0.792f);
            border           = Hex(0.863f, 0.867f, 0.878f);

            accent           = Hex(0.561f, 0.329f, 0.098f);
            accentHover      = Hex(0.612f, 0.380f, 0.122f);
            accentActive     = Hex(0.467f, 0.267f, 0.059f);
            accentDim        = new Vector4(0.561f, 0.329f, 0.098f, 0.55f);

            rowAlt           = new Vector4(0f, 0f, 0f, 0.030f);
            tableBorderLight = borderStrong;
            resizeGrip       = new Vector4(0.42f, 0.44f, 0.47f, 0.22f);
            resizeGripHover  = new Vector4(0.30f, 0.32f, 0.35f, 0.55f);
            textSelected     = new Vector4(0.561f, 0.329f, 0.098f, 0.26f);
            modalDim         = new Vector4(0.10f, 0.11f, 0.13f, 0.40f);
            navHighlight     = new Vector4(0.16f, 0.17f, 0.20f, 0.70f);
        }
        else
        {
            winBg = panelBg  = Hex(0.110f, 0.114f, 0.122f);
            recessed         = Hex(0.078f, 0.082f, 0.090f);
            inputBg          = Hex(0.078f, 0.082f, 0.090f);
            item             = Hex(0.145f, 0.153f, 0.165f);
            itemHover        = Hex(0.192f, 0.200f, 0.216f);
            text             = Hex(0.863f, 0.871f, 0.886f);
            textDim          = Hex(0.553f, 0.573f, 0.604f);
            borderStrong     = Hex(0.243f, 0.255f, 0.275f);
            border           = Hex(0.165f, 0.173f, 0.184f);

            accent           = Hex(0.863f, 0.647f, 0.392f);
            accentHover      = Hex(0.937f, 0.741f, 0.490f);
            accentActive     = Hex(0.737f, 0.522f, 0.286f);
            accentDim        = new Vector4(0.863f, 0.647f, 0.392f, 0.50f);

            rowAlt           = new Vector4(1f, 1f, 1f, 0.02f);
            tableBorderLight = borderStrong;
            resizeGrip       = new Vector4(0.35f, 0.35f, 0.35f, 0.25f);
            resizeGripHover  = new Vector4(0.45f, 0.45f, 0.45f, 0.67f);
            textSelected     = new Vector4(0.863f, 0.647f, 0.392f, 0.35f);
            modalDim         = new Vector4(0f, 0f, 0f, 0.50f);
            navHighlight     = new Vector4(1f, 1f, 1f, 0.70f);
        }

        if (PlayDim)
        {
            float k = Mode == ThemeMode.Light ? 0.94f : 0.80f;
            winBg = Dim(winBg, k); panelBg = Dim(panelBg, k); recessed = Dim(recessed, k);
            inputBg = Dim(inputBg, k); item = Dim(item, k); itemHover = Dim(itemHover, k);
            rowAlt.W *= k;
        }

        Accent = accent;
        AccentSoft = new Vector4(accent.X, accent.Y, accent.Z, Mode == ThemeMode.Light ? 0.22f : 0.16f);
        Success = Mode == ThemeMode.Light ? Hex(0.184f, 0.439f, 0.251f) : new Vector4(0.55f, 0.85f, 0.58f, 1f);
        Danger = Mode == ThemeMode.Light ? Hex(0.671f, 0.208f, 0.184f) : new Vector4(0.90f, 0.45f, 0.42f, 1f);
        DangerHover = Mode == ThemeMode.Light ? Hex(0.761f, 0.271f, 0.243f) : new Vector4(0.96f, 0.55f, 0.52f, 1f);
        ItemBg = item;
        ItemHoverBg = itemHover;
        Recessed = recessed;
        ViewportClear = Mode == ThemeMode.Light
            ? Hex(0.922f, 0.925f, 0.929f)
            : Hex(0.067f, 0.071f, 0.075f);
        AxisX = Mode == ThemeMode.Light ? Hex(0.760f, 0.290f, 0.270f) : Hex(0.851f, 0.420f, 0.396f);
        AxisY = Mode == ThemeMode.Light ? Hex(0.290f, 0.570f, 0.280f) : Hex(0.478f, 0.722f, 0.412f);

        colors[(int)ImGuiCol.WindowBg] = winBg;
        colors[(int)ImGuiCol.PopupBg] = panelBg;
        colors[(int)ImGuiCol.ChildBg] = new Vector4(0, 0, 0, 0);
        colors[(int)ImGuiCol.MenuBarBg] = panelBg;

        colors[(int)ImGuiCol.TitleBg] = recessed;
        colors[(int)ImGuiCol.TitleBgActive] = panelBg;
        colors[(int)ImGuiCol.TitleBgCollapsed] = recessed;

        colors[(int)ImGuiCol.Text] = text;
        colors[(int)ImGuiCol.TextDisabled] = textDim;

        colors[(int)ImGuiCol.Border] = border;
        colors[(int)ImGuiCol.BorderShadow] = new Vector4(0, 0, 0, 0);

        colors[(int)ImGuiCol.FrameBg] = inputBg;
        colors[(int)ImGuiCol.FrameBgHovered] = item;
        colors[(int)ImGuiCol.FrameBgActive] = item;

        colors[(int)ImGuiCol.Button] = item;
        colors[(int)ImGuiCol.ButtonHovered] = itemHover;
        colors[(int)ImGuiCol.ButtonActive] = accentActive;

        colors[(int)ImGuiCol.Header] = item;
        colors[(int)ImGuiCol.HeaderHovered] = itemHover;
        colors[(int)ImGuiCol.HeaderActive] = itemHover;

        colors[(int)ImGuiCol.Tab] = panelBg;
        colors[(int)ImGuiCol.TabHovered] = itemHover;
        colors[(int)ImGuiCol.TabSelected] = item;
        colors[(int)ImGuiCol.TabDimmed] = panelBg;
        colors[(int)ImGuiCol.TabDimmedSelected] = item;
        colors[(int)ImGuiCol.TabSelectedOverline] = new Vector4(0, 0, 0, 0);
        colors[(int)ImGuiCol.TabDimmedSelectedOverline] = new Vector4(0, 0, 0, 0);

        colors[(int)ImGuiCol.DockingPreview] = accentDim;
        colors[(int)ImGuiCol.DockingEmptyBg] = recessed;

        colors[(int)ImGuiCol.ScrollbarBg] = recessed;
        colors[(int)ImGuiCol.ScrollbarGrab] = item;
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = itemHover;
        colors[(int)ImGuiCol.ScrollbarGrabActive] = accent;

        colors[(int)ImGuiCol.SliderGrab] = accent;
        colors[(int)ImGuiCol.SliderGrabActive] = accentHover;

        colors[(int)ImGuiCol.CheckMark] = accent;

        colors[(int)ImGuiCol.ResizeGrip] = resizeGrip;
        colors[(int)ImGuiCol.ResizeGripHovered] = resizeGripHover;
        colors[(int)ImGuiCol.ResizeGripActive] = accentActive;

        colors[(int)ImGuiCol.Separator] = new Vector4(border.X, border.Y, border.Z, 0.60f);
        colors[(int)ImGuiCol.SeparatorHovered] = accent;
        colors[(int)ImGuiCol.SeparatorActive] = accentActive;

        colors[(int)ImGuiCol.DragDropTarget] = accentHover;

        colors[(int)ImGuiCol.NavWindowingHighlight] = navHighlight;
        colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.2f, 0.2f, 0.2f, 0.20f);

        colors[(int)ImGuiCol.ModalWindowDimBg] = modalDim;

        colors[(int)ImGuiCol.TextSelectedBg] = textSelected;

        colors[(int)ImGuiCol.TableHeaderBg] = panelBg;
        colors[(int)ImGuiCol.TableBorderStrong] = borderStrong;
        colors[(int)ImGuiCol.TableBorderLight] = tableBorderLight;
        colors[(int)ImGuiCol.TableRowBg] = new Vector4(0, 0, 0, 0);
        colors[(int)ImGuiCol.TableRowBgAlt] = rowAlt;
    }

    private static (uint dark, uint light) CheckerColors()
    {
        if (Mode == ThemeMode.Light)
            return (ImGui.GetColorU32(new Vector4(0.855f, 0.860f, 0.872f, 1f)),
                    ImGui.GetColorU32(new Vector4(0.945f, 0.949f, 0.953f, 1f)));
        return (ImGui.GetColorU32(new Vector4(0.173f, 0.180f, 0.192f, 1f)),
                ImGui.GetColorU32(new Vector4(0.275f, 0.282f, 0.294f, 1f)));
    }

    public static IntPtr CheckerMaskId;

    public static void DrawCheckerboard(ImDrawListPtr drawList, Vector2 pos, Vector2 size, float cell = 8f)
    {
        var visMin = Vector2.Max(pos, drawList.GetClipRectMin());
        var visMax = Vector2.Min(pos + size, drawList.GetClipRectMax());
        if (visMin.X >= visMax.X || visMin.Y >= visMax.Y) return;

        var (dark, light) = CheckerColors();
        drawList.AddRectFilled(visMin, visMax, dark);
        if (CheckerMaskId == IntPtr.Zero) return;

        float period = cell * 2f;
        var uv0 = (visMin - pos) / period;
        var uv1 = (visMax - pos) / period;
        drawList.AddImage(CheckerMaskId, visMin, visMax, uv0, uv1, light);
    }

    private static Vector4 Hex(float r, float g, float b) => new Vector4(r, g, b, 1f);

    private static Vector4 Dim(Vector4 c, float k) => new Vector4(c.X * k, c.Y * k, c.Z * k, c.W);
}
