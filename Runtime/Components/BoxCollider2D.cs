using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Physics;

namespace PixelCore.Runtime.Components;

[ComponentCategory(ComponentCategories.Physics)]
public class BoxCollider2D : Collider2D
{
    public Vector2 Size = new Vector2(16, 16);

    public bool IsOneWay = false;

    public override AABB GetBounds()
    {
        var transform = Entity.GetComponent<Transform>();
        if (transform == null)
            return new AABB();

        var size = Size;
        var pos = transform.Position + Offset;

        return new AABB(
            pos.X - size.X / 2f,
            pos.Y - size.Y / 2f,
            size.X,
            size.Y
        );
    }
}
