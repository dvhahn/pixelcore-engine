using System;
using System.Collections.Generic;
using System.IO;
using PixelCore.Runtime.Animation;

namespace PixelCore.Runtime.Assets;

public static class AnimClipCache
{
    public static string ContentRoot { get; set; } = ContentPaths.Root;

    static AnimClipCache()
    {
        AssetEvents.Changed += c =>
        {
            if (c.IsWholesale || c.Touches(".anim", ".atlas")) Clear();
        };
    }

    private static readonly Dictionary<string, AnimationClip> _byId = new(StringComparer.Ordinal);

    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    private static Dictionary<string, List<(string Id, string Path)>>? _byName;

    public static IReadOnlyList<(string Id, string Path)> FindByName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return Array.Empty<(string, string)>();
        _byName ??= BuildNameIndex();
        return _byName.TryGetValue(name, out var found)
            ? found
            : (IReadOnlyList<(string, string)>)Array.Empty<(string, string)>();
    }

    private static Dictionary<string, List<(string Id, string Path)>> BuildNameIndex()
    {
        var index = new Dictionary<string, List<(string Id, string Path)>>(StringComparer.Ordinal);

        foreach (var (id, entry) in AssetRegistry.Instance.Entries)
        {
            var rel = entry.Path;
            if (!rel.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)) continue;

            var name = Path.GetFileNameWithoutExtension(rel);
            if (string.IsNullOrEmpty(name)) continue;

            if (!index.TryGetValue(name, out var list))
                index[name] = list = new List<(string, string)>();
            list.Add((id, rel));
        }

        foreach (var list in index.Values)
            list.Sort((a, b) => StringComparer.Ordinal.Compare(a.Path, b.Path));

        return index;
    }

    public static AnimationClip? Get(string? animId)
    {
        if (string.IsNullOrEmpty(animId)) return null;
        if (_byId.TryGetValue(animId, out var cached)) return cached;

        var rel = AssetRegistry.Instance.GetPath(animId);
        if (string.IsNullOrEmpty(rel))
        {
            Warn(animId, $"animation id not in the registry: {animId}");
            return null;
        }

        var data = AnimationData.Load(Resolve(rel));
        if (data == null)
        {
            Warn(animId, $"could not read the animation file: {rel} (Id {animId})");
            return null;
        }

        var atlas = LoadAtlasFor(data, animId);
        if (atlas == null) return null;

        var clip = data.ToClip(atlas);
        _byId[animId] = clip;
        return clip;
    }

    private static SpriteAtlas? LoadAtlasFor(AnimationData data, string animId)
    {
        var atlasRel = !string.IsNullOrEmpty(data.AtlasId)
            ? AssetRegistry.Instance.GetPath(data.AtlasId)
            : null;

        if (string.IsNullOrEmpty(atlasRel)) atlasRel = data.AtlasPath?.Replace(".png", ".atlas");

        if (string.IsNullOrEmpty(atlasRel))
        {
            Warn(animId, $"animation '{data.Name}' points at no atlas (both AtlasId and AtlasPath are empty)");
            return null;
        }

        var atlas = SpriteAtlas.Load(Resolve(atlasRel));
        if (atlas == null)
        {
            Warn(animId, $"could not read the atlas: {atlasRel} (animation '{data.Name}')");
            return null;
        }

        var pngRel = atlasRel.Replace(".atlas", ".png");
        atlas.Texture = TextureLoader.Instance.Load(pngRel);
        return atlas;
    }

    private static string Resolve(string rel)
    {
        var full = Path.Combine(ContentRoot, rel);
        return File.Exists(full) ? full : rel;
    }

    private static void Warn(string id, string message)
    {
        if (!_warned.Add(id)) return;
        Console.WriteLine($"[AnimClip] ⚠ {message}");
    }

    public static void Clear()
    {
        _byId.Clear();
        _warned.Clear();
        _byName = null;
    }
}
