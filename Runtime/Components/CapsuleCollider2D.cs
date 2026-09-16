using PixelCore.Runtime.Core;
using System;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Physics;

namespace PixelCore.Runtime.Components;

[ComponentCategory(ComponentCategories.Physics)]
public class CapsuleCollider2D : Collider2D
{
    public float Radius = 6f;

    public float Length = 16f;

    public bool Horizontal = false;

    public (Vector2 a, Vector2 b) GetSegment()
    {
        var c = Center;
        float h = MathF.Max(0f, Length) * 0.5f;
        return Horizontal
            ? (new Vector2(c.X - h, c.Y), new Vector2(c.X + h, c.Y))
            : (new Vector2(c.X, c.Y - h), new Vector2(c.X, c.Y + h));
    }

    public override AABB GetBounds()
    {
        var (a, b) = GetSegment();
        float minX = MathF.Min(a.X, b.X) - Radius;
        float minY = MathF.Min(a.Y, b.Y) - Radius;
        float maxX = MathF.Max(a.X, b.X) + Radius;
        float maxY = MathF.Max(a.Y, b.Y) + Radius;
        return new AABB(minX, minY, maxX - minX, maxY - minY);
    }
}
