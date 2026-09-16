using System;
using System.Collections.Generic;

namespace PixelCore.Runtime.Core;

public struct Wait
{
    internal enum Kind { Seconds, Frames, Until, Sub }

    internal Kind WaitKind;
    internal float SecondsValue;
    internal int FrameValue;
    internal Func<bool>? UntilPredicate;
    internal IEnumerator<Wait>? SubRoutine;

    public static Wait NextFrame => new Wait { WaitKind = Kind.Frames, FrameValue = 1 };

    public static Wait Frames(int n) => new Wait { WaitKind = Kind.Frames, FrameValue = n };

    public static Wait Seconds(float seconds) => new Wait { WaitKind = Kind.Seconds, SecondsValue = seconds };

    public static Wait Until(Func<bool> predicate) => new Wait { WaitKind = Kind.Until, UntilPredicate = predicate };

    public static Wait For(IEnumerator<Wait> sub) => new Wait { WaitKind = Kind.Sub, SubRoutine = sub };
}
