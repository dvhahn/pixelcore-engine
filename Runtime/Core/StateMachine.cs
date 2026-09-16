using System;
using System.Collections.Generic;

namespace PixelCore.Runtime.Core;

public class StateMachine<TState> where TState : notnull
{
    private class State
    {
        public Action? OnEnter;
        public Action<float>? OnUpdate;
        public Action? OnExit;
    }

    private readonly Dictionary<TState, State> _states = new();
    private bool _started;

    public TState Current { get; private set; } = default!;

    public event Action<TState, TState>? OnTransition;

    public StateMachine<TState> Add(TState id, Action? onEnter = null, Action<float>? onUpdate = null, Action? onExit = null)
    {
        _states[id] = new State { OnEnter = onEnter, OnUpdate = onUpdate, OnExit = onExit };
        return this;
    }

    public void Start(TState id)
    {
        Current = id;
        _started = true;
        if (_states.TryGetValue(id, out var s)) s.OnEnter?.Invoke();
    }

    public void ChangeState(TState id)
    {
        if (_started && EqualityComparer<TState>.Default.Equals(Current, id))
            return;

        if (_started && _states.TryGetValue(Current, out var cur))
            cur.OnExit?.Invoke();

        var from = Current;
        Current = id;
        _started = true;

        if (_states.TryGetValue(id, out var next))
            next.OnEnter?.Invoke();

        OnTransition?.Invoke(from, id);
    }

    public void Update(float deltaTime)
    {
        if (_started && _states.TryGetValue(Current, out var s))
            s.OnUpdate?.Invoke(deltaTime);
    }

    public bool Is(TState id) => _started && EqualityComparer<TState>.Default.Equals(Current, id);
}
