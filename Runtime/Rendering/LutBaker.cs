using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PixelCore.Runtime.Rendering;

public static class LutBaker
{
    public const int Size = 32;

    public static void Bake(PostProfile p, Color[] pixels)
    {
        float exposureMul = MathF.Pow(2f, p.Exposure);
        Vector3 lmsBalance = ComputeLmsBalance(p.Temperature, p.TempTint);
        bool doBalance = p.Temperature != 0f || p.TempTint != 0f;
        float contrastF = 1f + p.Contrast / 100f;
        var filterLin = SrgbToLinear(p.ColorFilter.ToVector3());
        bool doFilter = p.ColorFilter != Color.White;
        float hue = p.HueShift / 360f;
        float satF = 1f + p.Saturation / 100f;
        var (lift, gammaInv, gain) = PrepareLgg(p.Lift, p.Gamma, p.Gain);
        bool doLgg = lift != Vector3.Zero || gammaInv != Vector3.One || gain != Vector3.One;
        var smhSh = new Vector3(p.SmhShadows.X, p.SmhShadows.Y, p.SmhShadows.Z) + new Vector3(p.SmhShadows.W);
        var smhMid = new Vector3(p.SmhMidtones.X, p.SmhMidtones.Y, p.SmhMidtones.Z) + new Vector3(p.SmhMidtones.W);
        var smhHi = new Vector3(p.SmhHighlights.X, p.SmhHighlights.Y, p.SmhHighlights.Z) + new Vector3(p.SmhHighlights.W);
        bool doSmh = smhSh != Vector3.One || smhMid != Vector3.One || smhHi != Vector3.One;

        for (int b = 0; b < Size; b++)
            for (int g = 0; g < Size; g++)
                for (int r = 0; r < Size; r++)
                {
                    var c = new Vector3(r / (Size - 1f), g / (Size - 1f), b / (Size - 1f));
                    c = SrgbToLinear(c);

                    if (p.Exposure != 0f) c *= exposureMul;

                    if (doBalance) c = ApplyLms(c, lmsBalance);

                    if (p.Contrast != 0f)
                    {
                        const float mid = 0.4135884f;
                        c = new Vector3(
                            LogCToLinear((LinearToLogC(c.X) - mid) * contrastF + mid),
                            LogCToLinear((LinearToLogC(c.Y) - mid) * contrastF + mid),
                            LogCToLinear((LinearToLogC(c.Z) - mid) * contrastF + mid));
                    }

                    if (doFilter) c *= filterLin;

                    if (hue != 0f)
                    {
                        var hsv = RgbToHsv(c);
                        hsv.X = Frac(hsv.X + hue);
                        c = HsvToRgb(hsv);
                    }

                    if (p.Saturation != 0f)
                    {
                        float lum = Luminance(c);
                        c = new Vector3(lum) + (c - new Vector3(lum)) * satF;
                    }

                    if (doLgg)
                    {
                        c = Vector3.Max(c * gain + lift, Vector3.Zero);
                        c = new Vector3(
                            MathF.Pow(c.X, gammaInv.X),
                            MathF.Pow(c.Y, gammaInv.Y),
                            MathF.Pow(c.Z, gammaInv.Z));
                    }

                    if (doSmh)
                    {
                        float lum = Luminance(c);
                        float ws = 1f - Smoothstep(0f, 0.33f, lum);
                        float wh = Smoothstep(0.55f, 1f, lum);
                        float wm = 1f - ws - wh;
                        c *= smhSh * ws + smhMid * wm + smhHi * wh;
                    }

                    c = p.Tonemap switch
                    {
                        ToneMode.Neutral => NeutralTonemap(c),
                        ToneMode.Aces => AcesTonemap(c),
                        _ => c,
                    };

                    c = LinearToSrgb(Vector3.Clamp(c, Vector3.Zero, Vector3.One));
                    pixels[g * (Size * Size) + b * Size + r] =
                        new Color(c.X, c.Y, c.Z, 1f);
                }
    }

    public static Texture2D Ensure(GraphicsDevice gd, PostProfile p,
        ref Texture2D? tex, ref int bakedHash, Color[] scratch)
    {
        int hash = p.ColorHash();
        if (tex == null)
        {
            tex = new Texture2D(gd, Size * Size, Size);
            bakedHash = hash ^ 1;
        }
        if (hash != bakedHash)
        {
            Bake(p, scratch);
            tex.SetData(scratch);
            bakedHash = hash;
        }
        return tex;
    }

    private static float SrgbToLinear(float c) =>
        c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    private static float LinearToSrgb(float c) =>
        c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;

    private static Vector3 SrgbToLinear(Vector3 c) =>
        new(SrgbToLinear(c.X), SrgbToLinear(c.Y), SrgbToLinear(c.Z));

    private static Vector3 LinearToSrgb(Vector3 c) =>
        new(LinearToSrgb(c.X), LinearToSrgb(c.Y), LinearToSrgb(c.Z));

    private static float Luminance(Vector3 c) =>
        0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;

    private static float Frac(float x) => x - MathF.Floor(x);

    private static float Smoothstep(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float LinearToLogC(float x) =>
        0.244161f * MathF.Log10(MathF.Max(5.555556f * x + 0.047996f, 1e-5f)) + 0.386036f;

    private static float LogCToLinear(float x) =>
        (MathF.Pow(10f, (x - 0.386036f) / 0.244161f) - 0.047996f) / 5.555556f;

    private static float StandardIlluminantY(float x) =>
        2.87f * x - 3f * x * x - 0.27509507f;

    private static Vector3 CieXyToLms(float x, float y)
    {
        float Y = 1f;
        float X = Y * x / y;
        float Z = Y * (1f - x - y) / y;
        return new Vector3(
            0.7328f * X + 0.4296f * Y - 0.1624f * Z,
            -0.7036f * X + 1.6975f * Y + 0.0061f * Z,
            0.0030f * X + 0.0136f * Y + 0.9834f * Z);
    }

    private static Vector3 ComputeLmsBalance(float temperature, float tint)
    {
        float t1 = temperature / 65f;
        float t2 = tint / 65f;
        float x = 0.31271f - t1 * (t1 < 0f ? 0.1f : 0.05f);
        float y = StandardIlluminantY(x) + t2 * 0.05f;
        var w1 = CieXyToLms(0.31271f, StandardIlluminantY(0.31271f));
        var w2 = CieXyToLms(x, y);
        return new Vector3(w1.X / w2.X, w1.Y / w2.Y, w1.Z / w2.Z);
    }

    private static Vector3 ApplyLms(Vector3 c, Vector3 balance)
    {
        var lms = new Vector3(
            0.390405f * c.X + 0.549941f * c.Y + 0.00892632f * c.Z,
            0.0708416f * c.X + 0.963172f * c.Y + 0.00135775f * c.Z,
            0.0231082f * c.X + 0.128021f * c.Y + 0.936245f * c.Z);
        lms *= balance;
        return new Vector3(
            2.85847f * lms.X - 1.62879f * lms.Y - 0.0248910f * lms.Z,
            -0.210182f * lms.X + 1.15820f * lms.Y + 0.000324281f * lms.Z,
            -0.0418120f * lms.X - 0.118169f * lms.Y + 1.06867f * lms.Z);
    }

    private static (Vector3 lift, Vector3 gammaInv, Vector3 gain) PrepareLgg(
        Vector4 inLift, Vector4 inGamma, Vector4 inGain)
    {
        static Vector3 Wheel(Vector4 v, out float w)
        {
            w = v.W;
            var rgb = new Vector3(v.X, v.Y, v.Z);
            float avg = (rgb.X + rgb.Y + rgb.Z) / 3f;
            return rgb - new Vector3(avg);
        }

        var liftDev = Wheel(inLift, out float liftW);
        var gammaDev = Wheel(inGamma, out float gammaW);
        var gainDev = Wheel(inGain, out float gainW);

        var lift = liftDev * 0.1f + new Vector3(liftW);
        var gain = Vector3.One + gainDev * 0.8f + new Vector3(gainW);
        var gammaF = Vector3.One + gammaDev * 0.8f + new Vector3(gammaW);
        var gammaInv = new Vector3(
            1f / MathF.Max(gammaF.X, 1e-3f),
            1f / MathF.Max(gammaF.Y, 1e-3f),
            1f / MathF.Max(gammaF.Z, 1e-3f));
        return (lift, gammaInv, gain);
    }

    private static Vector3 NeutralTonemap(Vector3 c)
    {
        static float Curve(float x)
        {
            const float a = 0.2f, b = 0.29f, cc = 0.24f, d = 0.272f, e = 0.02f, f = 0.3f;
            return (x * (a * x + cc * b) + d * e) / (x * (a * x + b) + d * f) - e / f;
        }

        const float whiteLevel = 5.3f;
        float whiteScale = 1f / Curve(whiteLevel);
        return new Vector3(
            Math.Clamp(Curve(c.X * whiteScale) * whiteScale, 0f, 1f),
            Math.Clamp(Curve(c.Y * whiteScale) * whiteScale, 0f, 1f),
            Math.Clamp(Curve(c.Z * whiteScale) * whiteScale, 0f, 1f));
    }

    private static Vector3 AcesTonemap(Vector3 c)
    {
        static float Fit(float x) =>
            Math.Clamp(x * (2.51f * x + 0.03f) / (x * (2.43f * x + 0.59f) + 0.14f), 0f, 1f);
        return new Vector3(Fit(c.X), Fit(c.Y), Fit(c.Z));
    }

    private static Vector3 RgbToHsv(Vector3 c)
    {
        float max = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
        float min = MathF.Min(c.X, MathF.Min(c.Y, c.Z));
        float d = max - min;
        float h = 0f;
        if (d > 1e-6f)
        {
            if (max == c.X) h = Frac((c.Y - c.Z) / d / 6f + 1f);
            else if (max == c.Y) h = ((c.Z - c.X) / d + 2f) / 6f;
            else h = ((c.X - c.Y) / d + 4f) / 6f;
        }
        float s = max <= 1e-6f ? 0f : d / max;
        return new Vector3(h, s, max);
    }

    private static Vector3 HsvToRgb(Vector3 hsv)
    {
        float h = hsv.X * 6f, s = hsv.Y, v = hsv.Z;
        int i = (int)MathF.Floor(h) % 6;
        float f = h - MathF.Floor(h);
        float p = v * (1f - s);
        float q = v * (1f - f * s);
        float t = v * (1f - (1f - f) * s);
        return i switch
        {
            0 => new Vector3(v, t, p),
            1 => new Vector3(q, v, p),
            2 => new Vector3(p, v, t),
            3 => new Vector3(p, q, v),
            4 => new Vector3(t, p, v),
            _ => new Vector3(v, p, q),
        };
    }
}
