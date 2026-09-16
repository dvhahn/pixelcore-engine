using System;

namespace PixelCore.Runtime.UI;

public static class UiScale
{
    public const int ReferenceHeight = 180;

    public static int For(float gameAreaHeight)
    {
        if (!(gameAreaHeight > 0f)) return 1;
        return Math.Max(1, (int)MathF.Floor(gameAreaHeight / ReferenceHeight));
    }
}
