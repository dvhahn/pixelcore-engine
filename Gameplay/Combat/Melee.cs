using Microsoft.Xna.Framework;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Physics;

namespace PixelCore.Gameplay.Combat;

public static class Melee
{
    public const float BodyHeight = 6f;

    public const float Reach = 12f;

    public static Vector2 BodyCenter(Vector2 feet) => feet - new Vector2(0f, BodyHeight);

    public static Vector2 SlashPosition(Vector2 feet, Vector2 facing) => BodyCenter(feet) + facing * Reach;

    public static AABB HitBox(Vector2 feet, Vector2 facing)
    {
        var center = SlashPosition(feet, facing);
        var size = facing.X != 0f ? new Vector2(16f, 20f) : new Vector2(22f, 16f);
        return new AABB(center.X - size.X / 2f, center.Y - size.Y / 2f, size.X, size.Y);
    }

    public static int Strike(Scene scene, Entity attacker, AABB box, int damage, float knockback)
    {
        var from = attacker.GetComponent<Transform>()?.Position ?? box.Center;
        int hits = 0;
        var entities = scene.Entities;
        for (int i = 0; i < entities.Count; i++)
        {
            var target = entities[i];
            if (target == attacker || !target.ActiveInHierarchy) continue;
            var health = target.GetComponent<Health>();
            if (health == null || health.IsDead || !Touches(target, box)) continue;
            if (health.TakeHit(damage, from, knockback)) hits++;
        }
        return hits;
    }

    public static bool Touches(Entity target, AABB box)
    {
        bool hasCollider = false;
        foreach (var collider in target.GetComponents<Collider2D>())
        {
            if (!collider.Enabled) continue;
            hasCollider = true;
            bool hit = collider is BoxCollider2D
                ? box.Overlaps(collider.GetBounds(), out _, out _)
                : ShapeTests.OverlapsAabb(box, collider);
            if (hit) return true;
        }
        if (hasCollider) return false;

        var transform = target.GetComponent<Transform>();
        return transform != null && box.Contains(transform.Position);
    }
}
