using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Text;

public sealed class LocCsvRow
{
    public string Key = "";
    public string Speaker = "";
    public string Original = "";
    public string Translated = "";
    public string Notes = "";
}

public readonly struct LocEntry
{
    public readonly string Section;
    public readonly string Key;
    public readonly string Speaker;
    public readonly string Original;
    public readonly string Notes;

    public LocEntry(string section, string key, string original, string speaker = "", string notes = "")
    { Section = section; Key = key; Original = original; Speaker = speaker; Notes = notes; }
}

public sealed class LocCsvReport
{
    public int Added, Kept, Changed, Stale, Orphaned, Total;
    public readonly List<string> Details = new();

    public override string ToString() =>
        $"{Total} rows, {Added} new, {Kept} translations kept, {Stale} source-changed ({Changed} newly), {Orphaned} missing";
}

public static class LocCsv
{
    public const string OrphanSection = "!! missing - not present in the source";
    private const string StaleMark = "!!source-changed";
    private const string OrphanMark = "!!unreferenced";
    private const string NoteSep = " | ";

    private static readonly string[] Header = { "Key", "Speaker", "Original", "Translated", "Notes" };

    public static List<LocCsvRow> Read(string path)
    {
        var rows = new List<LocCsvRow>();
        if (!File.Exists(path)) return rows;

        var table = ParseCsv(File.ReadAllText(path));
        for (int r = 0; r < table.Count; r++)
        {
            var cells = table[r];
            if (cells.Count == 0) continue;
            string first = cells[0].Trim();
            if (first.Length == 0) continue;
            if (first.StartsWith("##", StringComparison.Ordinal)) continue;
            if (r == 0 && first == Header[0]) continue;

            rows.Add(new LocCsvRow
            {
                Key = first,
                Speaker = Cell(cells, 1),
                Original = Cell(cells, 2),
                Translated = Cell(cells, 3),
                Notes = Cell(cells, 4),
            });
        }
        return rows;
    }

    public static Dictionary<string, string> LoadTranslations(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in Read(path))
            if (row.Translated.Length > 0) map[row.Key] = row.Translated;
        return map;
    }

    public static LocCsvReport Export(string path, IReadOnlyList<LocEntry> entries)
    {
        var report = new LocCsvReport();

        var existing = new Dictionary<string, LocCsvRow>(StringComparer.Ordinal);
        var existingOrder = new List<LocCsvRow>();
        foreach (var row in Read(path))
        {
            if (existing.ContainsKey(row.Key)) continue;
            existing[row.Key] = row;
            existingOrder.Add(row);
        }

        var sections = new List<string>();
        var bySection = new Dictionary<string, List<LocCsvRow>>(StringComparer.Ordinal);
        var live = new HashSet<string>(StringComparer.Ordinal);

        foreach (var e in entries)
        {
            live.Add(e.Key);
            if (!bySection.TryGetValue(e.Section, out var list))
            {
                list = new List<LocCsvRow>();
                bySection[e.Section] = list;
                sections.Add(e.Section);
            }

            var row = new LocCsvRow
            {
                Key = e.Key,
                Speaker = e.Speaker,
                Original = e.Original,
                Notes = e.Notes,
            };

            if (existing.TryGetValue(e.Key, out var old))
            {
                row.Translated = old.Translated;

                string carried = StripOrphanMarks(old.Notes);
                if (carried.Length > 0) row.Notes = Join(row.Notes, carried);

                bool alreadyStale = carried.Contains(StaleMark, StringComparison.Ordinal);
                if (old.Original != e.Original && old.Translated.Length > 0 && !alreadyStale)
                {
                    row.Notes = Join(row.Notes, $"{StaleMark}(was: {old.Original})");
                    report.Changed++;
                    report.Details.Add($"source changed {e.Key}: \"{old.Original}\" -> \"{e.Original}\"");
                }
                else if (old.Translated.Length > 0) report.Kept++;

                if (row.Notes.Contains(StaleMark, StringComparison.Ordinal)) report.Stale++;
            }
            else report.Added++;

            list.Add(row);
        }

        var orphans = new List<LocCsvRow>();
        foreach (var old in existingOrder)
        {
            if (live.Contains(old.Key)) continue;
            orphans.Add(new LocCsvRow
            {
                Key = old.Key,
                Speaker = old.Speaker,
                Original = old.Original,
                Translated = old.Translated,
                Notes = Join(OrphanMark, StripOrphanMarks(old.Notes)),
            });
            report.Orphaned++;
            report.Details.Add($"missing {old.Key}: \"{old.Original}\"");
        }

        var sb = new StringBuilder();
        WriteRow(sb, Header);
        foreach (var section in sections)
        {
            WriteSection(sb, section);
            foreach (var row in bySection[section]) WriteRow(sb, row);
        }
        if (orphans.Count > 0)
        {
            WriteSection(sb, OrphanSection);
            foreach (var row in orphans) WriteRow(sb, row);
        }

        report.Total = entries.Count + orphans.Count;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        AtomicFile.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        return report;
    }

    private static void WriteSection(StringBuilder sb, string name) =>
        WriteRow(sb, new[] { $"## {name} ##", "", "", "", "" });

    private static void WriteRow(StringBuilder sb, LocCsvRow r) =>
        WriteRow(sb, new[] { r.Key, r.Speaker, r.Original, r.Translated, r.Notes });

    private static void WriteRow(StringBuilder sb, string[] cells)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Quote(cells[i]));
        }
        sb.Append('\n');
    }

    private static string Quote(string? s)
    {
        s ??= "";
        bool needs = s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0
            || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0
            || s.StartsWith(" ", StringComparison.Ordinal) || s.EndsWith(" ", StringComparison.Ordinal);
        if (!needs) return s;
        return '"' + s.Replace("\"", "\"\"") + '"';
    }

    public static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else cell.Append(c);
                continue;
            }

            switch (c)
            {
                case '"' when cell.Length == 0:
                    quoted = true;
                    break;
                case ',':
                    row.Add(cell.ToString()); cell.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(cell.ToString()); cell.Clear();
                    rows.Add(row); row = new List<string>();
                    break;
                default:
                    cell.Append(c);
                    break;
            }
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }

    private static string Cell(List<string> cells, int i) => i < cells.Count ? cells[i] : "";

    private static string Join(string a, string b) =>
        a.Length == 0 ? b : b.Length == 0 ? a : a + NoteSep + b;

    private static string StripOrphanMarks(string notes)
    {
        if (notes.Length == 0) return "";
        var kept = new List<string>();
        foreach (var part in notes.Split(NoteSep, StringSplitOptions.None))
        {
            string t = part.Trim();
            if (t.Length == 0) continue;
            if (t.StartsWith(OrphanMark, StringComparison.Ordinal)) continue;
            kept.Add(t);
        }
        return string.Join(NoteSep, kept);
    }
}
