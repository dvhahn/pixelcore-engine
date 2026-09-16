using System;
using Microsoft.Xna.Framework;
using PixelCore.Gameplay.Combat;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Cutscenes;

namespace PixelCore.Gameplay.Player;

public class PlayerController : Component
{
    public static float DefaultMoveSpeed => 80f;

    public float MoveSpeed => DefaultMoveSpeed;

    public float Acceleration => 1000f;
    public float Deceleration => 800f;

    public float AttackTime => 0.28f;
    public int AttackDamage => 1;
    public float AttackKnockback => 200f;

    public static PlayerController? Of(Scene? scene)
        => scene?.FindPlayer()?.GetComponent<PlayerController>();

    private Vector2 _facing = new(0f, 1f);

    public Vector2 Facing
    {
        get => _facing;
        set { if (value != Vector2.Zero) _facing = value; }
    }

    public bool IsAttacking => _attack > 0f;

    private Vector2 _input;
    private float _attack;

    public override void Update(float deltaTime)
    {
        var player = Entity;
        if (player == null) return;

        if (FreezeState.IsFrozen(FreezeFlags.PlayerInput))
        {
            _input = Vector2.Zero;
            _attack = 0f;
            var frozenBody = player.GetComponent<Rigidbody2D>();
            if (frozenBody != null) frozenBody.Velocity = Vector2.Zero;
            return;
        }

        var animator = player.GetComponent<Animator>();
        var health = player.GetComponent<Health>();

        if (health is { IsDead: true })
        {
            _input = Vector2.Zero;
            _attack = 0f;
            if (player.GetComponent<Interactor>() is { Enabled: true } interactor) interactor.Enabled = false;
            if (animator != null && animator.HasClip("Dead"))
            {
                animator.Speed = 1f;
                animator.Play("Dead");
            }
            return;
        }

        if (_attack > 0f) _attack = MathF.Max(0f, _attack - deltaTime);

        _input = ReadInput();

        if (_attack <= 0f && health is not { IsKnockedBack: true } && CanAttack()
            && InputMap.IsPressed(GameAction.Attack))
            StartAttack(player, animator);

        if (_attack > 0f)
        {
            _input = Vector2.Zero;
            player.GetComponent<Interactor>()?.SetFacing(_facing);
            return;
        }

        if (_input != Vector2.Zero) _facing = FacingFor(_input, _facing);

        player.GetComponent<Interactor>()?.SetFacing(_facing);

        if (animator == null) return;

        animator.Speed = 1f;
        string suffix = FacingSuffix(_facing);
        if (_input != Vector2.Zero)
        {
            animator.Play($"Walk_{suffix}");
        }
        else if (animator.HasClip($"Idle_{suffix}"))
        {
            animator.Play($"Idle_{suffix}");
        }
        else if (animator.IsPlaying || animator.CurrentClip?.Name != $"Walk_{suffix}")
        {
            animator.Play($"Walk_{suffix}", restart: true);
            animator.Pause();
        }
    }

    public override void FixedTick(float dt)
    {
        var player = Entity;
        if (player == null) return;
        var rb = player.GetComponent<Rigidbody2D>();
        if (rb == null) return;

        if (FreezeState.IsFrozen(FreezeFlags.PlayerInput))
        {
            rb.Velocity = Vector2.Zero;
            return;
        }

        var health = player.GetComponent<Health>();
        if (health is { IsDead: true })
        {
            rb.Velocity = Vector2.Zero;
            return;
        }
        if (health is { IsKnockedBack: true })
        {
            rb.Velocity = health.KnockbackVelocity;
            return;
        }

        var target = _input * MoveSpeed * SurfaceSpeed(player);
        float rate = _input != Vector2.Zero ? Acceleration : Deceleration;
        rb.Velocity = MoveTowards(rb.Velocity, target, rate * dt);
    }

    private void StartAttack(Entity player, Animator? animator)
    {
        _attack = AttackTime;
        string suffix = FacingSuffix(_facing);

        if (animator != null && animator.HasClip($"Attack_{suffix}"))
        {
            animator.Speed = 1f;
            animator.Play($"Attack_{suffix}", restart: true);
        }

        CombatAssets.Play(CombatAssets.SwingSound);

        var scene = player.Scene;
        if (scene == null) return;

        var feet = player.GetComponent<Transform>()?.Position ?? Vector2.Zero;
        int hits = Melee.Strike(scene, player, Melee.HitBox(feet, _facing), AttackDamage, AttackKnockback);
        CombatAssets.SpawnFx(scene, CombatAssets.SlashFxPrefabId, Melee.SlashPosition(feet, _facing), $"Slash_{suffix}");
        if (hits > 0) CombatAssets.Play(CombatAssets.HitSound);
    }

    private static bool CanAttack()
        => !FreezeState.IsFrozen(FreezeFlags.PlayerInputSoft | FreezeFlags.Interaction);

    private static Vector2 ReadInput()
    {
        if (FreezeState.IsFrozen(FreezeFlags.PlayerInputSoft)) return Vector2.Zero;

        var v = new Vector2(
            (InputMap.IsDown(GameAction.MoveRight) ? 1f : 0f) - (InputMap.IsDown(GameAction.MoveLeft) ? 1f : 0f),
            (InputMap.IsDown(GameAction.MoveDown) ? 1f : 0f) - (InputMap.IsDown(GameAction.MoveUp) ? 1f : 0f));
        return v == Vector2.Zero ? v : Vector2.Normalize(v);
    }

    internal static Vector2 FacingFor(Vector2 input, Vector2 current)
    {
        float ax = MathF.Abs(input.X), ay = MathF.Abs(input.Y);
        var horizontal = new Vector2(MathF.Sign(input.X), 0f);
        var vertical = new Vector2(0f, MathF.Sign(input.Y));
        if (ax > ay) return horizontal;
        if (ay > ax) return vertical;
        return current == horizontal || current == vertical ? current : horizontal;
    }

    private static float SurfaceSpeed(Entity player)
        => player.GetComponent<FootstepEmitter>()?.SpeedMultiplier ?? 1f;

    internal static string FacingSuffix(Vector2 f)
        => f.X < 0f ? "L" : f.X > 0f ? "R" : f.Y < 0f ? "U" : "D";

    private static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDelta)
    {
        var delta = target - current;
        float dist = delta.Length();
        if (dist <= maxDelta || dist == 0f) return target;
        return current + delta * (maxDelta / dist);
    }

#if DEBUG
    public void DebugReset(Vector2 startPos)
    {
        _facing = new Vector2(0f, 1f);
        _input = Vector2.Zero;
        _attack = 0f;
        var rb = Entity?.GetComponent<Rigidbody2D>();
        if (rb != null) rb.Velocity = Vector2.Zero;
    }
#endif
}
