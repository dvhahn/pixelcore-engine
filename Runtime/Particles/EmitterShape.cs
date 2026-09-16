using System;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;

namespace PixelCore.Runtime.Particles;

public enum EmitterShapeKind
{
    Point,
    Box,
    Circle,
}

public struct EmitterShape
{
    [JsonPropertyName("kind")]
    public EmitterShapeKind Kind { get; set; }

    [JsonPropertyName("boxW")]
    public float BoxWidth { get; set; }

    [JsonPropertyName("boxH")]
    public float BoxHeight { get; set; }

    [JsonPropertyName("radius")]
    public float Radius { get; set; }

    public static EmitterShape Point() => new() { Kind = EmitterShapeKind.Point };

    public static EmitterShape Box(float width, float height)
        => new() { Kind = EmitterShapeKind.Box, BoxWidth = width, BoxHeight = height };

    public static EmitterShape Circle(float radius)
        => new() { Kind = EmitterShapeKind.Circle, Radius = radius };

    public readonly Vector2 RandomPointIn(Random rng)
    {
        switch (Kind)
        {
            case EmitterShapeKind.Box:
                return new Vector2(
                    ((float)rng.NextDouble() - 0.5f) * BoxWidth,
                    ((float)rng.NextDouble() - 0.5f) * BoxHeight);

            case EmitterShapeKind.Circle:
            {
                float angle = (float)rng.NextDouble() * MathF.PI * 2f;
                float r = MathF.Sqrt((float)rng.NextDouble()) * Radius;
                return new Vector2(MathF.Cos(angle) * r, MathF.Sin(angle) * r);
            }

            default:
                return Vector2.Zero;
        }
    }
}
