using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Components;

[ComponentCategory(ComponentCategories.Interaction)]
public class Talker : Component
{
    public Vector2 BubbleAnchor { get; set; }

    public const float AutoAnchorHeightRate = 0.75f;

    public const float FallbackHeight = 16f;

    public static Vector2 AnchorFor(Entity entity)
    {
        var t = entity.GetComponent<Transform>();
        var pos = t?.Position ?? Vector2.Zero;

        var talker = entity.GetComponent<Talker>();
        if (talker is { Enabled: true } && talker.BubbleAnchor != Vector2.Zero)
            return pos + talker.BubbleAnchor;

        float height = VisualHeight(entity, out float pivotY);
        float feetY = pos.Y + height * (1f - pivotY);
        return new Vector2(pos.X, feetY - height * AutoAnchorHeightRate);
    }

    public static float VisualHeight(Entity entity, out float pivotY)
    {
        pivotY = 1f;
        var sr = entity.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            var s = sr.GetDrawSize();
            if (s.Y > 0f) { pivotY = sr.EffectivePivotY; return s.Y; }
        }

        var col = entity.GetComponent<Collider2D>();
        if (col != null)
        {
            var b = col.GetBounds();
            if (b.Height > 0)
            {
                pivotY = 0.5f;
                return b.Height;
            }
        }
        return FallbackHeight;
    }

    public static float VisualHeight(Entity entity) => VisualHeight(entity, out _);
}
