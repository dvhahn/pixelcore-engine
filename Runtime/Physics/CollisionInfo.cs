using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Physics;

public struct CollisionInfo
{
    public Entity Other;
    public Vector2 Normal;
    public float Penetration;

    public bool FromAbove => Normal.Y < -0.5f;

    public bool FromBelow => Normal.Y > 0.5f;

    public bool FromLeft => Normal.X > 0.5f;

    public bool FromRight => Normal.X < -0.5f;
}
