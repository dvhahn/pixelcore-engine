using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PixelCore.Runtime.Cutscenes;

public static class CinematicBars
{
    public static float HeightRatio => 0.11f;

    public static float Duration => 0.35f;

    public static float Amount { get; private set; }

    public static void Update(float unscaledDt)
    {
        float target = FreezeState.IsFrozen(FreezeFlags.Letterbox) ? 1f : 0f;
        if (Amount == target) return;

        float step = Duration > 0f ? unscaledDt / Duration : 1f;
        Amount = target > Amount ? MathF.Min(target, Amount + step) : MathF.Max(target, Amount - step);
    }

    public static void Set(float amount) => Amount = Math.Clamp(amount, 0f, 1f);

    public static void Draw(SpriteBatch sb, Texture2D pixel, int vw, int vh)
    {
        if (Amount <= 0.001f) return;

        int bar = (int)MathF.Round(vh * HeightRatio * Easing.Apply(Ease.Out, Amount));
        if (bar <= 0) return;

        sb.Begin();
        sb.Draw(pixel, new Rectangle(0, 0, vw, bar), Color.Black);
        sb.Draw(pixel, new Rectangle(0, vh - bar, vw, bar), Color.Black);
        sb.End();
    }
}
