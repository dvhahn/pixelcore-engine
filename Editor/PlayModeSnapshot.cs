using System.Collections.Generic;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;

namespace PixelCore.Editor;

public class PlayModeSnapshot
{
    private readonly Dictionary<Entity, EntitySnapshot> _snapshots = new();

    public int ForgottenCount { get; private set; }

    public void Forget(Entity entity)
    {
        if (IsPlayer(entity)) return;
        if (_snapshots.Remove(entity)) ForgottenCount++;
    }

    private static bool IsPlayer(Entity entity) => entity.Name == Scene.PlayerName;

    public void Capture(Scene scene)
    {
        _snapshots.Clear();
        ForgottenCount = 0;

        foreach (var entity in scene.Entities)
        {
            var snapshot = new EntitySnapshot { Active = entity.Active };

            var transform = entity.GetComponent<Transform>();
            if (transform != null)
            {
                snapshot.Position = transform.Position;
                snapshot.Rotation = transform.Rotation;
            }

            var rb = entity.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                snapshot.Velocity = rb.Velocity;
                snapshot.IsGrounded = rb.IsGrounded;
            }

            var sprite = entity.GetComponent<SpriteRenderer>();
            if (sprite != null) snapshot.FrameOverride = sprite.FrameOverride;

            var animator = entity.GetComponent<Animator>();
            if (animator != null) snapshot.Playback = animator.CapturePlayback();

            _snapshots[entity] = snapshot;
        }
    }

    public void Restore(Scene scene)
    {
        foreach (var entity in scene.Entities)
        {
            if (!_snapshots.TryGetValue(entity, out var snapshot))
                continue;

            entity.Active = snapshot.Active;

            var transform = entity.GetComponent<Transform>();
            if (transform != null)
            {
                transform.Position = snapshot.Position;
                transform.Rotation = snapshot.Rotation;
            }

            var rb = entity.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.Velocity = snapshot.Velocity;
                rb.IsGrounded = snapshot.IsGrounded;
            }

            var animator = entity.GetComponent<Animator>();
            if (animator != null && snapshot.Playback.HasValue)
                animator.RestorePlayback(snapshot.Playback.Value);

            var sprite = entity.GetComponent<SpriteRenderer>();
            if (sprite != null) sprite.FrameOverride = snapshot.FrameOverride;
        }
    }

    public void Clear()
    {
        _snapshots.Clear();
    }

    private class EntitySnapshot
    {
        public bool Active;

        public Vector2 Position;
        public float Rotation;

        public Vector2 Velocity;
        public bool IsGrounded;

        public SpriteRenderer.FrameOutput? FrameOverride;
        public Animator.PlaybackState? Playback;
    }
}
