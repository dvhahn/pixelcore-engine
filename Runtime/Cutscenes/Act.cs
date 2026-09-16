using System.Collections.Generic;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Cutscenes;

public readonly struct Act
{
    public IEnumerator<Wait> Routine { get; }

    public Act(IEnumerator<Wait> routine) => Routine = routine;

    public static implicit operator Wait(Act act) => Wait.For(act.Routine);
}

public readonly struct ForkHandle
{
    private readonly CoroutineRunner.CoroutineHandle? _handle;

    internal ForkHandle(CoroutineRunner.CoroutineHandle? handle) => _handle = handle;

    public bool IsDone => _handle?.IsDone ?? true;

    public void Stop() => _handle?.Stop();

    public Wait Join()
    {
        var h = _handle;
        return Wait.Until(() => h?.IsDone ?? true);
    }
}
