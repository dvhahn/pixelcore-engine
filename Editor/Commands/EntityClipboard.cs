using System.Collections.Generic;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;

namespace PixelCore.Editor.Commands;

public static class EntityClipboard
{
    private static readonly List<SubtreeSnapshot> _items = new();

    public static int Count => _items.Count;
    public static bool HasValue => _items.Count > 0;

    public static void Copy(IEnumerable<Entity> entities)
    {
        var roots = EntitySnapshot.TopLevel(entities);
        if (roots.Count == 0) return;

        _items.Clear();
        foreach (var e in roots)
            _items.Add(EntitySnapshot.CaptureSubtree(e));
    }

    public static List<Entity> Paste(Scene scene, Entity? parent, EditorState state)
    {
        var created = new List<Entity>();
        if (_items.Count == 0) return created;

        var offset = new Vector2(state.GridSize, state.GridSize);
        var cmds = new List<ICommand>();

        foreach (var snap in _items)
        {
            var root = EntitySnapshot.RestoreSubtree(scene, snap, parent, sameIdentity: false);
            if (root == null) continue;

            root.Name = EntitySnapshot.MakeCopyName(scene, root.Name);
            EntitySnapshot.MoveSubtreeBy(root, offset);

            created.Add(root);
            cmds.Add(new SpawnEntityCommand(scene, root, "Paste", state));
        }

        if (created.Count == 0) return created;

        state.SelectMany(created);
        state.CommandHistory.AddExecuted(cmds.Count == 1
            ? cmds[0]
            : new CompositeCommand($"Paste {created.Count} entities", cmds.ToArray()));
        state.MarkDirty();
        return created;
    }
}
