using System;

namespace PixelCore.Runtime.UI;

public readonly struct BubbleScales
{
    public static float SkinDivisor => 2f;

    public static float TextDivisor => 2.5f;

    public int World { get; init; }

    public int Skin { get; init; }

    public int Text { get; init; }

    public static BubbleScales For(float uiScale)
    {
        int world = Math.Max(1, Round(uiScale));
        return new BubbleScales
        {
            World = world,
            Skin = Math.Max(1, Round(world / SkinDivisor)),
            Text = Math.Max(1, Round(world / TextDivisor)),
        };
    }

    private static int Round(float v) => (int)MathF.Round(v, MidpointRounding.AwayFromZero);
}
