using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Editor.Commands;

public class ConvertToPrefabCommand : ICommand
{
    private readonly Scene _scene;
    private readonly EditorState? _state;
    private readonly string _filePath;
    private readonly int _origParentId;
    private readonly Vector2 _rootPos;

    private readonly SubtreeSnapshot _snapshot;

    private int _rootId;
    private int _instanceId;
    private string? _createdSceneId;

    public string Description { get; }
    public Entity? InstanceEntity => _scene.FindEntityById(_instanceId);

    public ConvertToPrefabCommand(Scene scene, Entity root, string filePath, EditorState? state = null)
    {
        _scene = scene;
        _state = state;
        _filePath = filePath;
        _origParentId = root.Parent?.Id ?? 0;
        _rootPos = root.GetComponent<Transform>()?.Position ?? Vector2.Zero;
        _rootId = root.Id;
        Description = $"Convert '{root.Name}' to Prefab";

        _snapshot = EntitySnapshot.CaptureSubtree(root);
        if (_snapshot.Nodes.Count > 0) _snapshot.Nodes[0].ParentId = 0;
    }

    public void Execute()
    {
        var sceneData = new SceneData
        {
            SchemaVersion = SceneData.CurrentSchemaVersion,
            Name = Path.GetFileNameWithoutExtension(_filePath),
            Kind = SceneKind.Prefab,
        };
        foreach (var ed in _snapshot.Nodes)
        {
            var clone = CloneEntityData(ed);
            foreach (var cd in clone.Components)
                if (cd is TransformData td) { td.X -= _rootPos.X; td.Y -= _rootPos.Y; }
            sceneData.Entities.Add(clone);
        }
        SceneSerializer.SaveToFile(sceneData, _filePath);
        _createdSceneId = AssetRegistry.Instance.GetOrCreateId(_filePath);
        if (!AssetRegistry.Instance.Save())
            System.Console.Error.WriteLine($"[Prefab] could not record the id for '{_filePath}' in the registry - the instance just created cannot find its base on the next boot");

        var live = _scene.FindEntityById(_rootId);
        if (live != null)
        {
            DeleteEntityCommand.DeselectSubtree(_state, live);
            _scene.DestroyEntity(live);
        }

        var host = _instanceId > 0
            ? _scene.CreateEntityWithId(sceneData.Name, _instanceId)
            : _scene.CreateEntity(sceneData.Name);
        _instanceId = host.Id;
        var origParent = CommandTarget.ResolveParentForRestore(_scene, _origParentId, Description);
        if (origParent != null) host.SetParent(origParent);
        host.GetComponent<Transform>()!.Position = _rootPos;
        var inst = host.AddComponent<SceneInstance>();
        inst.SceneId = _createdSceneId;
        inst.Load();
        _state?.Select(host);
    }

    public void Undo()
    {
        var hostLive = _scene.FindEntityById(_instanceId);
        if (hostLive != null)
        {
            DeleteEntityCommand.DeselectSubtree(_state, hostLive);
            _scene.DestroyEntity(hostLive);
        }

        var parent = CommandTarget.ResolveParentForRestore(_scene, _origParentId, Description);
        var restored = EntitySnapshot.RestoreSubtree(_scene, _snapshot, parent, sameIdentity: true);
        if (restored != null)
        {
            _rootId = restored.Id;
            _state?.Select(restored);
        }
    }

    private static EntityData CloneEntityData(EntityData ed)
    {
        var node = System.Text.Json.JsonSerializer.SerializeToNode(ed, SceneJsonContext.Default.EntityData)!;
        return System.Text.Json.JsonSerializer.Deserialize(node, SceneJsonContext.Default.EntityData)!;
    }
}

public class UnpackSceneInstanceCommand : ICommand
{
    private readonly Scene? _scene;
    private readonly int _hostId;
    private readonly string _hostName;
    private readonly EditorState? _state;

    private Entity? _hostAtExecute;
    private string? _sceneId;
    private List<InstanceOverrideData>? _overrides;
    private List<System.Type>? _appliedToHost;
    private List<string>? _appliedGenericToHost;
    private int _rootSourceId;
    private List<Entity> _children = new();
    private Dictionary<Entity, int> _sourceIds = new();

    public string Description => $"Unpack '{_hostName}'";

    public UnpackSceneInstanceCommand(Entity host, EditorState? state = null)
    {
        _scene = host.Scene;
        _hostId = host.Id;
        _hostName = host.Name;
        _state = state;
    }

    public void Execute()
    {
        var host = CommandTarget.Resolve(_scene, _hostId, Description);
        if (host == null) return;
        var inst = host.GetComponent<SceneInstance>();
        if (inst == null) return;
        _hostAtExecute = host;

        _sceneId = inst.SceneId;
        _overrides = inst.Overrides;
        _sourceIds = new Dictionary<Entity, int>();
        foreach (var kv in inst.SourceIds) _sourceIds[kv.Key] = kv.Value;
        _appliedToHost = new List<System.Type>(inst.AppliedToHost);
        _appliedGenericToHost = new List<string>(inst.AppliedGenericToHost);
        _rootSourceId = inst.RootSourceId;

        _children = inst.Release();
        foreach (var c in _children)
            c.HideFromSerialization = false;

        host.RemoveComponent(inst);
    }

    public void Undo()
    {
        var host = CommandTarget.Resolve(_scene, _hostId, Description);
        if (host == null) return;
        if (!ReferenceEquals(host, _hostAtExecute))
        {
            System.Console.Error.WriteLine($"[Undo] target is gone: {Description}");
            return;
        }

        foreach (var c in _children)
            c.HideFromSerialization = true;
        var inst = host.AddComponent<SceneInstance>();
        inst.SceneId = _sceneId;
        inst.Overrides = _overrides;
        inst.Adopt(_children, _sourceIds, _appliedToHost, _rootSourceId, _appliedGenericToHost);
    }
}

public class RevertInstanceOverridesCommand : ICommand
{
    private readonly Scene? _scene;
    private readonly int _hostId;
    private readonly string _hostName;
    private readonly EditorState? _state;
    private readonly int? _entityId;

    private List<InstanceOverrideData>? _before;

    public string Description => _entityId == null
        ? $"Revert overrides '{_hostName}'"
        : $"Revert override '{_hostName}' #{_entityId}";

    public RevertInstanceOverridesCommand(Entity host, int? entityId, EditorState? state = null)
    {
        _scene = host.Scene;
        _hostId = host.Id;
        _hostName = host.Name;
        _entityId = entityId;
        _state = state;
    }

    public void Execute()
    {
        var host = CommandTarget.Resolve(_scene, _hostId, Description);
        if (host == null) return;
        var inst = host.GetComponent<SceneInstance>();
        if (inst == null) return;

        _before = inst.IsLoaded ? InstanceOverrides.Compute(inst) : inst.Overrides;

        List<InstanceOverrideData>? after = null;
        if (_entityId != null && _before != null)
        {
            var filtered = new List<InstanceOverrideData>();
            foreach (var ov in _before)
                if (ov.EntityId != _entityId) filtered.Add(ov);
            after = filtered.Count > 0 ? filtered : null;
        }

        Reload(after);
    }

    public void Undo() => Reload(_before);

    private void Reload(List<InstanceOverrideData>? overrides)
    {
        var host = CommandTarget.Resolve(_scene, _hostId, Description);
        var inst = host?.GetComponent<SceneInstance>();
        if (inst == null) return;
        _state?.ClearSelection();
        inst.Overrides = overrides;
        inst.Load();
        _state?.Select(host!);
        _state?.MarkDirty();
    }
}
