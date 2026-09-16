using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PixelCore.Runtime.Serialization;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Assets;

public class PostAsset
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("profile")]
    public PostData Profile { get; set; } = new();

    internal static readonly JsonSerializerOptions JsonOptions =
        new(PostJsonContext.Default.Options)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

    private static System.Text.Json.Serialization.Metadata.JsonTypeInfo<PostAsset> TypeInfo
        => (System.Text.Json.Serialization.Metadata.JsonTypeInfo<PostAsset>)
           JsonOptions.GetTypeInfo(typeof(PostAsset));

    public void Save(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        AtomicFile.WriteAllText(filePath, JsonSerializer.Serialize(this, TypeInfo));
    }

    private static readonly HashSet<string> _nameNoted = new();

    public static PostAsset? Load(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            var raw = File.ReadAllText(filePath);
            var asset = JsonSerializer.Deserialize(raw, TypeInfo);
            if (asset == null) return null;

            PostData.WarnMovedKeys(raw, filePath);

            var stem = Path.GetFileNameWithoutExtension(filePath);
            if (!string.IsNullOrEmpty(stem) && asset.Name != stem)
            {
                if (!string.IsNullOrEmpty(asset.Name) && _nameNoted.Add(filePath))
                    Console.WriteLine(
                        $"[PostAsset] in-file name '{asset.Name}' is ignored; the preset name is the file stem '{stem}' " +
                        $"({filePath}). It will be corrected on the next save.");
                asset.Name = stem;
            }
            return asset;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PostAsset] Load failed: {filePath} — {ex.Message}");
            return null;
        }
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PostAsset))]
internal partial class PostJsonContext : JsonSerializerContext { }

public static class PostAssetCache
{
    private static readonly Dictionary<string, PostAsset> _byId = new();

    public static string ContentRoot { get; set; } = ContentPaths.Root;

    public static PostAsset? Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_byId.TryGetValue(id, out var cached)) return cached;

        var rel = AssetRegistry.Instance.GetPath(id);
        if (string.IsNullOrEmpty(rel)) return null;

        var asset = PostAsset.Load(Path.Combine(ContentRoot, rel)) ?? PostAsset.Load(rel);
        if (asset != null) _byId[id] = asset;
        return asset;
    }

    public static void Put(PostAsset asset)
    {
        if (!string.IsNullOrEmpty(asset.Id)) _byId[asset.Id] = asset;
    }

    public static bool SaveToDisk(PostAsset asset)
    {
        var rel = AssetRegistry.Instance.GetPath(asset.Id);
        if (string.IsNullOrEmpty(rel)) return false;
        try
        {
            var full = File.Exists(Path.Combine(ContentRoot, rel)) ? Path.Combine(ContentRoot, rel) : rel;
            asset.Save(full);
            Put(asset);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PostAsset] Save failed: {asset.Id} — {ex.Message}");
            return false;
        }
    }

    public static PostAsset? Create(string name, PostData profile)
    {
        var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Trim();
        if (safe.Length == 0) safe = "Post";

        var relDir = "Post";
        var rel = $"{relDir}/{safe}.post";
        var full = Path.Combine(ContentRoot, rel);
        int n = 1;
        while (File.Exists(full))
        {
            rel = $"{relDir}/{safe}_{n}.post";
            full = Path.Combine(ContentRoot, rel);
            n++;
        }

        var asset = new PostAsset
        {
            Id = AssetRegistry.Instance.NewId(),
            Name = Path.GetFileNameWithoutExtension(rel),
            Profile = profile,
        };
        try
        {
            asset.Save(full);
            AssetRegistry.Instance.Register(asset.Id, rel);
            if (!AssetRegistry.Instance.Save())
                Console.Error.WriteLine($"[PostAsset] could not record the id of '{asset.Name}' in the registry - references to this profile will break on the next boot");
            Put(asset);
            return asset;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PostAsset] Create failed: {rel} — {ex.Message}");
            return null;
        }
    }

    public static void Clear() => _byId.Clear();

    static PostAssetCache()
    {
        AssetEvents.Changed += c =>
        {
            if (c.IsWholesale || c.Touches(".post")) Clear();
        };
    }
}
