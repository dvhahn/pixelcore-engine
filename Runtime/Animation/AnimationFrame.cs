using Microsoft.Xna.Framework;

namespace PixelCore.Runtime.Animation;

public struct AnimationFrame
{
    public Rectangle SourceRect;

    public float Duration;

    public string? EventName;

    public AnimationFrame(Rectangle sourceRect, float duration = 0.1f, string? eventName = null)
    {
        SourceRect = sourceRect;
        Duration = duration;
        EventName = eventName;
    }

    public static AnimationFrame FromSheet(int frameX, int frameY, int frameWidth, int frameHeight, float duration = 0.1f)
    {
        return new AnimationFrame(
            new Rectangle(frameX * frameWidth, frameY * frameHeight, frameWidth, frameHeight),
            duration
        );
    }
}
