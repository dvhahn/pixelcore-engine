using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Editor.Commands;

public class CreateEntityCommand : ICommand
{
    private readonly Scene _scene;
    private readonly string _name;
    private readonly int _parentId;
    private int _entityId;
    private Entity? _createdEntity;

    public string Description => $"Create Entity '{_name}'";

    public CreateEntityCommand(Scene scene, string name, Entity? parent = null)
    {
        _scene = scene;
        _name = name;
        _parentId = parent?.Id ?? 0;
    }

    public Entity? CreatedEntity => _createdEntity;

    public void Execute()
    {
        _createdEntity = _entityId > 0
            ? _scene.CreateEntityWithId(_name, _entityId)
            : _scene.CreateEntity(_name);
        _entityId = _createdEntity.Id;

        var parent = CommandTarget.ResolveParentForRestore(_scene, _parentId, Description);
        if (parent != null) _createdEntity.SetParent(parent);
    }

    public void Undo()
    {
        if (_createdEntity != null)
        {
            _scene.DestroyEntity(_createdEntity);
            _createdEntity = null;
        }
    }
}

public class DeleteEntityCommand : ICommand
{
    private readonly Scene _scene;
    private readonly EditorState? _state;
    private readonly int _parentId;
    private readonly string _name;
    private Entity _entity;

    private SubtreeSnapshot? _snapshot;

    public string Description => $"Delete '{_name}'";

    public DeleteEntityCommand(Scene scene, Entity entity, EditorState? state = null)
    {
        _scene = scene;
        _entity = entity;
        _state = state;
        _parentId = entity.Parent?.Id ?? 0;
        _name = entity.Name;
    }

    public void Execute()
    {
        _snapshot = EntitySnapshot.CaptureSubtree(_entity);
        DeselectSubtree(_state, _entity);
        _scene.DestroyEntity(_entity);
    }

    public void Undo()
    {
        if (_snapshot == null) return;
        var parent = CommandTarget.ResolveParentForRestore(_scene, _parentId, $"Delete '{_name}'");
        var restored = EntitySnapshot.RestoreSubtree(_scene, _snapshot, parent, sameIdentity: true);
        if (restored == null) return;
        _entity = restored;
        _state?.Select(_entity);
    }

    internal static void DeselectSubtree(EditorState? state, Entity e)
    {
        if (state == null) return;
        state.Deselect(e);
        foreach (var c in e.Children) DeselectSubtree(state, c);
    }
}

public class SpawnEntityCommand : ICommand
{
    private readonly Scene _scene;
    private readonly EditorState? _state;
    private readonly int _parentId;
    private Entity _entity;

    private SubtreeSnapshot _snapshot;

    public string Description { get; }

    public SpawnEntityCommand(Scene scene, Entity created, string? description = null, EditorState? state = null)
    {
        _scene = scene;
        _entity = created;
        _state = state;
        _parentId = created.Parent?.Id ?? 0;
        _snapshot = EntitySnapshot.CaptureSubtree(created);
        Description = description ?? $"Create '{created.Name}'";
    }

    public void Execute()
    {
        var parent = CommandTarget.ResolveParentForRestore(_scene, _parentId, Description);
        var restored = EntitySnapshot.RestoreSubtree(_scene, _snapshot, parent, sameIdentity: true);
        if (restored == null) return;
        _entity = restored;
        _state?.Select(_entity);
    }

    public void Undo()
    {
        _snapshot = EntitySnapshot.CaptureSubtree(_entity);
        if (_state != null && _state.SelectedEntity == _entity) _state.ClearSelection();
        DeleteEntityCommand.DeselectSubtree(_state, _entity);
        _scene.DestroyEntity(_entity);
    }
}

public class RenameEntityCommand : ICommand
{
    private readonly Scene? _scene;
    private readonly int _entityId;
    private readonly string _oldName;
    private readonly string _newName;

    public string Description => $"Rename '{_oldName}' to '{_newName}'";

    public RenameEntityCommand(Entity entity, string newName)
    {
        _scene = entity.Scene;
        _entityId = entity.Id;
        _oldName = entity.Name;
        _newName = newName;
    }

    public void Execute() => Apply(_newName);
    public void Undo() => Apply(_oldName);

    private void Apply(string name)
    {
        var e = CommandTarget.Resolve(_scene, _entityId, $"Rename '{_oldName}'");
        if (e != null) e.Name = name;
    }
}

public class ReorderEntityCommand : ICommand
{
    private readonly Scene _scene;
    private readonly int _entityId;
    private readonly string _name;
    private readonly int _oldParentId;
    private readonly int _oldIndex;
    private readonly int _newParentId;
    private readonly int _newIndex;

    public string Description => $"Reorder '{_name}'";

    public ReorderEntityCommand(Scene scene, Entity entity, Entity? newParent, int newIndex)
    {
        _scene = scene;
        _entityId = entity.Id;
        _name = entity.Name;
        _oldParentId = entity.Parent?.Id ?? 0;
        _oldIndex = scene.GetSiblingIndex(entity);
        _newParentId = newParent?.Id ?? 0;
        _newIndex = newIndex;
    }

    public void Execute() => Apply(_newParentId, _newIndex);
    public void Undo() => Apply(_oldParentId, System.Math.Max(0, _oldIndex));

    private void Apply(int parentId, int index)
    {
        var e = CommandTarget.Resolve(_scene, _entityId, $"Reorder '{_name}'");
        if (e == null) return;
        if (!CommandTarget.TryResolveParent(_scene, parentId, $"Reorder '{_name}'", out var parent)) return;
        _scene.ReorderSibling(e, parent, index);
    }
}

public class SetParentCommand : ICommand
{
    private readonly Scene? _scene;
    private readonly int _entityId;
    private readonly string _name;
    private readonly int _oldParentId;
    private readonly int _newParentId;
    private readonly string? _newParentName;

    public string Description => _newParentName != null
        ? $"Set parent of '{_name}' to '{_newParentName}'"
        : $"Unparent '{_name}'";

    public SetParentCommand(Entity entity, Entity? newParent)
    {
        _scene = entity.Scene;
        _entityId = entity.Id;
        _name = entity.Name;
        _oldParentId = entity.Parent?.Id ?? 0;
        _newParentId = newParent?.Id ?? 0;
        _newParentName = newParent?.Name;
    }

    public void Execute() => Apply(_newParentId);
    public void Undo() => Apply(_oldParentId);

    private void Apply(int parentId)
    {
        var e = CommandTarget.Resolve(_scene, _entityId, $"Set parent of '{_name}'");
        if (e == null) return;
        if (!CommandTarget.TryResolveParent(_scene, parentId, $"Set parent of '{_name}'", out var parent)) return;
        e.SetParent(parent);
    }
}
