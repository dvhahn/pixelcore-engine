using System.Collections.Generic;
using System.IO;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Runtime.Components;

[ComponentCategory(ComponentCategories.Hidden)]
public class SceneInstance : Component
{
    public string? SceneId { get; set; }

    public static string ContentRoot { get; set; } = Assets.ContentPaths.Root;

    public string? ScenePath
    {
        get => ResolveSceneId(SceneId);
        set
        {
            if (value != null)
                SceneId = AssetRegistry.Instance.GetOrCreateId(value);
            else
                SceneId = null;
        }
    }

    public static string? ResolveSceneId(string? sceneId)
        => sceneId != null ? Resolve(AssetRegistry.Instance.GetPath(sceneId)) : null;

    private static string? Resolve(string? rel)
    {
        if (string.IsNullOrEmpty(rel)) return rel;
        var full = Path.Combine(ContentRoot, rel);
        return File.Exists(full) ? full : rel;
    }

    public bool IsLoaded { get; private set; }

    public SceneKind? BaseKind { get; private set; }

    public List<InstanceOverrideData>? Overrides { get; set; }

    private List<Entity> _instancedEntities = new();

    public IReadOnlyList<Entity> InstancedEntities => _instancedEntities;

    private readonly Dictionary<Entity, int> _sourceIds = new();

    public IReadOnlyDictionary<Entity, int> SourceIds => _sourceIds;

    private readonly HashSet<System.Type> _appliedToHost = new();
    private readonly HashSet<string> _appliedGenericToHost = new(System.StringComparer.Ordinal);

    public bool IsFromBase(System.Type componentDataType) => _appliedToHost.Contains(componentDataType);

    public bool IsFromBase(ComponentData d)
        => d is GenericComponentData g ? _appliedGenericToHost.Contains(g.Name) : _appliedToHost.Contains(d.GetType());

    public int RootSourceId { get; private set; }

    public bool RootMerged { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        if (SceneId != null && !IsLoaded)
        {
            Load();
        }
    }

    private static readonly HashSet<string> _loadingPaths = new();

    private static readonly HashSet<string> _schemaWarned = new();

    private static void WarnIfUnmigrated(Serialization.SceneData data, string path)
    {
        if (data.SchemaVersion >= Serialization.SceneData.CurrentSchemaVersion) return;
        if (!_schemaWarned.Add(path)) return;
        System.Console.Error.WriteLine(
            $"[SceneInstance] ✘ prefab uses the old schema: {path} " +
            $"(schemaVersion {data.SchemaVersion} < {Serialization.SceneData.CurrentSchemaVersion}) — " +
            "The instance path does not run pivot or scale migration. " +
            "Opening that scene in the editor once and saving it promotes the file.");
    }

    public void Load()
    {
        if (string.IsNullOrEmpty(SceneId))
            return;

        var path = ScenePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            System.Console.WriteLine($"[SceneInstance] Scene not found: {path ?? SceneId} " +
                                     $"(not found relative to content root '{ContentRoot}' either)");
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (!_loadingPaths.Add(fullPath))
        {
            System.Console.WriteLine($"[SceneInstance] circular reference detected, load aborted: {path}");
            return;
        }

        try
        {
            Unload();

            var sceneData = SceneSerializer.LoadFromFile(path);
            if (sceneData == null)
            {
                System.Console.WriteLine($"[SceneInstance] Failed to load: {path}");
                return;
            }

            BaseKind = sceneData.Kind;
            WarnIfUnmigrated(sceneData, path);

            var hostPos = Entity.GetComponent<Transform>()?.Position ?? Microsoft.Xna.Framework.Vector2.Zero;

            Dictionary<int, InstanceOverrideData>? ovById = null;
            var deleted = new HashSet<int>();
            if (Overrides != null)
            {
                ovById = new Dictionary<int, InstanceOverrideData>();
                foreach (var ov in Overrides)
                {
                    ovById[ov.EntityId] = ov;
                    if (ov.Deleted == true) deleted.Add(ov.EntityId);
                }
                bool grew = deleted.Count > 0;
                while (grew)
                {
                    grew = false;
                    foreach (var ed in sceneData.Entities)
                        if (!deleted.Contains(ed.Id) && ed.ParentId != 0 && deleted.Contains(ed.ParentId))
                        { deleted.Add(ed.Id); grew = true; }
                }
            }

            EntityData? mergedRoot = null;
            if (sceneData.Kind == SceneKind.Prefab)
            {
                var roots = sceneData.Entities.FindAll(e => e.ParentId == 0 && !deleted.Contains(e.Id));
                if (roots.Count == 1) mergedRoot = roots[0];
                else if (roots.Count > 1)
                    System.Console.WriteLine(
                        $"[SceneInstance] ⚠ the prefab has {roots.Count} roots: {path} - skipping direct root " +
                        "(a prefab is meant to have exactly one; wrap them in a single empty entity)");
            }

            if (mergedRoot != null)
            {
                InstanceOverrideData? rootOv = null;
                ovById?.TryGetValue(mergedRoot.Id, out rootOv);

                RootMerged = true;
                RootSourceId = mergedRoot.Id;
                _sourceIds[Entity] = mergedRoot.Id;

                foreach (var comp in InstanceOverrides.BuildEffective(mergedRoot, rootOv))
                {
                    if (comp is TransformData) continue;
                    comp.Apply(Entity);
                    if (comp is GenericComponentData g) _appliedGenericToHost.Add(g.Name);
                    else _appliedToHost.Add(comp.GetType());
                }
            }

            var idMap = new Dictionary<int, Entity>();
            if (mergedRoot != null) idMap[mergedRoot.Id] = Entity;
            foreach (var entityData in sceneData.Entities)
            {
                if (deleted.Contains(entityData.Id)) continue;
                if (mergedRoot != null && entityData.Id == mergedRoot.Id) continue;

                InstanceOverrideData? ov = null;
                ovById?.TryGetValue(entityData.Id, out ov);

                var childEntity = Entity.Scene?.CreateEntity(ov?.Name ?? entityData.Name);
                if (childEntity == null) continue;

                childEntity.SetParent(Entity);

                childEntity.HideFromSerialization = true;
                childEntity.Active = ov?.Active ?? entityData.Active;

                var effective = InstanceOverrides.BuildEffective(entityData, ov);

                foreach (var comp in effective)
                    if (comp is TransformData) comp.Apply(childEntity);
                var t = childEntity.GetComponent<Transform>();
                if (t != null) t.Position += hostPos;
                foreach (var comp in effective)
                    if (comp is not TransformData) comp.Apply(childEntity);

                idMap[entityData.Id] = childEntity;
                _sourceIds[childEntity] = entityData.Id;
                _instancedEntities.Add(childEntity);
            }

            foreach (var entityData in sceneData.Entities)
            {
                if (entityData.ParentId == 0) continue;
                if (idMap.TryGetValue(entityData.Id, out var child)
                    && idMap.TryGetValue(entityData.ParentId, out var parent))
                    child.SetParent(parent);
            }

            IsLoaded = true;
        }
        finally
        {
            _loadingPaths.Remove(fullPath);
        }
    }

    public void Unload()
    {
        foreach (var entity in _instancedEntities)
        {
            Entity.Scene?.DestroyEntity(entity);
        }
        _instancedEntities.Clear();
        _sourceIds.Clear();

        if ((_appliedToHost.Count > 0 || _appliedGenericToHost.Count > 0) && Entity != null)
        {
            foreach (var c in new List<Component>(Entity.Components))
            {
                var data = ComponentDataRegistry.CreateFor(c);
                bool fromBase = data != null && IsFromBase(data);
                if (fromBase && c is not SceneInstance)
                    Entity.RemoveComponent(c);
            }
            _appliedToHost.Clear();
            _appliedGenericToHost.Clear();
        }
        RootMerged = false;
        RootSourceId = 0;
        IsLoaded = false;
    }

    public List<Entity> Release()
    {
        var released = new List<Entity>(_instancedEntities);
        _instancedEntities.Clear();
        _sourceIds.Clear();
        _appliedToHost.Clear();
        _appliedGenericToHost.Clear();
        RootMerged = false;
        RootSourceId = 0;
        IsLoaded = false;
        return released;
    }

    public void Adopt(List<Entity> children, IReadOnlyDictionary<Entity, int> sourceIds,
                      IEnumerable<System.Type>? appliedToHost = null, int rootSourceId = 0,
                      IEnumerable<string>? appliedGenericToHost = null)
    {
        _instancedEntities.Clear();
        _sourceIds.Clear();
        _appliedToHost.Clear();
        _appliedGenericToHost.Clear();
        _instancedEntities.AddRange(children);
        foreach (var kv in sourceIds) _sourceIds[kv.Key] = kv.Value;
        if (appliedToHost != null)
            foreach (var t in appliedToHost) _appliedToHost.Add(t);
        if (appliedGenericToHost != null)
            foreach (var n in appliedGenericToHost) _appliedGenericToHost.Add(n);
        RootSourceId = rootSourceId;
        RootMerged = rootSourceId != 0;
        IsLoaded = true;
    }

    public IReadOnlyCollection<System.Type> AppliedToHost => _appliedToHost;

    public IReadOnlyCollection<string> AppliedGenericToHost => _appliedGenericToHost;

    public override void OnDestroy()
    {
        Unload();
        base.OnDestroy();
    }
}
