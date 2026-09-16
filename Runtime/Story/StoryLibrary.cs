using System;
using System.Collections.Generic;
using System.IO;

namespace PixelCore.Runtime.Story;

public sealed class StoryLibrary
{
    public const string DefaultRoot = "Content/Story";

    public const string Extension = ".story";

    private readonly Dictionary<string, DialogueBlock> _blocks = new(StringComparer.Ordinal);
    private readonly List<string> _errors = new();

    public readonly List<DialogueScript> Scripts = new();
    public IReadOnlyList<string> Errors => _errors;

    public static StoryLibrary Current { get; private set; } = new();

    public static StoryLibrary LoadDefault(string? root = null)
    {
        Current = Load(root);
        Current.Report();
        return Current;
    }

    internal static void UseLibrary(StoryLibrary lib) => Current = lib;

    public int BlockCount => _blocks.Count;

    public bool TryGetBlock(string id, out DialogueBlock block) => _blocks.TryGetValue(id, out block!);

    public static StoryLibrary Load(string? root = null)
    {
        root ??= DefaultRoot;
        var lib = new StoryLibrary();

        if (!Directory.Exists(root))
        {
            lib._errors.Add($"folder not found: {root}");
            return lib;
        }

        var files = Directory.GetFiles(root, "*" + Extension, SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);

        var namespaces = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in files)
        {
            string name = Path.GetFileName(path);
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception e) { lib._errors.Add($"{name}: read failed - {e.Message}"); continue; }

            var script = StoryParser.Parse(text, name, lib._errors);
            if (script.Namespace.Length == 0) continue;

            if (namespaces.TryGetValue(script.Namespace, out var prevFile))
            {
                lib._errors.Add($"{name}: duplicate namespace '{script.Namespace}' - also declared in {prevFile}"
                    + " (a different folder no longer makes it unique)");
                continue;
            }
            namespaces[script.Namespace] = name;
            lib.Scripts.Add(script);

            foreach (var b in script.Blocks)
            {
                if (lib._blocks.TryGetValue(b.Id, out var prev))
                {
                    lib._errors.Add($"{name}:{b.Line} duplicate block ID '{b.Id}' - {prev.Id} already exists");
                    continue;
                }
                lib._blocks[b.Id] = b;
            }
        }

        return lib;
    }

    public IEnumerable<string> Warnings
    {
        get
        {
            foreach (var s in Scripts)
                foreach (var w in s.Warnings) yield return w;
        }
    }

    public void Report()
    {
        foreach (var e in _errors) Console.WriteLine($"[Story] ⚠ {e}");
        foreach (var w in Warnings) Console.WriteLine($"[Story] note: {w}");
        Console.WriteLine($"[Story] {Scripts.Count} dialogue files, {_blocks.Count} blocks"
            + (_errors.Count > 0 ? $", ⚠ {_errors.Count} errors" : ""));
    }

    public List<string> ValidateEvents()
    {
        var problems = new List<string>();
        foreach (var (block, line, _) in EnumerateText())
        {
            if (line == null) continue;
            foreach (var (key, value) in line.Attributes)
            {
                if (key != DialogueRunner.EventAttribute) continue;
                if (!DialogueEvents.IsRegistered(value))
                    problems.Add($"{block.Id} (line {line.Line}) - unregistered event '{value}'");
            }
        }
        return problems;
    }

    public void ReportEventProblems()
    {
        var problems = ValidateEvents();
        foreach (var p in problems) Console.WriteLine($"[Story] ⚠ {p}");
        if (problems.Count > 0)
            Console.WriteLine($"[Story] ⚠ {problems.Count} unregistered events - those lines play with no staging"
                + (DialogueEvents.Names.Count > 0
                    ? $" (registered: {string.Join(", ", DialogueEvents.Names)})"
                    : " (no events are registered at all)"));
    }

    public IEnumerable<(DialogueBlock Block, DialogueLineNode? Line, DialogueOption? Option)> EnumerateText()
    {
        foreach (var script in Scripts)
            foreach (var block in script.Blocks)
                foreach (var variant in block.Variants)
                    foreach (var item in Walk(block, variant.Nodes))
                        yield return item;
    }

    private static IEnumerable<(DialogueBlock, DialogueLineNode?, DialogueOption?)> Walk(
        DialogueBlock block, List<DialogueNode> nodes)
    {
        foreach (var n in nodes)
        {
            switch (n)
            {
                case DialogueLineNode line:
                    yield return (block, line, null);
                    break;
                case DialogueChoiceNode choice:
                    foreach (var opt in choice.Options)
                    {
                        yield return (block, null, opt);
                        foreach (var item in Walk(block, opt.Nodes)) yield return item;
                    }
                    break;
            }
        }
    }
}
