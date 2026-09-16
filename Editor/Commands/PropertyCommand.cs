using System;
using PixelCore.Runtime.Core;

namespace PixelCore.Editor.Commands;

public class PropertyCommand<T> : ICommand
{
    private readonly object _target;
    private readonly string _propertyName;
    private readonly T _oldValue;
    private readonly T _newValue;
    private readonly Action<T> _setter;
    private readonly Entity? _owner;

    public string Description { get; }

    public PropertyCommand(object target, string propertyName, T oldValue, T newValue, Action<T> setter,
        string? customDescription = null, Entity? owner = null)
    {
        _target = target;
        _propertyName = propertyName;
        _oldValue = oldValue;
        _newValue = newValue;
        _setter = setter;
        _owner = owner;

        Description = customDescription ?? $"Change {_propertyName}";
    }

    public void Execute()
    {
        if (CommandTarget.OwnerAlive(_owner, Description)) _setter(_newValue);
    }

    public void Undo()
    {
        if (CommandTarget.OwnerAlive(_owner, Description)) _setter(_oldValue);
    }
}

public class CompositeCommand : ICommand
{
    private readonly ICommand[] _commands;

    public string Description { get; }

    public CompositeCommand(string description, params ICommand[] commands)
    {
        Description = description;
        _commands = commands;
    }

    public void Execute()
    {
        foreach (var command in _commands)
        {
            command.Execute();
        }
    }

    public void Undo()
    {
        for (int i = _commands.Length - 1; i >= 0; i--)
        {
            _commands[i].Undo();
        }
    }
}
