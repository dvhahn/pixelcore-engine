using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SDL3;

namespace PixelCore.Editor;

public static unsafe class FileDropWatcher
{
    private static readonly ConcurrentQueue<string> Dropped = new();
    private static SDL.SDL_EventFilter? _filter;

    public static void Install()
    {
        if (_filter != null) return;
        _filter = Watch;
        SDL.SDL_AddEventWatch(_filter, IntPtr.Zero);
    }

    private static bool Watch(IntPtr userdata, SDL.SDL_Event* evt)
    {
        if (evt->type == (uint)SDL.SDL_EventType.SDL_EVENT_DROP_FILE)
        {
            var path = Marshal.PtrToStringUTF8((IntPtr)evt->drop.data);
            if (!string.IsNullOrEmpty(path)) Dropped.Enqueue(path);
        }
        return true;
    }

    public static bool TryDequeue(out string path)
    {
        if (Dropped.TryDequeue(out var p)) { path = p; return true; }
        path = "";
        return false;
    }
}
