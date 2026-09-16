using System;
using Microsoft.Xna.Framework;

namespace PixelCore.Runtime.Systems;

public class TimeOfDay
{
    public float Hour { get; set; } = 12f;

    public float Speed { get; set; }

    public const float SunriseHour = 6f;
    public const float SunsetHour = 18f;

    public void SetHourClamped(float hour, float min, float max)
    {
        if (max < min) (min, max) = (max, min);
        Hour = MathHelper.Clamp(hour, min, max);
    }

    public void Update(float deltaTime)
    {
        if (Speed == 0f) return;
        Hour = (Hour + Speed * deltaTime) % 24f;
        if (Hour < 0f) Hour += 24f;
    }

    public float SunT => (Hour - SunriseHour) / (SunsetHour - SunriseHour);

    public bool IsDay => Hour >= SunriseHour && Hour <= SunsetHour;

    public float SunStrength => IsDay ? MathF.Sin(MathHelper.Clamp(SunT, 0f, 1f) * MathF.PI) : 0f;

    public float ShadowAngle => (1f - MathHelper.Clamp(SunT, 0f, 1f)) * MathF.PI;

    public float ShadowLengthFactor => IsDay ? MathHelper.Lerp(1.4f, 0.35f, SunStrength) : 0f;

    public float ShadowSkewX => IsDay ? -MathF.Cos(MathHelper.Clamp(SunT, 0f, 1f) * MathF.PI) * 1.8f : 0f;

    public float ShadowStretchY => IsDay ? MathHelper.Lerp(0.5f, 0.18f, SunStrength) : 0f;

    public float ShadowOpacity => 0.38f * MathF.Min(1f, SunStrength * 2.5f);

    private static readonly (float Hour, Color Color)[] AmbientStops =
    {
        (0.0f,  new Color(24, 28, 58)),
        (4.5f,  new Color(28, 32, 66)),
        (6.0f,  new Color(95, 74, 88)),
        (7.0f,  new Color(196, 150, 120)),
        (9.0f,  new Color(232, 224, 204)),
        (12.0f, new Color(255, 250, 240)),
        (15.0f, new Color(246, 236, 214)),
        (17.5f, new Color(235, 175, 122)),
        (19.0f, new Color(110, 82, 112)),
        (20.5f, new Color(38, 40, 74)),
        (24.0f, new Color(24, 28, 58)),
    };

    public Color AmbientColor
    {
        get
        {
            float h = ((Hour % 24f) + 24f) % 24f;
            for (int i = 1; i < AmbientStops.Length; i++)
            {
                if (h > AmbientStops[i].Hour) continue;
                var (h0, c0) = AmbientStops[i - 1];
                var (h1, c1) = AmbientStops[i];
                float t = (h - h0) / (h1 - h0);
                return Color.Lerp(c0, c1, t);
            }
            return AmbientStops[^1].Color;
        }
    }
}
