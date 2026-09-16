using System;
using SDL3;

namespace PixelCore.Editor;

public static class WindowBounds
{
    public static void Restore(IntPtr window)
    {
        var prefs = EditorPrefs.Load();
        if (prefs.WindowW < 320 || prefs.WindowH < 240) return;

        SDL.SDL_SetWindowSize(window, prefs.WindowW, prefs.WindowH);

        int cx = prefs.WindowX + prefs.WindowW / 2;
        int cy = prefs.WindowY + prefs.WindowH / 2;
        bool visible = false;

        var displays = SDL.SDL_GetDisplays(out int count);
        if (displays != IntPtr.Zero)
        {
            for (int i = 0; i < count; i++)
            {
                uint id = (uint)System.Runtime.InteropServices.Marshal.ReadInt32(displays, i * 4);
                if (SDL.SDL_GetDisplayBounds(id, out var b) &&
                    cx >= b.x && cx < b.x + b.w && cy >= b.y && cy < b.y + b.h)
                {
                    visible = true;
                    break;
                }
            }
        }

        if (visible)
            SDL.SDL_SetWindowPosition(window, prefs.WindowX, prefs.WindowY);

        Console.WriteLine($"[WindowBounds] restored: {prefs.WindowW}x{prefs.WindowH}"
            + (visible ? $" at {prefs.WindowX},{prefs.WindowY}" : " (default position - the saved monitor is gone)"));
    }

    public static void Save(IntPtr window, EditorPrefs prefs)
    {
        var flags = SDL.SDL_GetWindowFlags(window);
        if ((flags & (SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN | SDL.SDL_WindowFlags.SDL_WINDOW_MINIMIZED)) != 0)
            return;

        SDL.SDL_GetWindowPosition(window, out int x, out int y);
        SDL.SDL_GetWindowSize(window, out int w, out int h);
        if (w < 320 || h < 240) return;

        prefs.WindowX = x; prefs.WindowY = y;
        prefs.WindowW = w; prefs.WindowH = h;
        prefs.Save();
    }
}
