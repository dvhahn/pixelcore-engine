using System;
using Microsoft.Xna.Framework;

namespace PixelCore.Runtime.Rendering;

public static class LightingBalance
{
    public static readonly Vector3 ReadabilityFloor = new(0.1f, 0.1f, 0.2f);

    public static Color ApplyFloor(Color ambient)
    {
        var v = ambient.ToVector3();
        return new Color(
            MathHelper.Clamp(v.X + ReadabilityFloor.X, 0f, 1f),
            MathHelper.Clamp(v.Y + ReadabilityFloor.Y, 0f, 1f),
            MathHelper.Clamp(v.Z + ReadabilityFloor.Z, 0f, 1f))
        { A = ambient.A };
    }

    public const float MinDimming = 0.5f;

    public static float DimmingFactor(Color ambient)
    {
        float sum = ambient.R + ambient.G + ambient.B;
        return MathF.Max(MinDimming, 1f - 0.5f * sum / 765f);
    }
}
