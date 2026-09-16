using System;
using System.Collections.Generic;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Editor.Commands;

internal static class ComponentOps
{
    public static Component? AddFrom(Entity e, ComponentData data)
    {
        var before = new HashSet<Component>(e.Components);
        data.Apply(e);
        foreach (var c in e.Components)
            if (!before.Contains(c)) return c;
        return null;
    }
}

public class AddComponentCommand : ICommand
{
    private readonly Scene? _scene;
    private readonly int _entityId;
    private readonly string _name;
    private readonly Type _componentType;
    private Component? _addedComponent;

    public string Description => $"Add {_componentType.Name} to '{_name}'";

    public Component? AddedComponent => _addedComponent;

    public AddComponentCommand(Entity entity, Type componentType)
    {
        _scene = entity.Scene;
        _entityId = entity.Id;
        _name = entity.Name;
        _componentType = componentType;
    }

    public void Execute()
    {
        var e = CommandTarget.Resolve(_scene, _entityId, Description);
        if (e == null) return;
        var method = EditorReflection.AddComponentOpen.MakeGenericMethod(_componentType);
        _addedComponent = (Component?)method.Invoke(e, null);
    }

    public void Undo()
    {
        if (_addedComponent == null) return;
        var e = CommandTarget.Resolve(_scene, _entityId, Description);
        if (e == null) return;
        if (!ReferenceEquals(_addedComponent.Entity, e))
        {
            Console.Error.WriteLine($"[Undo] target is gone: {Description}");
            return;
        }
        e.RemoveComponent(_addedComponent);
        _addedComponent = null;
    }
}

public class AddComponentDataCommand : ICommand
{
    private readonly Scene? _scene;
    private readonly int _entityId;
    private readonly string _name;
    private readonly ComponentData _data;
    private readonly string _label;
    private Component? _added;

    public Component? AddedComponent => _added;
    public string Description => $"{_label} on '{_name}'";

    public AddComponentDataCommand(Entity entity, ComponentData data, string label)
    {
        _scene = entity.Scene;
        _entityId = entity.Id;
        _name = entity.Name;
        _data = data;
        _label = label;
    }

    public void Execute()
    {
        var e = CommandTarget.Resolve(_scene, _entityId, Description);
        if (e != null) _added = ComponentOps.AddFrom(e, _data);
    }

    public void Undo()
    {
        if (_added == null) return;
        var e = CommandTarget.Resolve(_scene, _entityId, Description);
        if (e == null) return;
        if (!ReferenceEquals(_added.Entity, e))
        {
            Console.Error.WriteLine($"[Undo] target is gone: {Description}");
            return;
        }
        e.RemoveComponent(_added);
        _added = null;
    }
}

public class WriteComponentValuesCommand : ICommand
{
    private readonly Component _target;
    private readonly ComponentData _next;
    private readonly ComponentData? _prev;
    private readonly string _label;

    public string Description => $"{_label} {_target.GetType().Name}";

    public WriteComponentValuesCommand(Component target, ComponentData next, string label)
    {
        _target = target;
        _next = next;
        _label = label;
        _prev = ComponentDataRegistry.CreateFor(target);
        _prev?.CaptureInstance(target);
    }

    public void Execute()
    {
        if (CommandTarget.OwnerAlive(_target.Entity, Description)) _next.WriteTo(_target);
    }

    public void Undo()
    {
        if (CommandTarget.OwnerAlive(_target.Entity, Description)) _prev?.WriteTo(_target);
    }
}

public class RemoveComponentCommand : ICommand
{
    private readonly Scene? _scene;
    private readonly int _entityId;
    private readonly string _name;
    private readonly Type _componentType;

    private Component _live;

    private ComponentData? _snapshot;

    public string Description => $"Remove {_componentType.Name} from '{_name}'";

    public RemoveComponentCommand(Entity entity, Component component)
    {
        _scene = entity.Scene;
        _entityId = entity.Id;
        _name = entity.Name;
        _live = component;
        _componentType = component.GetType();
    }

    public void Execute()
    {
        var e = CommandTarget.Resolve(_scene, _entityId, Description);
        if (e == null) return;
        if (!ReferenceEquals(_live.Entity, e))
        {
            Console.Error.WriteLine($"[Undo] target is gone: {Description}");
            return;
        }
        _snapshot = ComponentDataRegistry.CreateFor(_live);
        _snapshot?.CaptureInstance(_live);
        e.RemoveComponent(_live);
    }

    public void Undo()
    {
        var e = CommandTarget.Resolve(_scene, _entityId, Description);
        if (e == null) return;

        if (_snapshot != null)
        {
            var restored = ComponentOps.AddFrom(e, _snapshot);
            if (restored != null) { _live = restored; return; }
        }

        var method = EditorReflection.AddComponentOpen.MakeGenericMethod(_componentType);
        _live = (Component)method.Invoke(e, null)!;
    }
}
