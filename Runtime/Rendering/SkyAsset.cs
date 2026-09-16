using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Rendering;

public class SkyAsset
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("sky")]
    public SkyData Sky { get; set; } = new();

    internal static readonly JsonSerializerOptions JsonOptions =
        new(SkyJsonContext.Default.Options)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

    private static JsonTypeInfo<SkyAsset> TypeInfo
        => (JsonTypeInfo<SkyAsset>)JsonOptions.GetTypeInfo(typeof(SkyAsset));

    public void Save(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        AtomicFile.WriteAllText(filePath, JsonSerializer.Serialize(this, TypeInfo));
    }

    private static readonly HashSet<string> _nameNoted = new();

    public static SkyAsset? Load(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            var asset = JsonSerializer.Deserialize(File.ReadAllText(filePath), TypeInfo);
            if (asset == null) return null;

            var stem = Path.GetFileNameWithoutExtension(filePath);
            if (!string.IsNullOrEmpty(stem) && asset.Name != stem)
            {
                if (!string.IsNullOrEmpty(asset.Name) && _nameNoted.Add(filePath))
                    Console.WriteLine(
                        $"[SkyAsset] in-file name '{asset.Name}' is ignored; the preset name is the file stem '{stem}' " +
                        $"({filePath}). It will be corrected on the next save.");
                asset.Name = stem;
            }
            return asset;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SkyAsset] Load failed: {filePath} — {ex.Message}");
            return null;
        }
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SkyAsset))]
internal partial class SkyJsonContext : JsonSerializerContext { }

public static class SkyPresetCache
{
    private static readonly Dictionary<string, SkyAsset> _byId = new();

    public static string ContentRoot { get; set; } = ContentPaths.Root;

    public static void Clear() => _byId.Clear();

    public static SkyAsset? Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_byId.TryGetValue(id, out var cached)) return cached;

        var rel = AssetRegistry.Instance.GetPath(id);
        if (string.IsNullOrEmpty(rel)) return null;

        var asset = SkyAsset.Load(Path.Combine(ContentRoot, rel)) ?? SkyAsset.Load(rel);
        if (asset != null) _byId[id] = asset;
        return asset;
    }

    public static IReadOnlyCollection<SkyAsset> All => _byId.Values;

    public static void Put(SkyAsset asset)
    {
        if (!string.IsNullOrEmpty(asset.Id)) _byId[asset.Id] = asset;
    }

    public static bool SaveToDisk(SkyAsset asset)
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
            Console.Error.WriteLine($"[SkyAsset] Save failed: {asset.Id} — {ex.Message}");
            return false;
        }
    }

    public static SkyAsset? Create(string name, SkyData sky)
    {
        var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Trim();
        if (safe.Length == 0) safe = "Sky";

        var relDir = "Sky";
        var rel = $"{relDir}/{safe}.sky";
        var full = Path.Combine(ContentRoot, rel);
        int n = 1;
        while (File.Exists(full))
        {
            rel = $"{relDir}/{safe}_{n}.sky";
            full = Path.Combine(ContentRoot, rel);
            n++;
        }

        var asset = new SkyAsset
        {
            Id = AssetRegistry.Instance.NewId(),
            Name = Path.GetFileNameWithoutExtension(rel),
            Sky = sky,
        };
        try
        {
            asset.Save(full);
            AssetRegistry.Instance.Register(asset.Id, rel);
            if (!AssetRegistry.Instance.Save())
                Console.Error.WriteLine($"[SkyAsset] could not record the id of '{asset.Name}' in the registry - references to this sky will break on the next boot");
            Put(asset);
            return asset;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SkyAsset] Create failed: {rel} — {ex.Message}");
            return null;
        }
    }
}
