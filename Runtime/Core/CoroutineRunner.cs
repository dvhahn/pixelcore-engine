using System;
using System.Collections.Generic;

namespace PixelCore.Runtime.Core;

public class CoroutineRunner
{
    internal class Routine
    {
        public readonly Stack<IEnumerator<Wait>> Stack = new();
        public float SecondsLeft;
        public int FramesLeft;
        public Func<bool>? Until;
        public bool Done;

        public string Label = "";

        public void Dispose()
        {
            while (Stack.Count > 0)
            {
                var top = Stack.Pop();
                try { top.Dispose(); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Coroutine] ⚠ exception while cleaning up '{Label}': {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }

    private readonly List<Routine> _active = new();

    public int ActiveCount => _active.Count;

    public float Delta { get; private set; }

    public int FaultCount { get; private set; }

    public CoroutineHandle Start(IEnumerator<Wait> routine, string label = "")
    {
        var r = new Routine { Label = label };
        r.Stack.Push(routine);
        _active.Add(r);
        return new CoroutineHandle(r);
    }

    public void StopAll()
    {
        var snapshot = _active.ToArray();
        _active.Clear();
        foreach (var r in snapshot) r.Dispose();
    }

    public void Update(float deltaTime)
    {
        Delta = deltaTime;

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var r = _active[i];
            if (r.Done) { Retire(i, r); continue; }
            Step(r, deltaTime);
            if (r.Done) Retire(i, r);
        }
    }

    private void Retire(int index, Routine r)
    {
        _active.RemoveAt(index);
        r.Dispose();
    }

    private void Step(Routine r, float dt)
    {
        if (r.SecondsLeft > 0f) { r.SecondsLeft -= dt; if (r.SecondsLeft > 0f) return; }
        if (r.FramesLeft > 0) { r.FramesLeft--; if (r.FramesLeft > 0) return; }
        if (r.Until != null) { if (!r.Until()) return; r.Until = null; }

        while (r.Stack.Count > 0)
        {
            var top = r.Stack.Peek();

            bool moved;
            try { moved = top.MoveNext(); }
            catch (Exception ex)
            {
                FaultCount++;
                string who = string.IsNullOrEmpty(r.Label) ? "(unnamed coroutine)" : r.Label;
                Console.WriteLine($"[Coroutine] ✖ '{who}' aborted - {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                r.Done = true;
                return;
            }

            if (!moved)
            {
                r.Stack.Pop().Dispose();
                continue;
            }

            var w = top.Current;
            switch (w.WaitKind)
            {
                case Wait.Kind.Seconds:
                    r.SecondsLeft = w.SecondsValue;
                    if (r.SecondsLeft > 0f) return;
                    break;
                case Wait.Kind.Frames:
                    r.FramesLeft = w.FrameValue;
                    if (r.FramesLeft > 0) return;
                    break;
                case Wait.Kind.Until:
                    r.Until = w.UntilPredicate;
                    if (r.Until != null && !r.Until()) return;
                    r.Until = null;
                    break;
                case Wait.Kind.Sub:
                    if (w.SubRoutine != null) r.Stack.Push(w.SubRoutine);
                    break;
            }
        }

        r.Done = true;
    }

    public class CoroutineHandle
    {
        private readonly Routine _routine;
        internal CoroutineHandle(Routine routine) => _routine = routine;

        public bool IsDone => _routine.Done;

        public void Stop() => _routine.Done = true;
    }
}
