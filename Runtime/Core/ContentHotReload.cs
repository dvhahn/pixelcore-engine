#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;

namespace PixelCore.Runtime.Core;

public static class ContentHotReload
{
    public static float Interval = 0.4f;

    public static event Action<string>? Reloaded;

    private sealed class Entry
    {
        public string Dir = "", Pattern = "", Label = "";
        public Action Reload = () => { };
        public long Stamp;
    }

    private static readonly List<Entry> _entries = new();
    private static float _timer;

    public static void Watch(string dir, string pattern, string label, Action reload)
    {
        var e = new Entry { Dir = dir, Pattern = pattern, Label = label, Reload = reload };
        e.Stamp = Stamp(dir, pattern);
        _entries.Add(e);
    }

    public static void Clear() { _entries.Clear(); _timer = 0f; }

    public static void Update(float deltaTime)
    {
        if (_entries.Count == 0) return;
        _timer += deltaTime;
        if (_timer < Interval) return;
        _timer = 0f;
        CheckNow();
    }

    public static int CheckNow()
    {
        int reloaded = 0;
        foreach (var e in _entries)
        {
            long now = Stamp(e.Dir, e.Pattern);
            if (now == e.Stamp) continue;
            e.Stamp = now;

            Console.WriteLine($"[HotReload] {e.Label} changed - re-reading");
            e.Reload();
            Reloaded?.Invoke(e.Label);
            reloaded++;
        }
        return reloaded;
    }

    private static long Stamp(string dir, string pattern)
    {
        if (!Directory.Exists(dir)) return 0;

        long newest = 0;
        int count = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories))
            {
                count++;
                long t = File.GetLastWriteTimeUtc(f).Ticks;
                if (t > newest) newest = t;
            }
        }
        catch (IOException) { return 0; }

        return newest ^ ((long)count << 1);
    }
}
#endif
