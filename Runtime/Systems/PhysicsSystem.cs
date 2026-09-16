using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Physics;

namespace PixelCore.Runtime.Systems;

public class PhysicsSystem
{
    public float Gravity = 980f;

    public bool CornerNudge = true;

    public float CornerNudgeRatio = 1f;

    public float CornerNudgeAxisTolerance = 0.25f;

    private int _nudgeSign;
    private int _nudgeAxis;

    public bool RoundSteer = true;

    private const float RoundSteerDeadZone = 0.25f;

    private int _steerSign;
    private bool _steerFlipped;
    private Collider2D? _steerBlocker;

    private Scene _scene;
    private List<(Collider2D, Rigidbody2D?, Transform)> _colliders = new();

    public void SetScene(Scene scene)
    {
        _scene = scene;
        _prevContacts.Clear();
        _currContacts.Clear();
        _steerSign = 0; _steerFlipped = false; _steerBlocker = null;
    }

    private readonly struct Contact
    {
        public readonly Collider2D A;
        public readonly Collider2D B;
        public readonly bool Trigger;
        public Contact(Collider2D a, Collider2D b, bool trigger) { A = a; B = b; Trigger = trigger; }
    }
    private Dictionary<long, Contact> _prevContacts = new();
    private Dictionary<long, Contact> _currContacts = new();

    public PhysicsSystem(Scene scene)
    {
        _scene = scene;
    }

    public void Update(float deltaTime)
    {
        RefreshColliders();

        var tilemap = FindTilemap();

        _movedBodies.Clear();
        foreach (var (collider, rb, transform) in _colliders)
        {
            if (rb == null || rb.IsKinematic) continue;
            if (collider.IsTrigger && HasSolidCollider(collider.Entity)) continue;
            if (!_movedBodies.Add(rb)) continue;

            if (rb.UseGravity)
            {
                rb.Velocity.Y += Gravity * rb.GravityScale * deltaTime;
                rb.Velocity.Y = MathHelper.Min(rb.Velocity.Y, rb.MaxFallSpeed);
            }

            if (rb.Drag > 0)
                rb.Velocity *= 1f - rb.Drag * deltaTime;

            rb.IsGrounded = false;

            float cell = tilemap?.TileSize ?? 32f;
            float maxMove = MathF.Max(MathF.Abs(rb.Velocity.X), MathF.Abs(rb.Velocity.Y)) * deltaTime;
            int steps = maxMove > cell ? (int)MathF.Ceiling(maxMove / cell) : 1;
            steps = Math.Clamp(steps, 1, 8);
            float subDt = deltaTime / steps;

            for (int s = 0; s < steps; s++)
                MoveAndCollide(collider, rb, transform, subDt, tilemap);
        }

        DispatchCollisionEvents();
    }

    private readonly HashSet<Rigidbody2D> _movedBodies = new();

    private bool HasSolidCollider(Entity e)
    {
        foreach (var c in e.GetComponents<Collider2D>())
            if (c.Enabled && !c.IsTrigger) return true;
        return false;
    }

    private void MoveAndCollide(Collider2D collider, Rigidbody2D rb, Transform transform, float dt, TilemapRenderer? tilemap)
    {
        bool roundMover = collider is CircleCollider2D or CapsuleCollider2D;
        var frameStart = transform.Position;

        var vel = rb.Velocity;
        bool nudged = false;

        transform.Position = new Vector2(transform.Position.X + rb.Velocity.X * dt, transform.Position.Y);
        if (ResolveAxis(collider, transform, 0, tilemap, skipRound: roundMover))
        {
            if (TryCornerNudge(collider, transform, tilemap, vel, 0, dt, roundMover)) nudged = true;
            else rb.Velocity.X = 0;
        }

        transform.Position = new Vector2(transform.Position.X, transform.Position.Y + rb.Velocity.Y * dt);
        if (ResolveAxis(collider, transform, 1, tilemap, skipRound: roundMover))
        {
            if (TryCornerNudge(collider, transform, tilemap, vel, 1, dt, roundMover)) nudged = true;
            else
            {
                if (rb.UseGravity && rb.Velocity.Y > 0) rb.IsGrounded = true;
                rb.Velocity.Y = 0;
            }
        }

        if (roundMover)
            nudged |= ResolveRoundSlide(collider, rb, transform, tilemap, frameStart, rb.Velocity.Length() * dt, vel, dt);

        if (!nudged) _nudgeSign = 0;
    }

    private bool TryCornerNudge(Collider2D collider, Transform transform, TilemapRenderer? tilemap,
                                Vector2 vel, int moveAxis, float dt, bool skipRound)
    {
        if (!CornerNudge) return false;

        float along = moveAxis == 0 ? vel.X : vel.Y;
        float cross = moveAxis == 0 ? vel.Y : vel.X;
        if (along == 0f) return false;

        if (MathF.Abs(cross) > MathF.Abs(along) * CornerNudgeAxisTolerance) return false;

        float step = MathF.Abs(along) * dt;
        if (step <= 0f) return false;

        var b = collider.GetBounds();
        float dir = MathF.Sign(along);

        var (lowProbe, highProbe) = LeadingQuarters(b, moveAxis, dir, step);
        bool lowBlocked = IsBlocked(collider, lowProbe, tilemap, skipRound);
        bool highBlocked = IsBlocked(collider, highProbe, tilemap, skipRound);
        if (lowBlocked == highBlocked) return false;

        int sign = lowBlocked ? +1 : -1;

        if (_nudgeSign != 0 && _nudgeAxis == moveAxis && _nudgeSign != sign)
        {
            var keep = OffsetBounds(b, moveAxis == 0 ? 1 : 0, _nudgeSign * step);
            if (!IsBlocked(collider, keep, tilemap, skipRound)) sign = _nudgeSign;
        }

        int perp = moveAxis == 0 ? 1 : 0;
        float amount = step * CornerNudgeRatio;
        var dest = OffsetBounds(b, perp, sign * amount);
        if (IsBlocked(collider, dest, tilemap, skipRound)) return false;

        transform.Position += perp == 0
            ? new Vector2(sign * amount, 0f)
            : new Vector2(0f, sign * amount);

        _nudgeSign = sign;
        _nudgeAxis = moveAxis;
        return true;
    }

    private static (AABB Low, AABB High) LeadingQuarters(AABB b, int moveAxis, float dir, float step)
    {
        if (moveAxis == 0)
        {
            float x = dir > 0 ? b.Max.X : b.Min.X - step;
            float q = b.Height * 0.25f;
            return (new AABB(x, b.Min.Y, step, q),
                    new AABB(x, b.Max.Y - q, step, q));
        }
        else
        {
            float y = dir > 0 ? b.Max.Y : b.Min.Y - step;
            float q = b.Width * 0.25f;
            return (new AABB(b.Min.X, y, q, step),
                    new AABB(b.Max.X - q, y, q, step));
        }
    }

    private static AABB OffsetBounds(AABB b, int axis, float amount)
    {
        var min = b.Min + (axis == 0 ? new Vector2(amount, 0f) : new Vector2(0f, amount));
        return new AABB(min.X, min.Y, b.Width, b.Height);
    }

    private bool IsBlocked(Collider2D self, AABB probe, TilemapRenderer? tilemap, bool skipRound)
    {
        if (tilemap != null &&
            (tilemap.ResolveAxis(probe, 0) != 0f || tilemap.ResolveAxis(probe, 1) != 0f))
            return true;

        for (int k = 0; k < _colliders.Count; k++)
        {
            var (other, _, _) = _colliders[k];
            if (other == self || other.Entity == self.Entity) continue;
            if (other.IsTrigger || !other.Enabled) continue;
            if (skipRound && other is CircleCollider2D or CapsuleCollider2D) continue;
            if (other is BoxCollider2D)
            {
                if (probe.Overlaps(other.GetBounds(), out _, out _)) return true;
            }
            else if (Physics.ShapeTests.OverlapsAabb(probe, other)) return true;
        }
        return false;
    }

    private bool ResolveRoundSlide(Collider2D collider, Rigidbody2D rb, Transform transform,
        TilemapRenderer? tilemap, Vector2 frameStart, float moveLen, Vector2 vel, float dt)
    {
        Collider2D? blocker = null;

        for (int iter = 0; iter < 2; iter++)
        {
            bool pushed = false;
            for (int k = 0; k < _colliders.Count; k++)
            {
                var (other, _, _) = _colliders[k];
                if (other == collider) continue;
                if (other.Entity == collider.Entity) continue;
                if (collider.IsTrigger || other.IsTrigger) continue;
                if (other is not (CircleCollider2D or CapsuleCollider2D)) continue;
                if (!Physics.ShapeTests.TryGetRoundMtv(collider, other, out var normal, out var depth)) continue;

                transform.Position += normal * depth;
                float into = Vector2.Dot(rb.Velocity, normal);
                if (into < 0f) rb.Velocity -= normal * into;

                if (Vector2.Dot(vel, normal) < 0f) blocker = other;
                pushed = true;
            }
            if (!pushed) break;

            ResolveAxis(collider, transform, 0, tilemap, skipRound: true);
            ResolveAxis(collider, transform, 1, tilemap, skipRound: true);
        }

        if (Vector2.Distance(transform.Position, frameStart) > moveLen + 4f)
        {
            transform.Position = frameStart;
            return false;
        }

        if (AnyRoundSolidOverlap(collider))
        {
            var attempted = transform.Position;
            transform.Position = frameStart;
            if (AnyRoundSolidOverlap(collider)) transform.Position = attempted;
        }

        bool corrected = false;
        if (blocker != null)
        {
            int moveAxis = MathF.Abs(vel.X) >= MathF.Abs(vel.Y) ? 0 : 1;
            bool steerEngaged = _steerBlocker == blocker && _steerSign != 0;
            corrected = (!steerEngaged && TryCornerNudge(collider, transform, tilemap, vel, moveAxis, dt, skipRound: false))
                     || TryRoundSteer(collider, transform, tilemap, blocker, vel, moveAxis, dt);
        }
        else
        {
            _steerSign = 0; _steerFlipped = false; _steerBlocker = null;
        }
        return corrected;
    }

    private bool TryRoundSteer(Collider2D collider, Transform transform, TilemapRenderer? tilemap,
                               Collider2D blocker, Vector2 vel, int moveAxis, float dt)
    {
        if (!RoundSteer) return false;

        float along = moveAxis == 0 ? vel.X : vel.Y;
        float step = MathF.Abs(along) * dt;
        if (step <= 0f) return false;

        if (_steerBlocker != blocker) { _steerBlocker = blocker; _steerSign = 0; _steerFlipped = false; }

        int perp = moveAxis == 0 ? 1 : 0;
        float offset = perp == 0 ? collider.Center.X - blocker.Center.X
                                 : collider.Center.Y - blocker.Center.Y;

        int sign = _steerSign;
        if (sign == 0)
        {
            if (MathF.Abs(offset) <= RoundSteerDeadZone) return false;
            sign = offset > 0f ? 1 : -1;
        }

        float amount = step * CornerNudgeRatio;
        var b = collider.GetBounds();
        if (IsBlocked(collider, OffsetBounds(b, perp, sign * amount), tilemap, skipRound: false))
        {
            if (_steerFlipped) return false;
            sign = -sign;
            if (IsBlocked(collider, OffsetBounds(b, perp, sign * amount), tilemap, skipRound: false))
                return false;
            _steerFlipped = true;
        }

        transform.Position += perp == 0 ? new Vector2(sign * amount, 0f) : new Vector2(0f, sign * amount);
        _steerSign = sign;
        return true;
    }
    private bool AnyRoundSolidOverlap(Collider2D collider)
    {
        const float PinchEps = 0.05f;
        if (collider.IsTrigger) return false;
        for (int k = 0; k < _colliders.Count; k++)
        {
            var (other, _, _) = _colliders[k];
            if (other == collider || other.Entity == collider.Entity || other.IsTrigger) continue;
            if (other is not (CircleCollider2D or CapsuleCollider2D)) continue;
            if (Physics.ShapeTests.TryGetRoundMtv(collider, other, out _, out var depth) && depth > PinchEps)
                return true;
        }
        return false;
    }

    private bool ResolveAxis(Collider2D collider, Transform transform, int axis, TilemapRenderer? tilemap, bool skipRound = false)
    {
        bool hit = false;

        if (tilemap != null)
        {
            float push = tilemap.ResolveAxis(collider.GetBounds(), axis);
            if (push != 0f)
            {
                transform.Position += axis == 0 ? new Vector2(push, 0) : new Vector2(0, push);
                hit = true;
            }
        }

        var bounds = collider.GetBounds();
        for (int k = 0; k < _colliders.Count; k++)
        {
            var (other, _, _) = _colliders[k];
            if (other == collider) continue;
            if (other.Entity == collider.Entity) continue;
            if (collider.IsTrigger || other.IsTrigger) continue;
            if (other is BoxCollider2D otherBox)
            {
                if (otherBox.IsOneWay) continue;

                var ob = other.GetBounds();
                if (!bounds.Overlaps(ob, out float ox, out float oy)) continue;

                if (axis == 0)
                    transform.Position += new Vector2(bounds.Center.X < ob.Center.X ? -ox : ox, 0);
                else
                    transform.Position += new Vector2(0, bounds.Center.Y < ob.Center.Y ? -oy : oy);
            }
            else
            {
                if (skipRound) continue;

                float push = Physics.ShapeTests.ResolveAxisAgainst(bounds, other, axis);
                if (push == 0f) continue;
                transform.Position += axis == 0 ? new Vector2(push, 0) : new Vector2(0, push);
            }

            bounds = collider.GetBounds();
            hit = true;
        }

        return hit;
    }

    private TilemapRenderer? FindTilemap()
    {
        foreach (var entity in _scene.Entities)
        {
            if (!entity.ActiveInHierarchy) continue;
            var t = entity.GetComponent<TilemapRenderer>();
            if (t != null && t.Enabled) return t;
        }
        return null;
    }

    private void RefreshColliders()
    {
        _colliders.Clear();
        foreach (var entity in _scene.Entities)
        {
            if (!entity.ActiveInHierarchy) continue;

            var transform = entity.GetComponent<Transform>();
            if (transform == null) continue;
            var rb = entity.GetComponent<Rigidbody2D>();

            foreach (var collider in entity.GetComponents<Collider2D>())
            {
                if (!collider.Enabled) continue;
                _colliders.Add((collider, rb, transform));
            }
        }
    }

    private void DispatchCollisionEvents()
    {
        (_prevContacts, _currContacts) = (_currContacts, _prevContacts);
        _currContacts.Clear();

        for (int i = 0; i < _colliders.Count; i++)
        {
            var (colliderA, _, _) = _colliders[i];
            var boundsA = colliderA.GetBounds();

            for (int j = i + 1; j < _colliders.Count; j++)
            {
                var (colliderB, _, _) = _colliders[j];
                if (colliderB.Entity == colliderA.Entity) continue;
                if (!boundsA.Intersects(colliderB.GetBounds())) continue;
                if (!Physics.ShapeTests.Overlaps(colliderA, colliderB)) continue;

                long key = PairKey(colliderA.Entity.Id, colliderB.Entity.Id);
                bool trigger = colliderA.IsTrigger || colliderB.IsTrigger;
                _currContacts[key] = new Contact(colliderA, colliderB, trigger);
            }
        }

        foreach (var kv in _currContacts)
        {
            bool isNew = !_prevContacts.ContainsKey(kv.Key);
            DispatchPair(kv.Value, isNew ? 0 : 1);
        }

        foreach (var kv in _prevContacts)
        {
            if (!_currContacts.ContainsKey(kv.Key))
                DispatchPair(kv.Value, 2);
        }
    }

    private static long PairKey(int idA, int idB)
    {
        int lo = idA < idB ? idA : idB;
        int hi = idA < idB ? idB : idA;
        return ((long)lo << 32) | (uint)hi;
    }

    private static void DispatchPair(Contact c, int phase)
    {
        if (c.Trigger)
        {
            DispatchTrigger(c.A.Entity, c.B, phase);
            DispatchTrigger(c.B.Entity, c.A, phase);
        }
        else
        {
            var pen = c.A.GetBounds().GetPenetration(c.B.GetBounds());
            float mag = pen.Length();
            var normal = mag > 1e-4f ? pen / mag : Vector2.Zero;
            DispatchCollision(c.A.Entity, new CollisionInfo { Other = c.B.Entity, Normal = normal, Penetration = mag }, phase);
            DispatchCollision(c.B.Entity, new CollisionInfo { Other = c.A.Entity, Normal = -normal, Penetration = mag }, phase);
        }
    }

    private static void DispatchTrigger(Entity e, Collider2D other, int phase)
    {
        var comps = e.Components;
        for (int k = 0; k < comps.Count; k++)
        {
            var comp = comps[k];
            if (!comp.Enabled) continue;
            if (phase == 0) comp.OnTriggerEnter(other);
            else if (phase == 1) comp.OnTriggerStay(other);
            else comp.OnTriggerExit(other);
        }
    }

    private static void DispatchCollision(Entity e, CollisionInfo info, int phase)
    {
        var comps = e.Components;
        for (int k = 0; k < comps.Count; k++)
        {
            var comp = comps[k];
            if (!comp.Enabled) continue;
            if (phase == 0) comp.OnCollisionEnter(info);
            else if (phase == 1) comp.OnCollisionStay(info);
            else comp.OnCollisionExit(info);
        }
    }
}
