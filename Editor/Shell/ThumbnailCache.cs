using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Assets;

namespace PixelCore.Editor;

public static class ThumbnailCache
{
    private sealed record Entry(Texture2D? Tex, IntPtr Id, DateTime Mtime,
        Rectangle? FirstSlice, int SliceCount);

    private static readonly Dictionary<string, Entry> _cache = new();
    private static GraphicsDevice? _gd;
    private static Func<Texture2D, IntPtr>? _bind;
    private static Action<IntPtr>? _unbind;

    public static void Init(GraphicsDevice gd, Func<Texture2D, IntPtr> bind, Action<IntPtr> unbind)
    {
        _gd = gd;
        _bind = bind;
        _unbind = unbind;
    }

    public static bool TryGet(string fullPath, out IntPtr texId, out Point texSize,
        out Rectangle? firstSlice, out int sliceCount)
    {
        texId = IntPtr.Zero;
        texSize = Point.Zero;
        firstSlice = null;
        sliceCount = 0;
        if (_gd == null || _bind == null) return false;

        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp")) return false;

        DateTime mtime;
        try { mtime = File.GetLastWriteTimeUtc(fullPath); }
        catch { return false; }

        if (!_cache.TryGetValue(fullPath, out var entry) || entry.Mtime != mtime)
        {
            Evict(fullPath, entry);
            entry = Load(fullPath, mtime);
            _cache[fullPath] = entry;
        }

        if (entry.Tex == null) return false;
        texId = entry.Id;
        texSize = new Point(entry.Tex.Width, entry.Tex.Height);
        firstSlice = entry.FirstSlice;
        sliceCount = entry.SliceCount;
        return true;
    }

    private static Entry Load(string fullPath, DateTime mtime)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            var tex = Texture2D.FromStream(_gd, stream);

            Rectangle? slice0 = null;
            int count = 0;
            var atlasPath = Path.ChangeExtension(fullPath, ".atlas");
            if (File.Exists(atlasPath) && SpriteAtlas.Load(atlasPath) is { } atlas && atlas.Slices.Count > 0)
            {
                var s = atlas.Slices[0];
                slice0 = new Rectangle(s.X, s.Y, s.Width, s.Height);
                count = atlas.Slices.Count;
            }

            return new Entry(tex, _bind!(tex), mtime, slice0, count);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Thumb] failed to load: {fullPath} - {ex.Message}");
            return new Entry(null, IntPtr.Zero, mtime, null, 0);
        }
    }

    public static void Prune(HashSet<string> livePaths)
    {
        List<string>? dead = null;
        foreach (var (path, entry) in _cache)
        {
            if (livePaths.Contains(path)) continue;
            (dead ??= new List<string>()).Add(path);
            Evict(path, entry);
        }
        if (dead != null)
            foreach (var d in dead) _cache.Remove(d);
    }

    private static void Evict(string path, Entry? entry)
    {
        if (entry?.Tex == null) return;
        _unbind?.Invoke(entry.Id);
        entry.Tex.Dispose();
    }

    public static System.Numerics.Vector2 FitSize(Rectangle src, System.Numerics.Vector2 box)
    {
        float s = MathF.Min(box.X / src.Width, box.Y / src.Height);
        if (s >= 1f) s = MathF.Max(1f, MathF.Floor(s));
        return new System.Numerics.Vector2(src.Width * s, src.Height * s);
    }

    public static (System.Numerics.Vector2 uv0, System.Numerics.Vector2 uv1) Uv(Rectangle src, Point texSize)
    {
        return (new System.Numerics.Vector2((float)src.X / texSize.X, (float)src.Y / texSize.Y),
                new System.Numerics.Vector2((float)(src.X + src.Width) / texSize.X,
                                            (float)(src.Y + src.Height) / texSize.Y));
    }
}
