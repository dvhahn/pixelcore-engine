using PixelCore.Runtime.Core;

namespace PixelCore.Editor;

internal static class EditorInput
{
    internal static bool CmdOrCtrl =>
        Input.IsKeyDown(Input.SDL_SCANCODE_LCTRL) || Input.IsKeyDown(Input.SDL_SCANCODE_RCTRL)
        || Input.IsKeyDown(Input.SDL_SCANCODE_LGUI) || Input.IsKeyDown(Input.SDL_SCANCODE_RGUI);

    internal static bool Ctrl =>
        Input.IsKeyDown(Input.SDL_SCANCODE_LCTRL) || Input.IsKeyDown(Input.SDL_SCANCODE_RCTRL);

    internal static bool Shift =>
        Input.IsKeyDown(Input.SDL_SCANCODE_LSHIFT) || Input.IsKeyDown(Input.SDL_SCANCODE_RSHIFT);
}
