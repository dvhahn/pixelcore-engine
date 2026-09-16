using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SDL3;

namespace PixelCore.Editor;

public static class FileDialog
{
    private sealed record Picked(Action<string[]> Handler, string[] Files);

    private static readonly ConcurrentQueue<Picked> _results = new();

    private static SDL.SDL_DialogFileCallback? _callback;
    private static Action<string[]>? _pendingHandler;

    public static void OpenFiles(IntPtr sdlWindow, string startDir, Action<string[]> onPicked)
    {
        if (_pendingHandler != null) return;
        _pendingHandler = onPicked;
        _callback = OnDialogDone;
        SDL.SDL_ShowOpenFileDialog(_callback, IntPtr.Zero, sdlWindow,
            null!, 0, startDir, true);
    }

    private static void OnDialogDone(IntPtr userdata, IntPtr filelist, int filter)
    {
        var handler = _pendingHandler;
        _pendingHandler = null;
        if (handler == null) return;

        if (filelist == IntPtr.Zero)
        {
            Console.WriteLine("[FileDialog] error: " + SDL.SDL_GetError());
            return;
        }

        var files = new List<string>();
        for (int i = 0; ; i++)
        {
            var ptr = Marshal.ReadIntPtr(filelist, i * IntPtr.Size);
            if (ptr == IntPtr.Zero) break;
            var s = Marshal.PtrToStringUTF8(ptr);
            if (!string.IsNullOrEmpty(s)) files.Add(s);
        }
        if (files.Count == 0) return;

        _results.Enqueue(new Picked(handler, files.ToArray()));
    }

    public static void Pump()
    {
        while (_results.TryDequeue(out var r))
        {
            try { r.Handler(r.Files); }
            catch (Exception ex) { Console.WriteLine($"[FileDialog] handler failed: {ex.Message}"); }
        }
    }
}
