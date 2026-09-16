using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Editor.Commands;

public sealed class SubtreeSnapshot
{
    public List<EntityData> Nodes { get; } = new();
    public List<int> SerializedIds { get; } = new();

    public bool IsEmpty => Nodes.Count == 0;

    public string RootName => Nodes.Count > 0 ? Nodes[0].Name : "";
}

public static class EntitySnapshot
{
    public static EntityData Capture(Entity e) => new EntityData
    {
        Id = e.Id,
        ParentId = e.Parent?.Id ?? 0,
        Name = e.Name,
        Active = e.Active,
        Components = ComponentDataRegistry.CaptureAll(e),
    };

    public static Entity Restore(Scene scene, EntityData data, Entity? parent)
    {
        var e = scene.CreateEntity(data.Name);
        e.Active = data.Active;
        foreach (var c in data.Components)
            c.Apply(e);
        if (parent != null)
            e.SetParent(parent);
        return e;
    }

    public static SubtreeSnapshot CaptureSubtree(Entity root)
    {
        var snap = new SubtreeSnapshot();

        var inSubtree = new HashSet<Entity>();
        void Collect(Entity e) { inSubtree.Add(e); foreach (var c in e.Children) Collect(c); }
        Collect(root);

        void Walk(Entity e, int capturedParentId)
        {
            bool skip = e != root
                        && e.HideFromSerialization
                        && HostOf(e) is { } host && inSubtree.Contains(host);

            int nextParentId = capturedParentId;
            if (!skip)
            {
                var data = CaptureNode(e);
                data.ParentId = capturedParentId;
                snap.Nodes.Add(data);
                snap.SerializedIds.Add(e.SerializedId);
                nextParentId = e.Id;
            }

            foreach (var c in e.Children) Walk(c, nextParentId);
        }
        Walk(root, 0);

        return snap;
    }

    private static EntityData CaptureNode(Entity e)
    {
        var data = Capture(e);
        if (e.GetComponent<SceneInstance>() is { RootMerged: true } inst)
            data.Components = data.Components.FindAll(d => !inst.IsFromBase(d.GetType()));
        return data;
    }

    private static Entity? HostOf(Entity e)
    {
        for (var p = e.Parent; p != null; p = p.Parent)
            if (p.GetComponent<SceneInstance>() is { } inst && inst.InstancedEntities.Contains(e))
                return p;
        return null;
    }

    public static Entity? RestoreSubtree(Scene scene, SubtreeSnapshot snap, Entity? parent, bool sameIdentity)
    {
        if (snap.Nodes.Count == 0) return null;

        var idMap = new Dictionary<int, Entity>();
        Entity? root = null;

        for (int i = 0; i < snap.Nodes.Count; i++)
        {
            var d = snap.Nodes[i];
            var e = sameIdentity ? scene.CreateEntityWithId(d.Name, d.Id) : scene.CreateEntity(d.Name);
            e.SerializedId = sameIdentity ? snap.SerializedIds[i] : 0;
            e.Active = d.Active;
            foreach (var c in d.Components)
                c.Apply(e);

            idMap[d.Id] = e;
            e.SetParent(idMap.TryGetValue(d.ParentId, out var p) ? p : parent);

            if (i == 0) root = e;
        }

        return root;
    }

    public static List<Entity> TopLevel(IEnumerable<Entity> entities)
    {
        var set = new HashSet<Entity>(entities);
        var result = new List<Entity>();
        foreach (var e in entities)
        {
            bool nested = false;
            for (var p = e.Parent; p != null; p = p.Parent)
                if (set.Contains(p)) { nested = true; break; }
            if (!nested && !result.Contains(e)) result.Add(e);
        }
        return result;
    }

    public static void MoveSubtreeBy(Entity root, Vector2 delta)
    {
        if (delta == Vector2.Zero) return;
        void Shift(Entity e)
        {
            if (e.GetComponent<Transform>() is { } t) t.Position += delta;
            foreach (var c in e.Children) Shift(c);
        }
        Shift(root);
    }

    public static Entity Duplicate(Scene scene, Entity source, EditorState state)
    {
        var snap = CaptureSubtree(source);
        var duplicate = RestoreSubtree(scene, snap, source.Parent, sameIdentity: false)!;
        duplicate.Name = MakeCopyName(scene, source.Name);

        scene.ReorderSibling(duplicate, source.Parent, scene.GetSiblingIndex(source) + 1);

        state.Select(duplicate);
        state.CommandHistory.AddExecuted(new SpawnEntityCommand(scene, duplicate, $"Duplicate '{source.Name}'", state));
        state.MarkDirty();
        return duplicate;
    }

    public static List<Entity> DuplicateMany(Scene scene, IEnumerable<Entity> sources, EditorState state)
    {
        var roots = TopLevel(sources);
        var created = new List<Entity>();
        if (roots.Count == 0) return created;

        if (roots.Count == 1)
        {
            created.Add(Duplicate(scene, roots[0], state));
            return created;
        }

        var cmds = new List<ICommand>();
        foreach (var src in roots)
        {
            var dup = RestoreSubtree(scene, CaptureSubtree(src), src.Parent, sameIdentity: false);
            if (dup == null) continue;
            dup.Name = MakeCopyName(scene, src.Name);
            scene.ReorderSibling(dup, src.Parent, scene.GetSiblingIndex(src) + 1);
            created.Add(dup);
            cmds.Add(new SpawnEntityCommand(scene, dup, $"Duplicate '{src.Name}'", state));
        }
        if (created.Count == 0) return created;

        state.SelectMany(created);
        state.CommandHistory.AddExecuted(new CompositeCommand($"Duplicate {created.Count} entities", cmds.ToArray()));
        state.MarkDirty();
        return created;
    }

    public static string MakeCopyName(Scene scene, string sourceName)
    {
        scene.FlushPendingAdds();

        var baseName = sourceName;
        while (baseName.EndsWith(" (Copy)"))
            baseName = baseName[..^7];

        var m = System.Text.RegularExpressions.Regex.Match(baseName, @"^(.*) \((\d+)\)$");
        int n = 1;
        if (m.Success)
        {
            baseName = m.Groups[1].Value;
            n = int.Parse(m.Groups[2].Value) + 1;
        }

        while (scene.FindEntity($"{baseName} ({n})") != null)
            n++;
        return $"{baseName} ({n})";
    }
}
