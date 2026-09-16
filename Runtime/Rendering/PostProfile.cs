using Microsoft.Xna.Framework;

namespace PixelCore.Runtime.Rendering;

public enum ToneMode
{
    None,
    Neutral,
    Aces,
}

public class PostProfile
{
    public float Exposure { get; set; }

    public float Contrast { get; set; }

    public Color ColorFilter { get; set; } = Color.White;

    public float HueShift { get; set; }

    public float Saturation { get; set; }

    public float Temperature { get; set; }

    public float TempTint { get; set; }

    public Vector4 Lift { get; set; } = new(1f, 1f, 1f, 0f);

    public Vector4 Gamma { get; set; } = new(1f, 1f, 1f, 0f);

    public Vector4 Gain { get; set; } = new(1f, 1f, 1f, 0f);

    public Vector4 SmhShadows { get; set; } = new(1f, 1f, 1f, 0f);

    public Vector4 SmhMidtones { get; set; } = new(1f, 1f, 1f, 0f);

    public Vector4 SmhHighlights { get; set; } = new(1f, 1f, 1f, 0f);

    public ToneMode Tonemap { get; set; }

    public string? LutTextureId { get; set; }

    public Color VignetteColor { get; set; } = Color.Black;

    public float Vignette { get; set; }

    public float VignetteSmooth { get; set; } = 0.2f;

    public bool VignetteRounded { get; set; }

    public float GrainIntensity { get; set; }

    public float GrainCells { get; set; } = 720f;

    public float BloomIntensity { get; set; }

    public float BloomThreshold { get; set; } = 0.9f;

    public float BloomScatter { get; set; } = 0.7f;

    public Color BloomTint { get; set; } = Color.White;

    private static bool NeutralWheel(Vector4 v) =>
        v.X == 1f && v.Y == 1f && v.Z == 1f && v.W == 0f;

    public bool HasColorGrading =>
        Exposure != 0f || Contrast != 0f || ColorFilter != Color.White ||
        HueShift != 0f || Saturation != 0f || Temperature != 0f || TempTint != 0f ||
        !NeutralWheel(Lift) || !NeutralWheel(Gamma) || !NeutralWheel(Gain) ||
        !NeutralWheel(SmhShadows) || !NeutralWheel(SmhMidtones) || !NeutralWheel(SmhHighlights) ||
        Tonemap != ToneMode.None ||
        !string.IsNullOrEmpty(LutTextureId);

    public bool HasScreenFx =>
        Vignette > 0f || GrainIntensity > 0f;

    public bool HasBloom => BloomIntensity > 0f;

    public bool IsNeutral => !HasColorGrading && !HasScreenFx && !HasBloom;

    public int ColorHash()
    {
        var h = new System.HashCode();
        h.Add(Exposure); h.Add(Contrast); h.Add(ColorFilter.PackedValue);
        h.Add(HueShift); h.Add(Saturation); h.Add(Temperature); h.Add(TempTint);
        h.Add(Lift); h.Add(Gamma); h.Add(Gain);
        h.Add(SmhShadows); h.Add(SmhMidtones); h.Add(SmhHighlights);
        h.Add(Tonemap);
        h.Add(LutTextureId);
        return h.ToHashCode();
    }

    public int ValueHash()
    {
        var h = new System.HashCode();
        h.Add(ColorHash());
        h.Add(VignetteColor.PackedValue); h.Add(Vignette); h.Add(VignetteSmooth); h.Add(VignetteRounded);
        h.Add(GrainIntensity); h.Add(GrainCells);
        h.Add(BloomIntensity); h.Add(BloomThreshold); h.Add(BloomScatter); h.Add(BloomTint.PackedValue);
        return h.ToHashCode();
    }

    public static PostProfile Lerp(PostProfile a, PostProfile b, float t)
    {
        return new PostProfile
        {
            Exposure = MathHelper.Lerp(a.Exposure, b.Exposure, t),
            Contrast = MathHelper.Lerp(a.Contrast, b.Contrast, t),
            ColorFilter = Color.Lerp(a.ColorFilter, b.ColorFilter, t),
            HueShift = MathHelper.Lerp(a.HueShift, b.HueShift, t),
            Saturation = MathHelper.Lerp(a.Saturation, b.Saturation, t),
            Temperature = MathHelper.Lerp(a.Temperature, b.Temperature, t),
            TempTint = MathHelper.Lerp(a.TempTint, b.TempTint, t),
            Lift = Vector4.Lerp(a.Lift, b.Lift, t),
            Gamma = Vector4.Lerp(a.Gamma, b.Gamma, t),
            Gain = Vector4.Lerp(a.Gain, b.Gain, t),
            SmhShadows = Vector4.Lerp(a.SmhShadows, b.SmhShadows, t),
            SmhMidtones = Vector4.Lerp(a.SmhMidtones, b.SmhMidtones, t),
            SmhHighlights = Vector4.Lerp(a.SmhHighlights, b.SmhHighlights, t),
            Tonemap = t < 0.5f ? a.Tonemap : b.Tonemap,
            LutTextureId = t < 0.5f ? a.LutTextureId : b.LutTextureId,

            VignetteColor = Color.Lerp(a.VignetteColor, b.VignetteColor, t),
            Vignette = MathHelper.Lerp(a.Vignette, b.Vignette, t),
            VignetteSmooth = MathHelper.Lerp(a.VignetteSmooth, b.VignetteSmooth, t),
            VignetteRounded = t < 0.5f ? a.VignetteRounded : b.VignetteRounded,
            GrainIntensity = MathHelper.Lerp(a.GrainIntensity, b.GrainIntensity, t),
            GrainCells = MathHelper.Lerp(a.GrainCells, b.GrainCells, t),

            BloomIntensity = MathHelper.Lerp(a.BloomIntensity, b.BloomIntensity, t),
            BloomThreshold = MathHelper.Lerp(a.BloomThreshold, b.BloomThreshold, t),
            BloomScatter = MathHelper.Lerp(a.BloomScatter, b.BloomScatter, t),
            BloomTint = Color.Lerp(a.BloomTint, b.BloomTint, t),
        };
    }

    public PostProfile Clone() => (PostProfile)MemberwiseClone();
}

public class PostPreset
{
    public string Key = "";
    public PostProfile Profile = new();

    public string? AssetId;

    public bool IsShared => !string.IsNullOrEmpty(AssetId);
}
