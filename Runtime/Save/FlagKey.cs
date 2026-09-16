using System;
using System.Collections.Generic;

namespace PixelCore.Runtime.Save;

public enum FlagLifetime
{
    Permanent,
    Day,
    Phase,
}

public readonly struct FlagKey
{
    public string Name { get; }

    public FlagKey(string name, FlagLifetime lifetime = FlagLifetime.Permanent)
    {
        Name = name;
        FlagRegistry.Declare(name, lifetime);
    }

    public static implicit operator string(FlagKey key) => key.Name;
    public override string ToString() => Name;
}

public static class FlagRegistry
{
    private static readonly Dictionary<string, FlagLifetime> _lifetimes = new();

    public static int DeclaredCount => _lifetimes.Count;

    public static void Declare(string key, FlagLifetime lifetime)
    {
        if (string.IsNullOrEmpty(key))
        {
            Console.WriteLine("[Save] ⚠ an empty flag key was declared - ignoring it");
            return;
        }

        if (_lifetimes.TryGetValue(key, out var existing) && existing != lifetime)
        {
            Console.WriteLine($"[Save] ⚠ flag '{key}' declared with two different lifetimes: {existing} then {lifetime} (the later one wins)");
        }

        _lifetimes[key] = lifetime;
    }

    public static FlagLifetime LifetimeOf(string key)
        => _lifetimes.TryGetValue(key, out var lifetime) ? lifetime : FlagLifetime.Permanent;

    public static bool IsDeclared(string key) => _lifetimes.ContainsKey(key);

    public static IReadOnlyDictionary<string, FlagLifetime> All => _lifetimes;

    internal static void ClearForTest() => _lifetimes.Clear();
}
