using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Components;

[ComponentCategory(ComponentCategories.Physics)]
public class Rigidbody2D : Component
{
    public Vector2 Velocity;

    public bool UseGravity = false;

    public float GravityScale = 1f;

    public bool IsKinematic = false;

    public bool IsGrounded { get; internal set; }

    public float MaxFallSpeed = 800f;

    public float Drag = 0f;

    public void AddForce(Vector2 force)
    {
        Velocity += force;
    }

    public void Jump(float jumpForce)
    {
        if (IsGrounded)
        {
            Velocity.Y = -jumpForce;
            IsGrounded = false;
        }
    }
}
