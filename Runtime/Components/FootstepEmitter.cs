using System;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Audio;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Components;

[ComponentCategory(ComponentCategories.Sound)]
public class FootstepEmitter : Component
{
    public const string StepLeft = "step_l";

    public const string StepRight = "step_r";

    public float Volume { get; set; } = 1f;

    public float MoveGrace { get; set; } = 0.12f;

    private const float MoveEpsilon = 0.01f;

    public float SpeedMultiplier => _current?.SpeedMultiplier ?? 1f;

    public StepInfo? LastStep { get; private set; }

    public int StepCount { get; private set; }

    public SurfaceAsset? CurrentSurface => _current;

    public SurfaceArea? CurrentArea { get; private set; }

    public readonly record struct StepInfo(string SurfaceId, string ClipPath, bool Left,
                                           float Volume, float Pitch);

    private readonly System.Collections.Generic.HashSet<string> _warnedClips = new();

    private static readonly Random _rng = new();

    private Animator? _subscribed;
    private SurfaceAsset? _current;
    private Vector2 _prevPos;
    private bool _posPrimed;
    private float _stillTime = float.MaxValue;

    public override void Update(float deltaTime)
    {
        var animator = Entity?.GetComponent<Animator>();
        if (!ReferenceEquals(animator, _subscribed))
        {
            Unsubscribe();
            if (animator != null)
            {
                animator.OnFrameEvent += OnFrameEvent;
                _subscribed = animator;
            }
        }

        var transform = Entity?.GetComponent<Transform>();
        if (transform == null) return;

        if (!_posPrimed) { _prevPos = transform.Position; _posPrimed = true; }
        float moved = Vector2.Distance(transform.Position, _prevPos);
        _prevPos = transform.Position;
        _stillTime = moved > MoveEpsilon ? 0f : _stillTime + deltaTime;

        _current = ResolveSurface(transform.Position);
    }

    private void OnFrameEvent(string eventName)
    {
        if (eventName == StepLeft) PlayStep(left: true);
        else if (eventName == StepRight) PlayStep(left: false);
    }

    private void PlayStep(bool left)
    {
        if (!AudioManager.Instance.ListenerActive) return;

        if (_stillTime > MoveGrace) return;

        var surface = _current;
        if (surface == null) return;

        var clipIds = surface.ClipIdsFor(left);
        if (clipIds.Count == 0) return;

        var clipId = clipIds[_rng.Next(clipIds.Count)];

        var clip = Assets.AssetRegistry.Instance.GetPath(clipId);
        if (string.IsNullOrEmpty(clip))
        {
            if (_warnedClips.Add(clipId))
                Console.Error.WriteLine(
                    $"[Footstep] ✘ audio asset id '{clipId}' is not in the registry " +
                    $"(surface '{surface.Name}', {(left ? "left" : "right")} foot) - the file may have " +
                    "been deleted, or assets.json may not have been rescanned. Check clipIds in the .surface file.");
            return;
        }

        float jitter = 1f - (float)_rng.NextDouble() * MathF.Max(0f, surface.VolumeJitter);
        float volume = Math.Clamp(surface.Volume * Volume * jitter, 0f, 1f);
        float pitch = Math.Clamp(surface.Pitch + ((float)_rng.NextDouble() * 2f - 1f) * surface.PitchJitter,
                                 -1f, 1f);

        AudioManager.Instance.PlaySFX(clip, volume, pitch);

        LastStep = new StepInfo(surface.Id, clip, left, volume, pitch);
        StepCount++;
    }

    private SurfaceAsset? ResolveSurface(Vector2 foot)
    {
        var area = SurfaceArea.FindAt(Entity?.Scene, foot);
        var surface = SurfaceLibrary.Get(area?.SurfaceId ?? Entity?.Scene?.DefaultSurfaceId);
        CurrentArea = surface != null ? area : null;
        return surface;
    }

    private void Unsubscribe()
    {
        if (_subscribed == null) return;
        _subscribed.OnFrameEvent -= OnFrameEvent;
        _subscribed = null;
    }

    public override void OnDisable() => Unsubscribe();
    public override void OnDestroy() => Unsubscribe();
}
