using System;
using System.Collections.Generic;
using System.IO;

namespace PixelCore.Runtime.Text;

public static class Loc
{
    public const string SourceLanguage = "ko";

    public const string DefaultRoot = "Content/Strings";

    private static StringTable _table = new();
    private static Dictionary<string, string> _overlay = new(StringComparer.Ordinal);
    private static readonly List<string> _missing = new();
    private static readonly HashSet<string> _seenMissing = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _seenProblem = new(StringComparer.Ordinal);

    public static string Root { get; private set; } = DefaultRoot;

    public static string Language { get; private set; } = SourceLanguage;

    public static bool Ready { get; private set; }

    public static IReadOnlyList<string> Errors => _table.Errors;

    public static IReadOnlyList<string> Missing => _missing;

    public static int Count => _table.Count;

    public static void Load(string? root = null)
    {
        Root = root ?? DefaultRoot;
        _table = StringTable.LoadDirectory(Root);
        _missing.Clear();
        _seenMissing.Clear();
        _seenProblem.Clear();
        Ready = true;

        foreach (var e in _table.Errors) Console.WriteLine($"[Loc] ⚠ {e}");
        foreach (var p in _table.Lint()) Console.WriteLine($"[Loc] ⚠ {p}");

        if (Language != SourceLanguage) LoadOverlay(Language);

        Console.WriteLine($"[Loc] {_table.Count} labels, language {Language}"
            + (_table.Errors.Count > 0 ? $" - ⚠ {_table.Errors.Count} error(s)" : ""));
    }

    public static bool SetLanguage(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return false;
        Language = lang;

        if (lang == SourceLanguage)
        {
            _overlay = new Dictionary<string, string>(StringComparer.Ordinal);
            Console.WriteLine($"[Loc] language {lang} (source)");
            return true;
        }
        return LoadOverlay(lang);
    }

    private static bool LoadOverlay(string lang)
    {
        string path = Path.Combine(Root, lang + ".csv");
        if (!File.Exists(path))
        {
            _overlay = new Dictionary<string, string>(StringComparer.Ordinal);
            Console.WriteLine($"[Loc] ⚠ no translation file: {path} - displaying the source text");
            return false;
        }
        _overlay = LocCsv.LoadTranslations(path);
        Console.WriteLine($"[Loc] language {lang} - {_overlay.Count} translated rows ({Path.GetFileName(path)})");
        return true;
    }

    public static string T(string key, params (string name, object? value)[] args)
    {
        if (string.IsNullOrEmpty(key)) return "";

        if (!TryRaw(key, out string raw))
        {
            NoteMissing(key);
            return TextFormat.OpenMark + key + TextFormat.CloseMark;
        }
        return TextFormat.Apply(raw, args, p => NoteProblem(key, p));
    }

    public static string Line(string key, string original, params (string name, object? value)[] args)
    {
        string raw = original;
        if (Language != SourceLanguage && !string.IsNullOrEmpty(key)
            && _overlay.TryGetValue(key, out var translated)) raw = translated;

        return TextFormat.Apply(raw, args, p => NoteProblem(key, p));
    }

    public static bool Has(string key) => TryRaw(key, out _);

    public static bool TryRaw(string key, out string raw)
    {
        if (Language != SourceLanguage && _overlay.TryGetValue(key, out raw!)) return true;
        return _table.TryGet(key, out raw);
    }

    private static void NoteMissing(string key)
    {
        if (!_seenMissing.Add(key)) return;
        _missing.Add(key);
        Console.WriteLine($"[Loc] ⚠ missing key: {key}");
    }

    private static void NoteProblem(string key, string problem)
    {
        if (_seenProblem.Add(key + "|" + problem))
            Console.WriteLine($"[Loc] ⚠ '{key}' — {problem}");
    }

    public static List<LocEntry> ExportEntries()
    {
        var list = new List<LocEntry>(_table.Count);
        foreach (var e in _table.Entries)
            list.Add(new LocEntry(e.Namespace, e.Key, e.Value));
        return list;
    }

    public static LocCsvReport ExportCsv(string lang, IReadOnlyList<LocEntry>? extra = null)
    {
        var entries = ExportEntries();
        if (extra != null) entries.AddRange(extra);

        string path = Path.Combine(Root, lang + ".csv");
        var report = LocCsv.Export(path, entries);
        Console.WriteLine($"[Loc] {Path.GetFileName(path)} — {report}");
        foreach (var d in report.Details) Console.WriteLine($"[Loc]   {d}");
        return report;
    }

    internal static void UseTable(StringTable table)
    {
        _table = table;
        _missing.Clear();
        _seenMissing.Clear();
        _seenProblem.Clear();
        Ready = true;
    }
}
