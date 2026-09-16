#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PixelCore.Editor;

public static class FrameProf
{
    public static readonly int TargetFrames =
        int.TryParse(Environment.GetEnvironmentVariable("PIXELCORE_PROF"), out var f) ? f : 0;

    public static bool Enabled => TargetFrames > 0;

    public static readonly bool ExpandAll =
        Environment.GetEnvironmentVariable("PIXELCORE_PROF_EXPAND") == "1";

    private const int Warmup = 30;

    private sealed class Bucket
    {
        public double TotalMs;
        public double MaxMs;
        public int Count;
    }

    private static readonly Dictionary<string, Bucket> _buckets = new();
    private static readonly List<string> _order = new();
    private static int _frames;
    private static bool _counting;

    private static readonly Dictionary<string, long> _counters = new();
    private static readonly List<string> _counterOrder = new();

    public readonly struct Scope : IDisposable
    {
        private readonly string _name;
        private readonly long _start;
        public Scope(string name)
        {
            _name = name;
            _start = _counting ? Stopwatch.GetTimestamp() : 0;
        }
        public void Dispose()
        {
            if (!_counting) return;
            double ms = (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency;
            if (!_buckets.TryGetValue(_name, out var b))
            {
                b = new Bucket();
                _buckets[_name] = b;
                _order.Add(_name);
            }
            b.TotalMs += ms;
            b.MaxMs = Math.Max(b.MaxMs, ms);
            b.Count++;
        }
    }

    public static Scope Measure(string name) => new Scope(name);

    public static void Count(string name, long value)
    {
        if (!_counting) return;
        if (!_counters.ContainsKey(name)) _counterOrder.Add(name);
        _counters.TryGetValue(name, out long prev);
        _counters[name] = prev + value;
    }

    private static long _lastFrameStamp;

    public static bool EndFrame()
    {
        if (!Enabled) return false;
        _frames++;
        if (_frames == Warmup) _counting = true;

        long now = Stopwatch.GetTimestamp();
        if (_counting && _lastFrameStamp != 0)
        {
            double ms = (now - _lastFrameStamp) * 1000.0 / Stopwatch.Frequency;
            if (!_buckets.TryGetValue("frame.wall", out var wb))
            {
                wb = new Bucket();
                _buckets["frame.wall"] = wb;
                _order.Insert(0, "frame.wall");
            }
            wb.TotalMs += ms;
            wb.MaxMs = Math.Max(wb.MaxMs, ms);
            wb.Count++;
        }
        _lastFrameStamp = now;

        if (_frames < TargetFrames) return false;
        Report();
        return true;
    }

    private static bool _displayReported;

    public static unsafe void ReportDisplayMode(IntPtr windowHandle)
    {
        if (_displayReported) return;
        _displayReported = true;
        uint disp = SDL3.SDL.SDL_GetDisplayForWindow(windowHandle);
        IntPtr modePtr = SDL3.SDL.SDL_GetCurrentDisplayMode(disp);
        if (modePtr == IntPtr.Zero) return;
        var mode = *(SDL3.SDL.SDL_DisplayMode*)modePtr;
        Console.WriteLine($"[prof] display {disp}: {mode.w}x{mode.h} @ {mode.refresh_rate:0.###}Hz " +
                          $"(pixel density {mode.pixel_density:0.##})");
    }

    private static void Report()
    {
        int n = Math.Max(1, _frames - Warmup);
        Console.WriteLine();
        Console.WriteLine($"===== FRAME PROFILE ({n} frames) =====");
        Console.WriteLine($"{"section",-28}{"avg ms",10}{"max ms",10}{"calls/f",10}");
        foreach (var name in _order)
        {
            var b = _buckets[name];
            Console.WriteLine($"{name,-28}{b.TotalMs / n,10:0.000}{b.MaxMs,10:0.000}{(double)b.Count / n,10:0.0}");
        }
        foreach (var name in _counterOrder)
            Console.WriteLine($"{name,-28}{(double)_counters[name] / n,10:0.0}  (per frame)");
        Console.WriteLine("=====================================");
        Console.Out.Flush();
    }
}
#endif
