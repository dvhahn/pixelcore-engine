using System;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;

namespace PixelCore.Runtime.Rendering;

public class SkyProfile
{
    public Color SkyTop = new(120, 178, 232);
    public Color SkyBottom = new(196, 214, 232);
    public int SkyBands = 0;
    public bool Dither = false;

    public Color CloudLight = new(255, 255, 250);
    public Color CloudBody = new(248, 247, 232);
    public Color CloudShadow = new(198, 212, 224);

    public float Coverage = 0.5f;
    public float NearScale = 240f;
    public float Stretch = 1.8f;
    public float FlatBase = 0.6f;
    public float RowHeight = 60f;
    public float Lumpy = 0.35f;
    public int Shape = 1;
    public float Fluff = 4f;
    public int Layers = 2;
    public float LayerFalloff = 0.6f;
    public float Haze = 0.45f;
    public float Detail = 0.5f;
    public float Smooth = 0.3f;
    public int Shade = 1;
    public float ShadowDepth = 5f;
    public float LightAngle = 90f;
    public float Relief = 0.05f;
    public int Seed = 1;

    public float WindSpeed = 5f;
    public float WindAngle = 0f;
    public float Morph = 0.35f;
    public float TickHz = 10f;
    public float Parallax = 0.05f;

    public SkyProfile Clone() => (SkyProfile)MemberwiseClone();

    public int ValueHash()
    {
        var h = new HashCode();
        h.Add(SkyTop.PackedValue); h.Add(SkyBottom.PackedValue); h.Add(SkyBands); h.Add(Dither);
        h.Add(CloudLight.PackedValue); h.Add(CloudBody.PackedValue); h.Add(CloudShadow.PackedValue);
        h.Add(Coverage); h.Add(NearScale); h.Add(Stretch); h.Add(FlatBase); h.Add(RowHeight); h.Add(Lumpy); h.Add(Shape); h.Add(Fluff); h.Add(Layers); h.Add(LayerFalloff); h.Add(Haze); h.Add(Detail);
        h.Add(LightAngle); h.Add(Relief); h.Add(Seed); h.Add(Smooth); h.Add(Shade); h.Add(ShadowDepth);
        h.Add(WindSpeed); h.Add(WindAngle); h.Add(Morph); h.Add(TickHz); h.Add(Parallax);
        return h.ToHashCode();
    }
}

public class SkyData
{
    [JsonPropertyName("skyTop")] public int[]? SkyTop { get; set; }
    [JsonPropertyName("skyBottom")] public int[]? SkyBottom { get; set; }
    [JsonPropertyName("skyBands")] public int SkyBands { get; set; } = 0;
    [JsonPropertyName("dither")] public bool Dither { get; set; } = false;

    [JsonPropertyName("cloudLight")] public int[]? CloudLight { get; set; }
    [JsonPropertyName("cloudBody")] public int[]? CloudBody { get; set; }
    [JsonPropertyName("cloudShadow")] public int[]? CloudShadow { get; set; }

    [JsonPropertyName("coverage")] public float Coverage { get; set; } = 0.5f;
    [JsonPropertyName("nearScale")] public float NearScale { get; set; } = 240f;
    [JsonPropertyName("stretch")] public float Stretch { get; set; } = 1.8f;
    [JsonPropertyName("flatBase")] public float FlatBase { get; set; } = 0.6f;
    [JsonPropertyName("rowHeight")] public float RowHeight { get; set; } = 60f;
    [JsonPropertyName("lumpy")] public float Lumpy { get; set; } = 0.35f;
    [JsonPropertyName("shape")] public int Shape { get; set; } = 1;
    [JsonPropertyName("fluff")] public float Fluff { get; set; } = 4f;
    [JsonPropertyName("layers")] public int Layers { get; set; } = 2;
    [JsonPropertyName("layerFalloff")] public float LayerFalloff { get; set; } = 0.6f;
    [JsonPropertyName("haze")] public float Haze { get; set; } = 0.45f;
    [JsonPropertyName("detail")] public float Detail { get; set; } = 0.5f;
    [JsonPropertyName("smooth")] public float Smooth { get; set; } = 0.3f;
    [JsonPropertyName("shade")] public int Shade { get; set; } = 1;
    [JsonPropertyName("shadowDepth")] public float ShadowDepth { get; set; } = 5f;
    [JsonPropertyName("lightAngle")] public float LightAngle { get; set; } = 90f;
    [JsonPropertyName("relief")] public float Relief { get; set; } = 0.05f;
    [JsonPropertyName("seed")] public int Seed { get; set; } = 1;

    [JsonPropertyName("windSpeed")] public float WindSpeed { get; set; } = 5f;
    [JsonPropertyName("windAngle")] public float WindAngle { get; set; } = 0f;
    [JsonPropertyName("morph")] public float Morph { get; set; } = 0.35f;
    [JsonPropertyName("tickHz")] public float TickHz { get; set; } = 10f;
    [JsonPropertyName("parallax")] public float Parallax { get; set; } = 0.05f;

    public static SkyData From(SkyProfile p) => new()
    {
        SkyTop = Rgb(p.SkyTop), SkyBottom = Rgb(p.SkyBottom), SkyBands = p.SkyBands, Dither = p.Dither,
        CloudLight = Rgb(p.CloudLight), CloudBody = Rgb(p.CloudBody), CloudShadow = Rgb(p.CloudShadow),
        Coverage = p.Coverage, NearScale = p.NearScale, Stretch = p.Stretch, FlatBase = p.FlatBase, RowHeight = p.RowHeight, Lumpy = p.Lumpy, Shape = p.Shape, Fluff = p.Fluff, Layers = p.Layers, LayerFalloff = p.LayerFalloff,
        Haze = p.Haze, Detail = p.Detail, LightAngle = p.LightAngle, Relief = p.Relief, Seed = p.Seed,
        Smooth = p.Smooth, Shade = p.Shade, ShadowDepth = p.ShadowDepth,
        WindSpeed = p.WindSpeed, WindAngle = p.WindAngle, Morph = p.Morph, TickHz = p.TickHz, Parallax = p.Parallax,
    };

    public SkyProfile ToProfile()
    {
        var d = new SkyProfile();
        return new SkyProfile
        {
            SkyTop = Col(SkyTop, d.SkyTop), SkyBottom = Col(SkyBottom, d.SkyBottom), SkyBands = SkyBands, Dither = Dither,
            CloudLight = Col(CloudLight, d.CloudLight), CloudBody = Col(CloudBody, d.CloudBody), CloudShadow = Col(CloudShadow, d.CloudShadow),
            Coverage = Coverage, NearScale = NearScale, Stretch = Stretch, FlatBase = FlatBase, RowHeight = RowHeight, Lumpy = Lumpy, Shape = Shape, Fluff = Fluff, Layers = Layers, LayerFalloff = LayerFalloff,
            Haze = Haze, Detail = Detail, LightAngle = LightAngle, Relief = Relief, Seed = Seed,
            Smooth = Smooth, Shade = Shade, ShadowDepth = ShadowDepth,
            WindSpeed = WindSpeed, WindAngle = WindAngle, Morph = Morph, TickHz = TickHz, Parallax = Parallax,
        };
    }

    private static int[] Rgb(Color c) => new int[] { c.R, c.G, c.B };

    private static Color Col(int[]? a, Color fallback)
        => a is { Length: >= 3 }
            ? new Color(Math.Clamp(a[0], 0, 255), Math.Clamp(a[1], 0, 255), Math.Clamp(a[2], 0, 255))
            : fallback;
}
