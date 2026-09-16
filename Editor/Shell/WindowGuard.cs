using System;
using System.Threading;
using SDL3;

namespace PixelCore.Editor;

public static unsafe class WindowGuard
{
    private static int _closeRequested;
    private static int _quitRequested;
    private static SDL.SDL_EventFilter? _filter;

    public static void Install()
    {
        if (_filter != null) return;

        SDL.SDL_SetHint("SDL_QUIT_ON_LAST_WINDOW_CLOSE", "0");

        _filter = Filter;
        SDL.SDL_SetEventFilter(_filter, IntPtr.Zero);
        Console.WriteLine("[WindowGuard] installed (SDL_SetEventFilter)");
    }

    private static bool Filter(IntPtr userdata, SDL.SDL_Event* evt)
    {
        if (evt->type == (uint)SDL.SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED)
        {
            Console.WriteLine("[WindowGuard] intercepted CLOSE_REQUESTED");
            Interlocked.Exchange(ref _closeRequested, 1);
            return false;
        }
        if (evt->type == (uint)SDL.SDL_EventType.SDL_EVENT_QUIT)
        {
            Console.WriteLine("[WindowGuard] intercepted QUIT");
            Interlocked.Exchange(ref _quitRequested, 1);
            return false;
        }
        return true;
    }

    public static bool ConsumeCloseRequest() => Interlocked.Exchange(ref _closeRequested, 0) == 1;

    public static bool ConsumeQuitRequest() => Interlocked.Exchange(ref _quitRequested, 0) == 1;
}
