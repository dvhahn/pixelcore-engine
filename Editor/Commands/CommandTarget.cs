using System;
using PixelCore.Runtime.Core;

namespace PixelCore.Editor.Commands;

internal static class CommandTarget
{
    public static Entity? Resolve(Scene? scene, int id, string what)
    {
        var e = scene?.FindEntityById(id);
        if (e == null) Console.Error.WriteLine($"[Undo] target is gone: {what}");
        return e;
    }

    public static bool TryResolveParent(Scene? scene, int parentId, string what, out Entity? parent)
    {
        parent = null;
        if (parentId == 0) return true;
        parent = scene?.FindEntityById(parentId);
        if (parent != null) return true;
        Console.Error.WriteLine($"[Undo] parent is missing: {what}");
        return false;
    }

    public static Entity? ResolveParentForRestore(Scene? scene, int parentId, string what)
    {
        if (parentId == 0) return null;
        var p = scene?.FindEntityById(parentId);
        if (p == null) Console.Error.WriteLine($"[Undo] parent is gone, restored at the root: {what}");
        return p;
    }

    public static bool OwnerAlive(Entity? owner, string what)
    {
        if (owner == null) return true;
        var live = owner.Scene?.FindEntityById(owner.Id);
        if (ReferenceEquals(live, owner)) return true;
        Console.Error.WriteLine($"[Undo] target is gone: {what}");
        return false;
    }
}
