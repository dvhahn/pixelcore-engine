using System;
using System.Collections.Generic;

namespace PixelCore.Runtime.Story;

public static class DialogueEvents
{
    private static readonly Dictionary<string, Action> _handlers = new(StringComparer.Ordinal);

    public static IReadOnlyCollection<string> Names => _handlers.Keys;

    public static void Register(string name, Action handler)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (_handlers.ContainsKey(name))
            Console.WriteLine($"[Story] ⚠ event '{name}' registered twice - the later one wins");
        _handlers[name] = handler;
    }

    public static bool IsRegistered(string name) => _handlers.ContainsKey(name);

    public static void Clear() => _handlers.Clear();

    public static void Fire(string name, string blockId)
    {
        if (!_handlers.TryGetValue(name, out var handler))
        {
            Console.WriteLine($"[Story] ⚠ unregistered event '{name}' ({blockId}) - skipped");
            return;
        }

        try { handler(); }
        catch (Exception e) { Console.WriteLine($"[Story] ⚠ event '{name}' failed ({blockId}) - {e.Message}"); }
    }
}
