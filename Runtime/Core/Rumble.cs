using System;
using Microsoft.Xna.Framework.Input;

namespace PixelCore.Runtime.Core;

public static class Rumble
{
    private static float _strength;
    private static float _remaining;
    private static float _total;
    private static bool _applied;

    public static bool Enabled
    {
        get => _enabled;
        set { _enabled = value; if (!value) StopAll(); }
    }
    private static bool _enabled = true;

    public static bool IsActive => _remaining > 0f;

    public static float CurrentStrength => _remaining > 0f ? _strength * (_total > 0f ? _remaining / _total : 1f) : 0f;

    public static void Play(float strength, float seconds)
    {
        if (!_enabled) return;
        strength = Math.Clamp(strength, 0f, 1f);
        if (strength <= 0f || seconds <= 0f) return;

        _strength = Math.Max(_strength, strength);
        if (seconds > _remaining) { _remaining = seconds; _total = seconds; }
    }

    public static void Update(float dt)
    {
        if (_remaining <= 0f)
        {
            if (_applied) { Apply(0f); _applied = false; }
            return;
        }

        _remaining -= dt;
        if (_remaining <= 0f)
        {
            _remaining = 0f; _strength = 0f; _total = 0f;
            Apply(0f); _applied = false;
            return;
        }

        Apply(CurrentStrength);
        _applied = true;
    }

    public static void Cancel()
    {
        if (_remaining <= 0f && !_applied) return;
        StopAll();
    }

    public static void StopAll()
    {
        _strength = _remaining = _total = 0f;
        Apply(0f);
        _applied = false;
    }

    private static void Apply(float v)
    {
#if DEBUG
        LastApplied = v;
        DeviceTouched = true;
        if (SuppressDevice) return;
#endif
        try { GamePad.SetVibration(Microsoft.Xna.Framework.PlayerIndex.One, v, v); }
        catch {  }
    }

#if DEBUG
    public static float LastApplied { get; private set; }

    public static bool SuppressDevice;

    public static bool DeviceTouched { get; private set; }

    public static void LastAppliedProbeReset() => DeviceTouched = false;

    public static void DebugReset() { StopAll(); _enabled = true; LastApplied = 0f; DeviceTouched = false; }
#endif
}
