using Microsoft.Xna.Framework;

namespace PixelCore.Runtime.Physics;

public struct AABB
{
    public Vector2 Min;
    public Vector2 Max;

    public AABB(Vector2 min, Vector2 max)
    {
        Min = min;
        Max = max;
    }

    public AABB(float x, float y, float width, float height)
    {
        Min = new Vector2(x, y);
        Max = new Vector2(x + width, y + height);
    }

    public float Width => Max.X - Min.X;
    public float Height => Max.Y - Min.Y;
    public Vector2 Center => (Min + Max) / 2f;
    public Vector2 Size => Max - Min;

    public bool Intersects(AABB other)
    {
        return Min.X < other.Max.X && Max.X > other.Min.X &&
               Min.Y < other.Max.Y && Max.Y > other.Min.Y;
    }

    public Vector2 GetPenetration(AABB other)
    {
        float overlapX = MathHelper.Min(Max.X - other.Min.X, other.Max.X - Min.X);
        float overlapY = MathHelper.Min(Max.Y - other.Min.Y, other.Max.Y - Min.Y);

        if (overlapX < overlapY)
        {
            return new Vector2(Center.X < other.Center.X ? -overlapX : overlapX, 0);
        }
        else
        {
            return new Vector2(0, Center.Y < other.Center.Y ? -overlapY : overlapY);
        }
    }

    public bool Overlaps(AABB other, out float overlapX, out float overlapY)
    {
        overlapX = MathHelper.Min(Max.X, other.Max.X) - MathHelper.Max(Min.X, other.Min.X);
        overlapY = MathHelper.Min(Max.Y, other.Max.Y) - MathHelper.Max(Min.Y, other.Min.Y);
        return overlapX > 0f && overlapY > 0f;
    }

    public bool Contains(Vector2 point)
    {
        return point.X >= Min.X && point.X <= Max.X &&
               point.Y >= Min.Y && point.Y <= Max.Y;
    }
}
