using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Editor;

public static class ComponentCatalog
{
    public readonly record struct Entry(string Name, Type Type, string Category);

    public static readonly string[] Order =
    {
        ComponentCategories.Basic, ComponentCategories.Render, ComponentCategories.Physics,
        ComponentCategories.Interaction, ComponentCategories.Sound, ComponentCategories.Game,
        ComponentCategories.Other,
    };

    public static IReadOnlyList<Entry> All { get; } = ComponentTypes.Types
        .Select(t => new Entry(DisplayName(t), t, CategoryOf(t)))
        .Where(e => e.Category != ComponentCategories.Hidden)
        .OrderBy(e => Rank(e.Category))
        .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static string DisplayName(Type t) => DisplayName(t.Name);

    public static string DisplayName(string typeName)
    {
        var sb = new StringBuilder(typeName.Length + 4);
        for (int i = 0; i < typeName.Length; i++)
        {
            char c = typeName[i];
            if (i > 0)
            {
                char prev = typeName[i - 1];
                if ((char.IsUpper(c) && char.IsLower(prev)) || (char.IsDigit(c) && char.IsLetter(prev)))
                    sb.Append(' ');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static string CategoryOf(Type type)
    {
        if (Attribute.GetCustomAttribute(type, typeof(ComponentCategoryAttribute), inherit: false)
            is ComponentCategoryAttribute attr)
            return attr.Category;
        return IsEngine(type) ? ComponentCategories.Other : ComponentCategories.Game;
    }

    public static bool IsEngine(Type type)
        => (type.Namespace ?? "").StartsWith("PixelCore.Runtime", StringComparison.Ordinal);

    public static int Rank(string category)
    {
        int i = Array.IndexOf(Order, category);
        return i < 0 ? Order.Length : i;
    }

    public static IReadOnlyList<Entry> Search(string? query)
    {
        string q = Normalize(query);
        if (q.Length == 0) return All;
        return All.Where(e => Normalize(e.Name).Contains(q, StringComparison.Ordinal)
                           || Normalize(e.Type.Name).Contains(q, StringComparison.Ordinal)
                           || Normalize(e.Category).Contains(q, StringComparison.Ordinal))
                  .ToArray();
    }

    public static IReadOnlyList<(string Category, IReadOnlyList<Entry> Items)> Grouped(string? query)
        => Search(query)
            .GroupBy(e => e.Category)
            .OrderBy(g => Rank(g.Key))
            .Select(g => (g.Key, (IReadOnlyList<Entry>)g.ToArray()))
            .ToArray();

    private static string Normalize(string? s)
        => string.IsNullOrEmpty(s) ? "" : s.Replace(" ", "").Replace("·", "").ToLowerInvariant();
}
