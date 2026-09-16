using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Physics;

namespace PixelCore.Runtime.Components;

public abstract class Collider2D : Component
{
    public Vector2 Offset = Vector2.Zero;

    public bool IsTrigger = false;

    public Vector2 Center
    {
        get
        {
            var t = Entity.GetComponent<Transform>();
            return (t?.Position ?? Vector2.Zero) + Offset;
        }
    }

    public abstract AABB GetBounds();
}
