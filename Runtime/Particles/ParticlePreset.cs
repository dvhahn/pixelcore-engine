using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Particles;

public enum NoiseMode
{
    Displacement,

    Velocity,
}

public class ParticlePreset
{
    [JsonPropertyName("shape")]
    public EmitterShape Shape { get; set; } = EmitterShape.Point();

    [JsonPropertyName("rate")]
    public float Rate { get; set; }

    [JsonPropertyName("burstMin")]
    public int BurstMin { get; set; }

    [JsonPropertyName("burstMax")]
    public int BurstMax { get; set; }

    [JsonPropertyName("maxParticles")]
    public int MaxParticles { get; set; } = 256;

    [JsonPropertyName("speed")]
    public ParticleValue Speed { get; set; } = ParticleValue.Constant(0f);

    [JsonPropertyName("direction")]
    public float Direction { get; set; } = 90f;

    [JsonPropertyName("spread")]
    public float Spread { get; set; }

    [JsonPropertyName("gravityX")]
    public float GravityX { get; set; }

    [JsonPropertyName("gravityY")]
    public float GravityY { get; set; }

    [JsonPropertyName("swayAmplitude")]
    public float SwayAmplitude { get; set; }

    [JsonPropertyName("swayFrequency")]
    public float SwayFrequency { get; set; } = 0.5f;

    [JsonPropertyName("noiseStrength")]
    public float NoiseStrength { get; set; }

    [JsonPropertyName("noiseFrequency")]
    public float NoiseFrequency { get; set; } = 0.3f;

    [JsonPropertyName("noiseScroll")]
    public float NoiseScroll { get; set; } = 0.1f;

    [JsonPropertyName("noiseOctaves")]
    public int NoiseOctaves { get; set; } = 2;

    [JsonPropertyName("noiseMode")]
    public NoiseMode NoiseMode { get; set; } = NoiseMode.Displacement;

    [JsonPropertyName("noiseResponse")]
    public float NoiseResponse { get; set; } = 0.5f;

    [JsonPropertyName("lifetime")]
    public ParticleValue Lifetime { get; set; } = ParticleValue.Constant(1f);

    [JsonPropertyName("scale")]
    public ParticleValue Scale { get; set; } = ParticleValue.Constant(1f);

    [JsonPropertyName("alpha")]
    public ParticleValue Alpha { get; set; } = ParticleValue.Constant(1f);

    [JsonPropertyName("rotation")]
    public ParticleValue Rotation { get; set; } = ParticleValue.Constant(0f);

    [JsonPropertyName("rotationSpeed")]
    public ParticleValue RotationSpeed { get; set; } = ParticleValue.Constant(0f);

    [JsonPropertyName("texture")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TextureId { get; set; }

    [JsonPropertyName("tintR")]
    public int TintR { get; set; } = 255;

    [JsonPropertyName("tintG")]
    public int TintG { get; set; } = 255;

    [JsonPropertyName("tintB")]
    public int TintB { get; set; } = 255;

    [JsonPropertyName("renderLayer")]
    public int RenderLayer { get; set; } = Core.RenderLayers.AboveEntities;

    [JsonPropertyName("sortYOffset")]
    public float SortYOffset { get; set; }

    [JsonPropertyName("followEmitter")]
    public bool FollowEmitter { get; set; }

    [JsonPropertyName("prewarmSeconds")]
    public float PrewarmSeconds { get; set; }

    public const float MaxPrewarmSeconds = 30f;

    [JsonPropertyName("additive")]
    public bool Additive { get; set; }

    [JsonPropertyName("snapToPixels")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SnapToPixels { get; set; }

    [JsonIgnore]
    public bool SnapsToPixels => SnapToPixels ?? true;

    [JsonIgnore]
    public bool InAtmosphere => !SnapsToPixels;

    [JsonIgnore]
    public Color Tint => new(TintR, TintG, TintB);
}

public class ParticleAsset
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("preset")]
    public ParticlePreset Preset { get; set; } = new();

    internal static readonly JsonSerializerOptions JsonOptions =
        new(ParticleJsonContext.Default.Options)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

    private static JsonTypeInfo<ParticleAsset> TypeInfo
        => (JsonTypeInfo<ParticleAsset>)JsonOptions.GetTypeInfo(typeof(ParticleAsset));

    public void Save(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        AtomicFile.WriteAllText(filePath, JsonSerializer.Serialize(this, TypeInfo));
    }

    private static readonly HashSet<string> _nameNoted = new();

    public static ParticleAsset? Load(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            var text = File.ReadAllText(filePath);

            var compact = text.Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "");
            if (compact.Contains("\"kind\":\"Constant\"", StringComparison.Ordinal)
                || compact.Contains("\"kind\":\"Range\"", StringComparison.Ordinal)
                || compact.Contains("\"kind\":\"Curve\"", StringComparison.Ordinal))
            {
                Console.Error.WriteLine(
                    $"[ParticleAsset] ✘ this is the old format: {filePath}\n" +
                    "  Values look like {\"kind\":...,\"a\":...,\"b\":...}. The current format is only " +
                    "{\"min\":...,\"max\":...,\"curve\":[...]}.\n" +
                    "  To port: Constant(v) becomes min=max=v; Range(a,b) at spawn becomes min=a,max=b; " +
                    "Range(a,b) over lifetime becomes min=max=a,curve=[a,b]; Curve(p...) becomes min=max=1,curve=[p...]");
                return null;
            }

            var asset = JsonSerializer.Deserialize(text, TypeInfo);
            if (asset == null) return null;

            var stem = Path.GetFileNameWithoutExtension(filePath);
            if (!string.IsNullOrEmpty(stem) && asset.Name != stem)
            {
                if (!string.IsNullOrEmpty(asset.Name) && _nameNoted.Add(filePath))
                    Console.WriteLine(
                        $"[ParticleAsset] in-file name '{asset.Name}' is ignored; the preset name is the file stem '{stem}' " +
                        $"({filePath}). It will be corrected on the next save.");
                asset.Name = stem;
            }
            return asset;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ParticleAsset] Load failed: {filePath} — {ex.Message}");
            return null;
        }
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ParticleAsset))]
internal partial class ParticleJsonContext : JsonSerializerContext { }

public static class ParticlePresetCache
{
    private static readonly Dictionary<string, ParticleAsset> _byId = new();

    public static string ContentRoot { get; set; } = ContentPaths.Root;

    public static ParticleAsset? Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_byId.TryGetValue(id, out var cached)) return cached;

        var rel = AssetRegistry.Instance.GetPath(id);
        if (string.IsNullOrEmpty(rel)) return null;

        var asset = ParticleAsset.Load(Path.Combine(ContentRoot, rel)) ?? ParticleAsset.Load(rel);
        if (asset != null) _byId[id] = asset;
        return asset;
    }

    public static System.Collections.Generic.IReadOnlyCollection<ParticleAsset> All => _byId.Values;

    public static ParticleAsset? GetByName(string name)
    {
        foreach (var a in _byId.Values)
            if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a;
        return null;
    }

    public static void Put(ParticleAsset asset)
    {
        if (!string.IsNullOrEmpty(asset.Id)) _byId[asset.Id] = asset;
    }

    public static void Clear() => _byId.Clear();

    static ParticlePresetCache()
    {
        AssetEvents.Changed += c =>
        {
            if (c.IsWholesale || c.Touches(".particle")) Clear();
        };
    }
}
