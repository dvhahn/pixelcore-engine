using PixelCore.Runtime.Rendering;

namespace PixelCore.Gameplay.Systems;

public static class FxPresets
{
    public static readonly FxLayer Battle = new() { ChromAb = 0.2f, FogColor = Neutral };

    public static readonly FxLayer CutIn = new() { ChromAb = 0.2f, FogColor = Neutral };

    public static readonly FxLayer MemoryCollapse = new() { ChromAb = 0.3f, FogColor = Neutral };

    public static readonly FxLayer Nausea = new() { ChromAb = 0.5f, FogColor = Neutral };

    public static readonly FxLayer Drowsy = new()
    { ChromAb = 0.18f, LensDistortion = -0.12f, FogColor = Neutral };

    public static readonly FxLayer Sinking = new() { LensDistortion = -0.3f, FogColor = Neutral };

    private static Microsoft.Xna.Framework.Color Neutral => FxLayer.Neutral.FogColor;
}
