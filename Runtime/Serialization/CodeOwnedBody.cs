using System;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;

namespace PixelCore.Runtime.Serialization;

public static class CodeOwnedBody
{
    public static bool Owns(Entity? e) => false;

    public static bool IsPart(Component c)
        => c is Rigidbody2D or BoxCollider2D or CapsuleCollider2D;

    public static bool IsPart(ComponentData d)
        => d is RigidbodyData or ColliderData or CapsuleColliderData;

    public static bool IsPart(string discriminator)
        => discriminator is "rigidbody" or "collider" or "capsuleCollider";

    public const string NotPartExample = "circleCollider";
}
