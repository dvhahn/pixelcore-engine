using ImGuiNET;

namespace PixelCore.Editor;

public static class Mascot
{
    public const int GridW = 22;
    public const int GridH = 16;

    public static void Draw(ImDrawListPtr dl, System.Numerics.Vector2 topLeft, float cell)
    {
        float bob = (long)(ImGui.GetTime() / 0.8) % 2 == 0 ? 0f : cell;
        var origin = new System.Numerics.Vector2(topLeft.X, topLeft.Y + bob);

        void P(int x, int y, int rw, int rh, uint c) => dl.AddRectFilled(
            new System.Numerics.Vector2(origin.X + x * cell, origin.Y + y * cell),
            new System.Numerics.Vector2(origin.X + (x + rw) * cell, origin.Y + (y + rh) * cell), c);

        bool lightMode = EditorTheme.Mode == ThemeMode.Light;
        uint body   = ImGui.GetColorU32(lightMode
            ? new System.Numerics.Vector4(0.58f, 0.61f, 0.67f, 1f)
            : new System.Numerics.Vector4(0.80f, 0.84f, 0.89f, 1f));
        uint bodyHi = ImGui.GetColorU32(lightMode
            ? new System.Numerics.Vector4(0.66f, 0.69f, 0.74f, 1f)
            : new System.Numerics.Vector4(0.87f, 0.89f, 0.93f, 1f));
        uint eye    = ImGui.GetColorU32(new System.Numerics.Vector4(0.23f, 0.25f, 0.31f, 1f));
        uint cheek  = ImGui.GetColorU32(new System.Numerics.Vector4(0.91f, 0.69f, 0.66f, 1f));
        uint sprout = ImGui.GetColorU32(EditorTheme.Accent);

        P(4, 6, 14, 7, body); P(2, 8, 18, 4, body); P(6, 4, 7, 3, body); P(13, 5, 4, 2, bodyHi);
        P(7, 9, 2, 2, eye); P(13, 9, 2, 2, eye);
        P(5, 11, 2, 1, cheek); P(15, 11, 2, 1, cheek);
        P(9, 3, 3, 1, sprout); P(10, 1, 1, 2, sprout);

        dl.AddRectFilled(
            new System.Numerics.Vector2(topLeft.X + 5 * cell, topLeft.Y + 14 * cell),
            new System.Numerics.Vector2(topLeft.X + 17 * cell, topLeft.Y + 15 * cell),
            ImGui.GetColorU32(new System.Numerics.Vector4(0.54f, 0.58f, 0.65f, 0.25f)));
    }
}
