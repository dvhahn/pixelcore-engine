using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Assets;

namespace PixelCore.Runtime.Rendering;

public static class LightingProfiles
{
    public static System.Text.Json.JsonSerializerOptions JsonOptions => LightingJsonContext.Default.Options;

    public const string FileName = "Lighting/profiles.json";

    public static string ContentRoot { get; set; } = ContentPaths.Root;

    private static LightingProfileTable? _table;
    private static readonly HashSet<string> _warnedTags = new();

    private static ProfileFrame _from = ProfileFrame.Neutral;
    private static ProfileFrame _to = ProfileFrame.Neutral;
    private static float _blendT = 1f;
    private static float _blendDuration;

    public static ProfileFrame Current { get; private set; } = ProfileFrame.Neutral;

    public static bool Loaded => _table != null;

    public static int RowCount => _table?.Rows?.Count ?? 0;

    public static void Load()
    {
        _table = null;
        _warnedTags.Clear();

        var path = Path.Combine(ContentRoot, FileName);
        if (!File.Exists(path)) return;

        try
        {
            var table = JsonSerializer.Deserialize(File.ReadAllText(path), LightingJsonContext.Default.LightingProfileTable);
            if (table == null)
            {
                Console.Error.WriteLine($"[Lighting] ✘ the profile table is empty: {path}");
                return;
            }
            table.Rows ??= new List<LightingProfileRow>();
            _table = table;
            Console.WriteLine($"[Lighting] loaded profile table ({table.Rows.Count} rows)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Lighting] ✘ could not read the profile table: {path} — {ex.Message}");
        }
    }

    public static void Clear()
    {
        _table = null;
        _warnedTags.Clear();
        _lastKey = null;
    }

    public static Rendering.PostProfile ModulatePost(Rendering.PostProfile source)
    {
        var f = Current;
        if (f.GrainScale == 1f && f.Vignette == null) return source;

        var copy = source.Clone();
        copy.GrainIntensity = MathF.Max(0f, copy.GrainIntensity * f.GrainScale);
        if (f.Vignette is { } v) copy.Vignette = v;
        return copy;
    }

    public static ProfileFrame Resolve(string? sceneId, string? tag, float hour)
    {
        if (_table?.Rows == null || _table.Rows.Count == 0) return ProfileFrame.Neutral;

        var row = (string.IsNullOrEmpty(sceneId) ? null : Pick(sceneId!, tag)) ?? Pick("*", tag);
        if (row == null) { WarnMissing(sceneId, tag); return ProfileFrame.Neutral; }
        return ToFrame(row, hour);
    }

    private static LightingProfileRow? Pick(string scene, string? tag)
    {
        foreach (var key in TagChain(tag))
            if (FindRow(scene, key) is { } r) return r;
        return null;
    }

    private static IEnumerable<string> TagChain(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) yield break;
        yield return tag;
        int dot = tag.LastIndexOf('.');
        if (dot > 0) yield return tag[..dot];
    }

    private static LightingProfileRow? FindRow(string scene, string tag)
    {
        foreach (var r in _table!.Rows!)
            if (string.Equals(r.Scene, scene, StringComparison.Ordinal)
             && string.Equals(r.Tag, tag, StringComparison.Ordinal)) return r;
        return null;
    }

    private static ProfileFrame ToFrame(LightingProfileRow r, float hour) => new(
        AmbientTint: new Color(r.AmbientTintR, r.AmbientTintG, r.AmbientTintB),
        AmbientScale: r.AmbientScale,
        LightScale: EvaluateCurve(r.LightScaleStops, hour),
        GrainScale: EvaluateCurve(r.GrainScaleStops, hour),
        ShadowScale: r.ShadowScale,
        Vignette: r.Vignette,
        Wind: r.Wind, Rain: r.Rain, Fog: r.Fog, Heat: r.Heat,
        PostId: string.IsNullOrEmpty(r.Post) ? null : r.Post);

    public static float EvaluateCurve(float[][]? stops, float hour)
    {
        if (stops == null || stops.Length == 0) return 1f;

        float h = ((hour % 24f) + 24f) % 24f;
        float first = stops[0].Length > 1 ? stops[0][1] : 1f;
        if (h <= stops[0][0]) return first;

        for (int i = 1; i < stops.Length; i++)
        {
            if (stops[i].Length < 2 || stops[i - 1].Length < 2) continue;
            if (h > stops[i][0]) continue;
            float h0 = stops[i - 1][0], h1 = stops[i][0];
            float t = h1 > h0 ? (h - h0) / (h1 - h0) : 0f;
            return MathHelper.Lerp(stops[i - 1][1], stops[i][1], t);
        }
        var last = stops[^1];
        return last.Length > 1 ? last[1] : 1f;
    }

    private static void WarnMissing(string? sceneId, string? tag)
    {
        var key = $"{sceneId}|{tag}";
        if (!_warnedTags.Add(key)) return;
        Console.Error.WriteLine(
            $"[Lighting] ✘ no profile row: scene '{sceneId}' × tag '{tag}' — " +
            $"add that row or a \"*\" default row to {FileName} (continuing with no modulation for now)");
    }

    public static void Begin(ProfileFrame target, float seconds)
    {
        if (seconds <= 0f)
        {
            _from = _to = Current = target;
            _blendT = 1f; _blendDuration = 0f;
            return;
        }
        _from = Current;
        _to = target;
        _blendT = 0f;
        _blendDuration = seconds;
    }

    public static void Tick(float dt)
    {
        if (_blendT >= 1f) return;
        _blendT += _blendDuration > 0f ? dt / _blendDuration : 1f;
        if (_blendT >= 1f)
        {
            _blendT = 1f;
            Current = _to;
            return;
        }
        Current = ProfileFrame.Lerp(_from, _to, _blendT);
    }

    public static bool IsBlending => _blendT < 1f;

    private static string? _lastKey;

    public static void Update(string? sceneId, string? tag, float hour, float dt, float blendSeconds = 0.7f)
    {
        var target = Resolve(sceneId, tag, hour);
        var key = $"{sceneId}|{tag}";

        if (!string.Equals(key, _lastKey, StringComparison.Ordinal))
        {
            Begin(target, _lastKey == null ? 0f : blendSeconds);
            _lastKey = key;
        }
        else if (IsBlending)
        {
            _to = target;
        }

        if (IsBlending) Tick(dt);
        else Current = target;
    }

    public static void ResetRuntime()
    {
        _from = _to = Current = ProfileFrame.Neutral;
        _blendT = 1f;
        _blendDuration = 0f;
        _lastKey = null;
        _warnedTags.Clear();
    }
}

public readonly record struct ProfileFrame(
    Color AmbientTint, float AmbientScale, float LightScale, float GrainScale, float ShadowScale,
    float? Vignette, float Wind, float Rain, float Fog, float Heat, string? PostId)
{
    public static readonly ProfileFrame Neutral =
        new(Color.White, 1f, 1f, 1f, 1f, null, 0f, 0f, 0f, 0f, null);

    public bool IsNeutral =>
        AmbientTint == Color.White && AmbientScale == 1f && LightScale == 1f && GrainScale == 1f
        && ShadowScale == 1f && Vignette == null && Wind == 0f && Rain == 0f && Fog == 0f && Heat == 0f && PostId == null;

    public static ProfileFrame Lerp(ProfileFrame a, ProfileFrame b, float t) => new(
        Color.Lerp(a.AmbientTint, b.AmbientTint, t),
        MathHelper.Lerp(a.AmbientScale, b.AmbientScale, t),
        MathHelper.Lerp(a.LightScale, b.LightScale, t),
        MathHelper.Lerp(a.GrainScale, b.GrainScale, t),
        MathHelper.Lerp(a.ShadowScale, b.ShadowScale, t),
        a.Vignette is { } va && b.Vignette is { } vb ? MathHelper.Lerp(va, vb, t) : (t < 0.5f ? a.Vignette : b.Vignette),
        MathHelper.Lerp(a.Wind, b.Wind, t),
        MathHelper.Lerp(a.Rain, b.Rain, t),
        MathHelper.Lerp(a.Fog, b.Fog, t),
        MathHelper.Lerp(a.Heat, b.Heat, t),
        t < 0.5f ? a.PostId : b.PostId);

    public Color ModulateAmbient(Color baseAmbient)
    {
        float s = MathF.Max(0f, AmbientScale);
        return new Color(
            (int)MathHelper.Clamp(baseAmbient.R * (AmbientTint.R / 255f) * s, 0f, 255f),
            (int)MathHelper.Clamp(baseAmbient.G * (AmbientTint.G / 255f) * s, 0f, 255f),
            (int)MathHelper.Clamp(baseAmbient.B * (AmbientTint.B / 255f) * s, 0f, 255f),
            baseAmbient.A);
    }
}

public class LightingProfileTable
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("rows")]
    public List<LightingProfileRow>? Rows { get; set; }
}

public class LightingProfileRow
{
    [JsonPropertyName("scene")]
    public string Scene { get; set; } = "*";

    [JsonPropertyName("tag")]
    public string Tag { get; set; } = "";

    [JsonPropertyName("post")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Post { get; set; }

    [JsonPropertyName("ambientTintR")] public int AmbientTintR { get; set; } = 255;
    [JsonPropertyName("ambientTintG")] public int AmbientTintG { get; set; } = 255;
    [JsonPropertyName("ambientTintB")] public int AmbientTintB { get; set; } = 255;

    [JsonPropertyName("ambientScale")] public float AmbientScale { get; set; } = 1f;

    [JsonPropertyName("shadowScale")] public float ShadowScale { get; set; } = 1f;

    [JsonPropertyName("vignette")] public float? Vignette { get; set; }

    [JsonPropertyName("wind")] public float Wind { get; set; }
    [JsonPropertyName("rain")] public float Rain { get; set; }
    [JsonPropertyName("fog")] public float Fog { get; set; }
    [JsonPropertyName("heat")] public float Heat { get; set; }

    [JsonPropertyName("lightScaleStops")] public float[][]? LightScaleStops { get; set; }

    [JsonPropertyName("grainScaleStops")] public float[][]? GrainScaleStops { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(LightingProfileTable))]
internal partial class LightingJsonContext : JsonSerializerContext { }
