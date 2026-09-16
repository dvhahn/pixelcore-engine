using System;
using System.Text.Json.Serialization;

namespace PixelCore.Runtime.Particles;

public struct ParticleValue
{
    [JsonPropertyName("min")]
    public float Min { get; set; }

    [JsonPropertyName("max")]
    public float Max { get; set; }

    [JsonPropertyName("curve")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float[]? Curve { get; set; }

    public static ParticleValue Constant(float value) => new() { Min = value, Max = value };

    public static ParticleValue Range(float min, float max) => new() { Min = min, Max = max };

    public static ParticleValue Curved(params float[] points)
        => new() { Min = 1f, Max = 1f, Curve = points };

    public static ParticleValue RangeCurved(float min, float max, params float[] points)
        => new() { Min = min, Max = max, Curve = points };

    public readonly float Evaluate(float seed01, float delta)
    {
        float value = Min + (Max - Min) * seed01;

        var c = Curve;
        if (c == null || c.Length == 0) return value;
        if (c.Length == 1) return value * c[0];

        float t = delta < 0f ? 0f : (delta > 1f ? 1f : delta);
        float scaled = t * (c.Length - 1);
        int i = (int)scaled;
        if (i >= c.Length - 1) return value * c[c.Length - 1];
        return value * (c[i] + (c[i + 1] - c[i]) * (scaled - i));
    }

    public readonly float SampleAtSpawn(Random rng) => Evaluate((float)rng.NextDouble(), 0f);
}
