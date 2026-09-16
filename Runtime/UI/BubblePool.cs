using System;

namespace PixelCore.Runtime.UI;

public static class BubblePool
{
    public const int Size = 4;

    private static readonly DialogueBubble[] _slots = Create();
    private static readonly bool[] _inUse = new bool[Size];

    private static readonly string[] _owner = CreateOwners();

    private static long _clock;

    private static DialogueBubble[] Create()
    {
        var a = new DialogueBubble[Size];
        for (int i = 0; i < Size; i++) a[i] = new DialogueBubble();
        return a;
    }

    private static string[] CreateOwners()
    {
        var a = new string[Size];
        for (int i = 0; i < Size; i++) a[i] = "";
        return a;
    }

    public static string OwnerOf(int slot) => slot >= 0 && slot < Size ? _owner[slot] : "";

    public static int SlotOf(DialogueBubble? bubble) => IndexOf(bubble);

    private static int IndexOf(DialogueBubble? bubble)
    {
        if (bubble == null) return -1;
        for (int i = 0; i < Size; i++) if (ReferenceEquals(_slots[i], bubble)) return i;
        return -1;
    }

    public static int InUseCount => CountInUse();

    private static int CountInUse()
    {
        int n = 0;
        foreach (bool b in _inUse) if (b) n++;
        return n;
    }

    public static DialogueBubble Acquire(string speakerId)
    {
        speakerId ??= "";

        for (int i = 0; i < Size; i++)
            if (speakerId.Length > 0 && _owner[i] == speakerId)
                return Claim(i, speakerId);

        for (int i = 0; i < Size; i++)
            if (!_inUse[i]) return Claim(i, speakerId);

        int oldest = 0;
        for (int i = 1; i < Size; i++)
            if (_slots[i].Stamp < _slots[oldest].Stamp) oldest = i;

        Console.WriteLine(
            $"[Bubble] ⚠ more than {Size} bubbles - closing the oldest one ('{_owner[oldest]}') and " +
            $"giving the slot to '{speakerId}'. Raise the pool size if a scene needs more than {Size} actors speaking at once");

        _slots[oldest].Clear();
        return Claim(oldest, speakerId);
    }

    private static DialogueBubble Claim(int i, string speakerId)
    {
        _inUse[i] = true;
        _owner[i] = speakerId;
        _slots[i].Stamp = ++_clock;
        return _slots[i];
    }

    public static void Release(DialogueBubble? bubble)
    {
        int i = IndexOf(bubble);
        if (i >= 0) _inUse[i] = false;
    }

    public static void ResetAll()
    {
        for (int i = 0; i < Size; i++) { _slots[i].Clear(); _inUse[i] = false; _owner[i] = ""; }
        _clock = 0;
    }
}
