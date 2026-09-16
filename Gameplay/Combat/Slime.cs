using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Cutscenes;
using PixelCore.Runtime.Nav;

namespace PixelCore.Gameplay.Combat;

public class Slime : Component
{
    public const int MaxHp = 3;

    public float InvulnerableTime => 0.2f;
    public float WanderSpeed => 18f;
    public float ChaseSpeed => 40f;
    public float SightRange => 72f;
    public float LoseRange => 120f;
    public float WanderRadius => 28f;
    public float ContactRange => 10f;
    public int ContactDamage => 2;
    public float ContactKnockback => 170f;
    public float RepathInterval => 0.35f;
    public float WaypointReach => 3f;

    public bool IsChasing { get; private set; }

    public Vector2 Facing { get; private set; } = new(0f, 1f);

    public IReadOnlyList<Vector2> Path => _path;

    public int PathIndex => _pathIndex;

    private Health? _health;
    private Vector2? _home;
    private Vector2 _wanderTarget;
    private float _wanderTimer;
    private float _repathTimer;
    private readonly List<Vector2> _path = new();
    private int _pathIndex;
    private Vector2 _move;
    private Random? _random;

    private static bool Frozen => FreezeState.IsFrozen(FreezeFlags.PlayerInput | FreezeFlags.PlayerInputSoft);

    public override void Initialize() => Setup();

    public override void Update(float deltaTime)
    {
        var health = Setup();
        var transform = Entity.GetComponent<Transform>();
        if (transform == null || health is { IsDead: true }) return;

        var position = transform.Position;
        _home ??= position;
        _move = Vector2.Zero;

        if (Frozen)
        {
            IsChasing = false;
            Animate();
            return;
        }

        var player = Entity.Scene?.FindPlayer();
        var playerHealth = player?.GetComponent<Health>();
        var playerPosition = player?.GetComponent<Transform>()?.Position;
        float distance = playerPosition is { } p && playerHealth is not { IsDead: true }
            ? Vector2.Distance(position, p)
            : float.MaxValue;

        bool wasChasing = IsChasing;
        IsChasing = distance <= (wasChasing ? LoseRange : SightRange);
        if (wasChasing && !IsChasing) _wanderTimer = 0f;
        if (!wasChasing && IsChasing) _repathTimer = 0f;

        if (health is not { IsKnockedBack: true })
        {
            if (IsChasing && playerPosition is { } goal) Chase(position, goal, deltaTime);
            else Wander(position, deltaTime);
        }

        if (playerHealth != null && distance <= ContactRange
            && playerHealth.TakeHit(ContactDamage, position, ContactKnockback))
            CombatAssets.Play(CombatAssets.HurtSound);

        Animate();
    }

    public override void FixedTick(float fixedDeltaTime)
    {
        var health = Setup();
        var body = Entity.GetComponent<Rigidbody2D>();
        if (body == null) return;

        if (health is { IsDead: true } || Frozen)
        {
            body.Velocity = Vector2.Zero;
            return;
        }
        body.Velocity = health is { IsKnockedBack: true } ? health.KnockbackVelocity : _move;
    }

    private Health? Setup()
    {
        if (_health != null || Entity == null) return _health;
        _health = Entity.GetComponent<Health>();
        if (_health == null) return null;

        _health.Configure(MaxHp, InvulnerableTime);
        _health.Died += OnDied;
        return _health;
    }

    private void Chase(Vector2 position, Vector2 goal, float deltaTime)
    {
        _repathTimer -= deltaTime;
        if (_repathTimer <= 0f)
        {
            _repathTimer = RepathInterval;
            _path.Clear();
            _pathIndex = 0;
            if (Entity.Scene is { } scene)
            {
                var result = Nav.FindPath(scene, position, goal);
                if (result.Status == NavPathStatus.Path) _path.AddRange(result.Points);
            }
        }

        while (_pathIndex < _path.Count && Vector2.Distance(position, _path[_pathIndex]) <= WaypointReach)
            _pathIndex++;

        var next = _pathIndex < _path.Count ? _path[_pathIndex] : goal;
        _move = Toward(position, next) * ChaseSpeed;
    }

    private void Wander(Vector2 position, float deltaTime)
    {
        _path.Clear();
        _random ??= new Random(Entity.Id * 7919 + 17);

        _wanderTimer -= deltaTime;
        if (_wanderTimer <= 0f)
        {
            _wanderTimer = 1.2f + (float)_random.NextDouble() * 1.6f;
            if (_random.NextDouble() < 0.4)
            {
                _wanderTarget = position;
            }
            else
            {
                double angle = _random.NextDouble() * Math.PI * 2.0;
                float radius = WanderRadius * (float)Math.Sqrt(_random.NextDouble());
                _wanderTarget = (_home ?? position)
                    + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
            }
        }

        if (Vector2.Distance(position, _wanderTarget) > 2f)
            _move = Toward(position, _wanderTarget) * WanderSpeed;
    }

    private void Animate()
    {
        var animator = Entity.GetComponent<Animator>();
        if (animator == null) return;

        bool moving = _move.LengthSquared() > 0.01f;
        if (moving)
            Facing = MathF.Abs(_move.X) > MathF.Abs(_move.Y)
                ? new Vector2(MathF.Sign(_move.X), 0f)
                : new Vector2(0f, MathF.Sign(_move.Y));

        animator.Speed = moving ? 1f : 0.5f;
        animator.Play($"Walk_{Player.PlayerController.FacingSuffix(Facing)}");
    }

    private void OnDied(Health health)
    {
        CombatAssets.Play(CombatAssets.PopSound);

        var scene = Entity?.Scene;
        if (scene == null) return;

        var position = Entity!.GetComponent<Transform>()?.Position ?? Vector2.Zero;
        CombatAssets.SpawnFx(scene, CombatAssets.SmokeFxPrefabId, position);
        scene.DestroyEntity(Entity);
    }

    private static Vector2 Toward(Vector2 from, Vector2 to)
    {
        var delta = to - from;
        return delta.LengthSquared() > 0.25f ? Vector2.Normalize(delta) : Vector2.Zero;
    }
}
