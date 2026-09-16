using System;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;

namespace PixelCore.Gameplay.Combat;

public class Health : Component
{
    public const int QuartersPerHeart = 4;

    public const int PlayerMax = 3 * QuartersPerHeart;

    public const float KnockbackTime = 0.15f;

    public const float FlashTime = 0.12f;

    public const float BlinkPeriod = 0.08f;

    public static Color FlashColor => new(255, 120, 120);

    public static Color BlinkColor => Color.White * 0.35f;

    public int Max { get; private set; } = PlayerMax;

    public int Current { get; private set; } = PlayerMax;

    public float InvulnerableTime { get; private set; } = 0.8f;

    public bool IsDead => Current <= 0;

    public bool IsInvulnerable => _invulnerable > 0f;

    public bool IsKnockedBack => _knockback > 0f;

    public Vector2 KnockbackVelocity
        => _knockback > 0f ? _knockbackVelocity * (_knockback / KnockbackTime) : Vector2.Zero;

    public event Action<Health>? Damaged;

    public event Action<Health>? Died;

    private float _invulnerable;
    private float _flash;
    private float _knockback;
    private Vector2 _knockbackVelocity;
    private Color? _tint;

    public void Configure(int max, float invulnerableTime)
    {
        Max = Math.Max(1, max);
        Current = Max;
        InvulnerableTime = MathF.Max(0f, invulnerableTime);
    }

    public bool TakeHit(int amount, Vector2 from, float knockbackSpeed)
    {
        if (amount <= 0 || IsDead || IsInvulnerable) return false;

        Current = Math.Max(0, Current - amount);
        _invulnerable = InvulnerableTime;
        _flash = FlashTime;

        var position = Entity?.GetComponent<Transform>()?.Position ?? from;
        var away = position - from;
        away = away.LengthSquared() > 0.0001f ? Vector2.Normalize(away) : new Vector2(0f, 1f);
        _knockbackVelocity = away * MathF.Max(0f, knockbackSpeed);
        _knockback = knockbackSpeed > 0f ? KnockbackTime : 0f;

        ApplyTint();
        Damaged?.Invoke(this);
        if (IsDead) Died?.Invoke(this);
        return true;
    }

    public override void Update(float deltaTime)
    {
        if (_invulnerable > 0f) _invulnerable = MathF.Max(0f, _invulnerable - deltaTime);
        if (_flash > 0f) _flash = MathF.Max(0f, _flash - deltaTime);
        ApplyTint();
    }

    public override void FixedTick(float fixedDeltaTime)
    {
        if (_knockback > 0f) _knockback = MathF.Max(0f, _knockback - fixedDeltaTime);
    }

    public override void OnDisable() => ReleaseTint();

    public override void OnDestroy() => ReleaseTint();

    private Color? WantedTint()
    {
        if (_flash > 0f) return FlashColor;
        if (IsDead || _invulnerable <= 0f) return null;
        return (int)(_invulnerable / BlinkPeriod) % 2 == 0 ? BlinkColor : null;
    }

    private void ApplyTint()
    {
        var sprite = Entity?.GetComponent<SpriteRenderer>();
        if (sprite == null) return;
        if (sprite.ColorOverride != _tint)
        {
            _tint = null;
            return;
        }
        var want = WantedTint();
        sprite.ColorOverride = want;
        _tint = want;
    }

    private void ReleaseTint()
    {
        var sprite = Entity?.GetComponent<SpriteRenderer>();
        if (sprite != null && _tint != null && sprite.ColorOverride == _tint) sprite.ColorOverride = null;
        _tint = null;
    }
}
