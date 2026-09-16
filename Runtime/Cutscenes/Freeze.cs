using System;

namespace PixelCore.Runtime.Cutscenes;

[Flags]
public enum FreezeFlags
{
    None = 0,

    PlayerInput = 1 << 0,

    Interaction = 1 << 1,

    Letterbox = 1 << 2,

    PlayerInputSoft = 1 << 3,

    Cutscene = PlayerInput | Interaction | Letterbox,

    DialogueOnly = PlayerInput | Interaction,
}

public enum FreezeSource
{
    RoomTransition = 0,
    Dialogue = 1,
}

public static class FreezeState
{
    private const int FlagCount = 3;
    private static readonly int[] _counts = new int[FlagCount];

    private static readonly bool[] _soft = new bool[2];

    public static event Action? Changed;

    public static bool IsFrozen(FreezeFlags flag)
    {
        for (int i = 0; i < FlagCount; i++)
            if ((flag & (FreezeFlags)(1 << i)) != 0 && _counts[i] > 0) return true;

        if ((flag & FreezeFlags.PlayerInputSoft) != 0)
            for (int i = 0; i < _soft.Length; i++)
                if (_soft[i]) return true;

        return false;
    }

    public static void SetSoft(FreezeSource source, bool on)
    {
        int i = (int)source;
        if (i < 0 || i >= _soft.Length || _soft[i] == on) return;
        _soft[i] = on;
        Changed?.Invoke();
    }

    public static bool IsSoft(FreezeSource source)
    {
        int i = (int)source;
        return i >= 0 && i < _soft.Length && _soft[i];
    }

    public static int CountOf(FreezeFlags flag)
    {
        for (int i = 0; i < FlagCount; i++)
            if (flag == (FreezeFlags)(1 << i)) return _counts[i];
        return 0;
    }

    internal static void Acquire(FreezeFlags flags)
    {
        bool changed = false;
        for (int i = 0; i < FlagCount; i++)
            if ((flags & (FreezeFlags)(1 << i)) != 0) { _counts[i]++; changed = true; }
        if (changed) Changed?.Invoke();
    }

    internal static void Release(FreezeFlags flags)
    {
        bool changed = false;
        for (int i = 0; i < FlagCount; i++)
        {
            if ((flags & (FreezeFlags)(1 << i)) == 0) continue;
            if (_counts[i] == 0)
            {
                Console.WriteLine($"[Freeze] ⚠ {(FreezeFlags)(1 << i)} released more times than acquired (double Dispose?)");
                continue;
            }
            _counts[i]--; changed = true;
        }
        if (changed) Changed?.Invoke();
    }

    public static void ResetAll()
    {
        bool changed = false;
        for (int i = 0; i < FlagCount; i++) { if (_counts[i] != 0) { _counts[i] = 0; changed = true; } }
        for (int i = 0; i < _soft.Length; i++) { if (_soft[i]) { _soft[i] = false; changed = true; } }
        if (changed) Changed?.Invoke();
    }
}

public readonly struct FreezeScope : IDisposable
{
    private readonly FreezeFlags _flags;

    internal FreezeScope(FreezeFlags flags)
    {
        _flags = flags;
        FreezeState.Acquire(flags);
    }

    public void Dispose() => FreezeState.Release(_flags);
}
