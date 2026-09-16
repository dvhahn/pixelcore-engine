using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Audio;

public class SurfaceAsset
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("clipIdsL")]
    public List<string> ClipIdsL { get; set; } = new();

    [JsonPropertyName("clipIdsR")]
    public List<string> ClipIdsR { get; set; } = new();

    [JsonPropertyName("clipsL")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? LegacyClipsL { get; set; }

    [JsonPropertyName("clipsR")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? LegacyClipsR { get; set; }

    [JsonPropertyName("volume")]
    public float Volume { get; set; } = 1f;

    [JsonPropertyName("pitch")]
    public float Pitch { get; set; }

    [JsonPropertyName("pitchJitter")]
    public float PitchJitter { get; set; } = 0.07f;

    [JsonPropertyName("volumeJitter")]
    public float VolumeJitter { get; set; } = 0.1f;

    [JsonPropertyName("speedMultiplier")]
    public float SpeedMultiplier { get; set; } = 1f;

    public List<string> ClipIdsFor(bool left) => left ? ClipIdsL : ClipIdsR;

    private void MigrateLegacyClips(string file)
    {
        ClipIdsL = MigrateOne(ClipIdsL, LegacyClipsL, file, Name, "left");
        ClipIdsR = MigrateOne(ClipIdsR, LegacyClipsR, file, Name, "right");
        LegacyClipsL = null;
        LegacyClipsR = null;

        static List<string> MigrateOne(List<string> ids, List<string>? old, string file, string name, string foot)
        {
            if (old == null || old.Count == 0) return ids;

            if (ids.Count > 0)
            {
                Console.Error.WriteLine(
                    $"[surface migration] ⚠ {file}({name}) has both the new and the old key for the {foot} foot - the new key wins");
                return ids;
            }

            var migrated = new List<string>(old.Count);
            foreach (var path in old)
            {
                var id = AssetRegistry.Instance.GetAudioIdByLegacyPath(path);
                if (id != null) { migrated.Add(id); continue; }

                Console.Error.WriteLine(
                    $"[surface migration] ✘ {file}({name}) {foot} foot: no audio matches the path '{path}' " +
                    "in the registry - the value was left as it was (that clip will be silent)");
                migrated.Add(path);
            }
            return migrated;
        }
    }

    public void Save(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        AtomicFile.WriteAllText(filePath, JsonSerializer.Serialize(this, SurfaceJsonContext.Default.SurfaceAsset));
    }

    private static readonly HashSet<string> _nameNoted = new();

    public static SurfaceAsset? Load(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            var asset = JsonSerializer.Deserialize(File.ReadAllText(filePath), SurfaceJsonContext.Default.SurfaceAsset);
            if (asset == null) return null;
            asset.MigrateLegacyClips(Path.GetFileName(filePath));

            var stem = Path.GetFileNameWithoutExtension(filePath);
            if (!string.IsNullOrEmpty(stem) && asset.Name != stem)
            {
                if (!string.IsNullOrEmpty(asset.Name) && _nameNoted.Add(filePath))
                    Console.WriteLine(
                        $"[SurfaceAsset] in-file name '{asset.Name}' is ignored; the surface name is the file stem '{stem}' " +
                        $"({filePath}). It will be corrected on the next save.");
                asset.Name = stem;
            }
            return asset;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SurfaceAsset] Load failed: {filePath} — {ex.Message}");
            return null;
        }
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SurfaceAsset))]
internal partial class SurfaceJsonContext : JsonSerializerContext { }

public static class SurfaceLibrary
{
    private static readonly Dictionary<string, SurfaceAsset> _byId = new();

    public static string ContentRoot { get; set; } = Assets.ContentPaths.Root;

    public static IEnumerable<SurfaceAsset> All => _byId.Values;

    public static SurfaceAsset? Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_byId.TryGetValue(id, out var cached)) return cached;

        var rel = AssetRegistry.Instance.GetPath(id);
        if (string.IsNullOrEmpty(rel)) return null;

        var asset = SurfaceAsset.Load(Path.Combine(ContentRoot, rel)) ?? SurfaceAsset.Load(rel);
        if (asset != null) _byId[asset.Id] = asset;
        return asset;
    }

    public static void Put(SurfaceAsset asset)
    {
        if (!string.IsNullOrEmpty(asset.Id)) _byId[asset.Id] = asset;
    }

    public static void Clear() => _byId.Clear();

    static SurfaceLibrary()
    {
        Assets.AssetEvents.Changed += c =>
        {
            if (c.IsWholesale || c.Touches(".surface")) Clear();
        };
    }
}
