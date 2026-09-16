using System;
using System.Collections.Generic;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Systems;

public static class GameActions
{
    private static readonly Dictionary<string, Action<Entity>> _actions = new(StringComparer.Ordinal);
    private static readonly List<string> _order = new();

    public static IReadOnlyList<string> Ids => _order;

    public static void Register(string id, Action<Entity> body)
    {
        if (string.IsNullOrEmpty(id)) { Console.Error.WriteLine("[Event] ✘ cannot register an empty name"); return; }
        if (!_actions.ContainsKey(id)) _order.Add(id);
        _actions[id] = body;
    }

    public static bool IsRegistered(string id) => !string.IsNullOrEmpty(id) && _actions.ContainsKey(id);

    public static bool TryRun(string id, Entity actor)
    {
        if (!_actions.TryGetValue(id, out var body)) return false;
        body(actor);
        return true;
    }

#if DEBUG
    internal static void ClearRegistry() { _actions.Clear(); _order.Clear(); }
#endif
}
