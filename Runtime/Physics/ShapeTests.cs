using System;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Components;

namespace PixelCore.Runtime.Physics;

public static class ShapeTests
{
    public static bool Overlaps(Collider2D a, Collider2D b)
    {
        if (a is BoxCollider2D && b is BoxCollider2D)
            return a.GetBounds().Intersects(b.GetBounds());

        if (TryAsCircle(a, Probe(b), out var ca, out var ra))
            return CircleOverlaps(ca, ra, b);
        if (TryAsCircle(b, Probe(a), out var cb, out var rb))
            return CircleOverlaps(cb, rb, a);

        return a.GetBounds().Intersects(b.GetBounds());
    }

    private static Vector2 Probe(Collider2D other) => other.Center;

    private static bool CircleOverlaps(Vector2 c, float r, Collider2D other)
    {
        switch (other)
        {
            case BoxCollider2D box:
            {
                var b = box.GetBounds();
                var closest = ClampToAabb(c, b);
                return Vector2.DistanceSquared(c, closest) <= r * r;
            }
            case CircleCollider2D circle:
            {
                float rr = r + circle.Radius;
                return Vector2.DistanceSquared(c, circle.Center) <= rr * rr;
            }
            case CapsuleCollider2D cap:
            {
                var (s0, s1) = cap.GetSegment();
                float rr = r + cap.Radius;
                return DistanceSquaredPointSegment(c, s0, s1) <= rr * rr;
            }
            default:
                return other.GetBounds().Intersects(new AABB(c.X - r, c.Y - r, r * 2, r * 2));
        }
    }

    private static bool TryAsCircle(Collider2D col, Vector2 towards, out Vector2 center, out float radius)
    {
        switch (col)
        {
            case CircleCollider2D c:
                center = c.Center; radius = c.Radius; return true;
            case CapsuleCollider2D cap:
            {
                var (a, b) = cap.GetSegment();
                center = ClosestPointOnSegment(towards, a, b);
                radius = cap.Radius;
                return true;
            }
            default:
                center = default; radius = 0f; return false;
        }
    }

    public static float ResolveAxisAgainst(AABB mover, Collider2D obstacle, int axis)
    {
        Vector2 c; float r;
        var moverCenter = new Vector2(mover.Min.X + mover.Width / 2f, mover.Min.Y + mover.Height / 2f);

        switch (obstacle)
        {
            case CircleCollider2D circle:
                c = circle.Center; r = circle.Radius; break;
            case CapsuleCollider2D cap:
            {
                var (a, b) = cap.GetSegment();
                c = ClosestPointOnSegment(moverCenter, a, b);
                r = cap.Radius;
                break;
            }
            default:
                return 0f;
        }

        var closest = ClampToAabb(c, mover);
        var d = closest - c;
        float distSq = d.LengthSquared();
        if (distSq >= r * r) return 0f;

        float dist = MathF.Sqrt(distSq);
        Vector2 normal;
        if (dist > 1e-4f) normal = d / dist;
        else
        {
            var away = moverCenter - c;
            normal = away.LengthSquared() > 1e-6f ? Vector2.Normalize(away)
                     : (axis == 0 ? Vector2.UnitX : Vector2.UnitY);
        }

        float pen = r - dist;
        float push = axis == 0 ? normal.X * pen : normal.Y * pen;

        return MathF.Abs(push) < 1e-4f ? 0f : push;
    }

    public static bool TryGetRoundMtv(Collider2D mover, Collider2D obstacle,
        out Vector2 normal, out float depth)
    {
        normal = default;
        depth = 0f;

        if (!TryAsCircle(mover, obstacle.Center, out var cm, out var rm)) return false;
        if (!TryAsCircle(obstacle, cm, out var co, out var ro)) return false;
        TryAsCircle(mover, co, out cm, out rm);

        var d = cm - co;
        float distSq = d.LengthSquared();
        float rr = rm + ro;
        if (distSq >= rr * rr) return false;

        float dist = MathF.Sqrt(distSq);
        normal = dist > 1e-4f ? d / dist : Vector2.UnitX;
        depth = rr - dist;
        return true;
    }

    public static bool OverlapsAabb(AABB box, Collider2D col)
    {
        const float Skin = 0.05f;
        switch (col)
        {
            case CircleCollider2D c:
            {
                float r = MathF.Max(0f, c.Radius - Skin);
                return Vector2.DistanceSquared(c.Center, ClampToAabb(c.Center, box)) < r * r;
            }
            case CapsuleCollider2D cap:
            {
                var boxCenter = new Vector2(box.Min.X + box.Width * 0.5f, box.Min.Y + box.Height * 0.5f);
                var (a, b) = cap.GetSegment();
                var p = ClosestPointOnSegment(boxCenter, a, b);
                p = ClosestPointOnSegment(ClampToAabb(p, box), a, b);
                float r = MathF.Max(0f, cap.Radius - Skin);
                return Vector2.DistanceSquared(p, ClampToAabb(p, box)) < r * r;
            }
            default:
                return box.Intersects(col.GetBounds());
        }
    }

    public static bool ContainsPoint(Collider2D col, Vector2 point)
    {
        switch (col)
        {
            case CircleCollider2D c:
                return Vector2.DistanceSquared(c.Center, point) <= c.Radius * c.Radius;
            case CapsuleCollider2D cap:
            {
                var (a, b) = cap.GetSegment();
                return DistanceSquaredPointSegment(point, a, b) <= cap.Radius * cap.Radius;
            }
            default:
                return col.GetBounds().Contains(point);
        }
    }

    public static Vector2 ClampToAabb(Vector2 p, AABB b) => new(
        Math.Clamp(p.X, b.Min.X, b.Min.X + b.Width),
        Math.Clamp(p.Y, b.Min.Y, b.Min.Y + b.Height));

    public static Vector2 ClosestPointOnSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float len = ab.LengthSquared();
        if (len < 1e-6f) return a;
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / len, 0f, 1f);
        return a + ab * t;
    }

    public static float DistanceSquaredPointSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var q = ClosestPointOnSegment(p, a, b);
        return Vector2.DistanceSquared(p, q);
    }
}
