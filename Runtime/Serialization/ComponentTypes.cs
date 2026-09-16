using System;
using System.Collections.Generic;
using System.Linq;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Serialization;

public static partial class ComponentTypes
{
    private static readonly Dictionary<string, (Type Type, Func<Component> Factory)> _factories = new(StringComparer.Ordinal);

    static ComponentTypes() => RegisterGenerated();

    static partial void RegisterGenerated();

    public static void Register(string fullName, Type type, Func<Component> factory) => _factories[fullName] = (type, factory);

    public static Component? Create(string fullName)
        => _factories.TryGetValue(fullName, out var e) ? e.Factory() : null;

    public static bool IsRegistered(Type type) => type.FullName != null && _factories.ContainsKey(type.FullName);
    public static bool IsRegistered(string fullName) => _factories.ContainsKey(fullName);

    public static IReadOnlyCollection<string> Names => _factories.Keys;
    public static IEnumerable<Type> Types => _factories.Values.Select(v => v.Type);
    public static int Count => _factories.Count;
}
