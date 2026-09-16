using System;
using Microsoft.Xna.Framework;

namespace PixelCore.Runtime.Rendering;

public static class SkyNoise
{
    public const int DefaultSize = 256;

    public const float WarpAmp = 0.18f;

    public static Color[] Bake(int size, int seed)
    {
        if (size < 4) throw new ArgumentOutOfRangeException(nameof(size));
        int n = size * size;
        var a = new float[n];
        var b = new float[n];
        var c = new float[n];
        var d = new float[n];

        float inv = 1f / size;
        for (int y = 0; y < size; y++)
        {
            float v = y * inv;
            for (int x = 0; x < size; x++)
            {
                float u = x * inv;
                int i = y * size + x;

                float wx = Fbm(u + 0.31f, v + 0.77f, 3, seed * 7 + 11, 3) - 0.5f;
                float wy = Fbm(u + 0.64f, v + 0.13f, 3, seed * 7 + 23, 3) - 0.5f;
                float ux = u + wx * WarpAmp;
                float uy = v + wy * WarpAmp;

                a[i] = Fbm(ux, uy, 4, seed, 5);
                b[i] = Fbm(ux + 0.5f, uy + 0.5f, 4, seed + 101, 5);

                float sx = u + wx * WarpAmp * 0.6f;
                float sy = v + wy * WarpAmp * 0.6f;
                c[i] = Fbm(sx, sy, 4, seed, 3);
                d[i] = Fbm(sx + 0.5f, sy + 0.5f, 4, seed + 101, 3);
            }
        }

        Stretch(a);
        Stretch(b);
        Stretch(c);
        Stretch(d);

        var px = new Color[n];
        for (int i = 0; i < n; i++)
            px[i] = new Color(ToByte(a[i]), ToByte(b[i]), ToByte(c[i]), ToByte(d[i]));
        return px;
    }

    public static float Fbm(float u, float v, int basePeriod, int seed, int octaves)
    {
        float sum = 0f, amp = 0.5f, total = 0f;
        int period = basePeriod;
        float fx = u * basePeriod, fy = v * basePeriod;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Perlin(fx, fy, period, seed + o * 31);
            total += amp;
            amp *= 0.5f;
            fx *= 2f; fy *= 2f;
            period *= 2;
        }
        return sum / total;
    }

    public static float Perlin(float x, float y, int period, int seed)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        float ux = Fade(fx), uy = Fade(fy);

        float n00 = Grad(ix, iy, fx, fy, period, seed);
        float n10 = Grad(ix + 1, iy, fx - 1f, fy, period, seed);
        float n01 = Grad(ix, iy + 1, fx, fy - 1f, period, seed);
        float n11 = Grad(ix + 1, iy + 1, fx - 1f, fy - 1f, period, seed);

        float n = Lerp(Lerp(n00, n10, ux), Lerp(n01, n11, ux), uy);
        return Math.Clamp(n * 1.41421356f * 0.5f + 0.5f, 0f, 1f);
    }

    private static readonly float[] GradX =
    {
        1f, 0.92388f, 0.70711f, 0.38268f, 0f, -0.38268f, -0.70711f, -0.92388f,
        -1f, -0.92388f, -0.70711f, -0.38268f, 0f, 0.38268f, 0.70711f, 0.92388f,
    };
    private static readonly float[] GradY =
    {
        0f, 0.38268f, 0.70711f, 0.92388f, 1f, 0.92388f, 0.70711f, 0.38268f,
        0f, -0.38268f, -0.70711f, -0.92388f, -1f, -0.92388f, -0.70711f, -0.38268f,
    };

    private static float Grad(int ix, int iy, float dx, float dy, int period, int seed)
    {
        int wx = ((ix % period) + period) % period;
        int wy = ((iy % period) + period) % period;
        int g = (int)(Hash(wx, wy, seed) & 15u);
        return GradX[g] * dx + GradY[g] * dy;
    }

    public static uint Hash(int x, int y, int seed)
    {
        uint h = (uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u ^ (uint)seed * 0xC2B2AE3Du;
        h ^= h >> 15; h *= 0x2C1B3C6Du;
        h ^= h >> 12; h *= 0x297A2D39u;
        h ^= h >> 15;
        return h;
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static void Stretch(float[] v)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var f in v) { if (f < min) min = f; if (f > max) max = f; }
        float range = max - min;
        if (range < 1e-6f) return;
        float inv = 1f / range;
        for (int i = 0; i < v.Length; i++) v[i] = (v[i] - min) * inv;
    }

    private static byte ToByte(float f) => (byte)Math.Clamp((int)MathF.Floor(f * 255f + 0.5f), 0, 255);
}
