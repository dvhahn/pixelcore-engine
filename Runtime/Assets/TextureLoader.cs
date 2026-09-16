using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework.Graphics;

namespace PixelCore.Runtime.Assets;

public class TextureLoader
{
    private static TextureLoader? _instance;
    public static TextureLoader Instance => _instance ??= new TextureLoader();

    private GraphicsDevice? _graphicsDevice;

    public static bool Headless { get; set; }

    private static bool _warnedUninitialized;
    private readonly Dictionary<string, Texture2D> _cache = new();
    private string _basePath = ContentPaths.Root;

    public string BasePath => _basePath;

    private TextureLoader() { }

    public void Initialize(GraphicsDevice graphicsDevice, string basePath = "Content")
    {
        _graphicsDevice = graphicsDevice;
        _basePath = basePath;
    }

    public Texture2D? Load(string path)
    {
        if (_graphicsDevice == null)
        {
            if (!Headless && !_warnedUninitialized)
            {
                _warnedUninitialized = true;
                Console.WriteLine("[TextureLoader] Not initialized - call Initialize() first " +
                                  "(this warns once; for a run without graphics, set Headless)");
            }
            return null;
        }

        if (_cache.TryGetValue(path, out var cached))
            return cached;

        var fullPath = Path.Combine(_basePath, path);

        if (!File.Exists(fullPath))
            return Missing(path, "file not found");

        try
        {
            using var stream = File.OpenRead(fullPath);
            var texture = Texture2D.FromStream(_graphicsDevice, stream);
            _cache[path] = texture;
            ForgetMissing(path);
            Console.WriteLine($"[TextureLoader] Loaded: {path} ({texture.Width}x{texture.Height})");
            return texture;
        }
        catch (Exception ex)
        {
            return Missing(path, ex.Message);
        }
    }

    private readonly HashSet<string> _missing = new();

    private Texture2D? _placeholder;

    internal bool NoteMissing(string path) => _missing.Add(path);

    internal bool ForgetMissing(string path) => _missing.Remove(path);

    private Texture2D? Missing(string path, string reason)
    {
        if (NoteMissing(path))
            Console.WriteLine($"[TextureLoader] texture missing: {path} — {reason}");

#if DEBUG
        return Placeholder();
#else
        return null;
#endif
    }

    public const int PlaceholderSize = 16;

    private Texture2D Placeholder()
    {
        if (_placeholder != null) return _placeholder;
        _placeholder = new Texture2D(_graphicsDevice, PlaceholderSize, PlaceholderSize);
        _placeholder.SetData(BuildPlaceholderPixels(PlaceholderSize));
        return _placeholder;
    }

    internal static Microsoft.Xna.Framework.Color[] BuildPlaceholderPixels(int size)
    {
        var pixels = new Microsoft.Xna.Framework.Color[size * size];
        int cell = Math.Max(1, size / 4);
        var magenta = new Microsoft.Xna.Framework.Color(255, 0, 255);
        var dark = new Microsoft.Xna.Framework.Color(48, 0, 48);

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = ((x / cell + y / cell) & 1) == 0 ? magenta : dark;

        return pixels;
    }

    public void Unload(string path)
    {
        if (_cache.TryGetValue(path, out var texture))
        {
            texture.Dispose();
            _cache.Remove(path);
        }
    }

    public void UnloadAll()
    {
        foreach (var texture in _cache.Values)
        {
            texture.Dispose();
        }
        _cache.Clear();
    }

    public IReadOnlyDictionary<string, Texture2D> GetCached() => _cache;
}
