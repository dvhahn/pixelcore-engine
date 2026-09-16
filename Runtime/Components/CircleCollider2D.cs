using PixelCore.Runtime.Core;
using PixelCore.Runtime.Physics;

namespace PixelCore.Runtime.Components;

[ComponentCategory(ComponentCategories.Physics)]
public class CircleCollider2D : Collider2D
{
    public float Radius = 8f;

    public override AABB GetBounds()
    {
        var c = Center;
        return new AABB(c.X - Radius, c.Y - Radius, Radius * 2f, Radius * 2f);
    }
}
