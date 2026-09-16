#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PixelCore.Runtime.Cutscenes;
using PixelCore.Runtime.Story;
using PixelCore.Runtime.Text;
using PixelCore.Runtime.Core;

namespace PixelCore.Gameplay.Systems;

public static class LocStamp
{
    public const string CutsceneDir = "Gameplay/Cutscenes";

    public const string LedgerFile = "Content/Strings/_stamp.json";

    private static readonly Regex SayCall = new(
        @"(?<recv>[A-Za-z_]\w*)\.Say\(\s*""(?<text>(?:[^""\\]|\\.)*)""\s*(?:,\s*(?<num>\d+)\s*)?\)",
        RegexOptions.Compiled);

    private static readonly Regex SayCallLoose = new(
        @"(?<recv>[A-Za-z_]\w*)\.Say\(", RegexOptions.Compiled);

    private static readonly Regex BodyStart = new(
        @"IEnumerator<Wait>\s+(?<name>\w+)\s*\(", RegexOptions.Compiled);

    public readonly struct InlineLine
    {
        public readonly string File, CutsceneId, Actor, Text;
        public readonly int Line, Number;
        public InlineLine(string file, int line, string id, string actor, string text, int number)
        { File = file; Line = line; CutsceneId = id; Actor = actor; Text = text; Number = number; }

        public string Key => Number > 0 ? CutsceneId + "." + Number : "";
    }

    public static List<InlineLine> Scan(string? dir = null)
    {
        var found = new List<InlineLine>();
        dir ??= CutsceneDir;
        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine($"[Stamp] ✘ no cutscene folder: {dir}");
            return found;
        }

        var byMethod = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in CutsceneDirector.Ids)
        {
            var m = CutsceneDirector.BodyMethodName(id);
            if (string.IsNullOrEmpty(m)) continue;
            if (byMethod.TryGetValue(m!, out var prev))
            {
                Console.Error.WriteLine(
                    $"[Stamp] ✘ method '{m}' is registered to two cutscenes: '{prev}' and '{id}' - the key cannot be resolved");
                continue;
            }
            byMethod[m!] = id;
        }

        foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            string src = StripComments(File.ReadAllText(file));
            foreach (var (name, start, end) in BodySpans(src))
            {
                if (!byMethod.TryGetValue(name, out var cutsceneId)) continue;
                foreach (Match m in SayCall.Matches(src, start))
                {
                    if (m.Index >= end) break;
                    if (!IsHandle(m.Groups["recv"].Value)) continue;
                    int number = m.Groups["num"].Success
                        ? int.Parse(m.Groups["num"].Value, CultureInfo.InvariantCulture) : 0;
                    found.Add(new InlineLine(file, LineOf(src, m.Index), cutsceneId,
                                             m.Groups["recv"].Value, Unescape(m.Groups["text"].Value), number));
                }
            }
        }
        return found;
    }

    private static bool IsHandle(string receiver) => receiver != "c";

    public static List<LocEntry> CollectStory(StoryLibrary lib)
    {
        var list = new List<LocEntry>();
        foreach (var script in lib.Scripts)
            foreach (var block in script.Blocks)
                foreach (var variant in block.Variants)
                    CollectNodes(variant.Nodes, block, list);
        return list;
    }

    private static void CollectNodes(List<DialogueNode> nodes, DialogueBlock block, List<LocEntry> into)
    {
        foreach (var n in nodes)
        {
            if (n is DialogueLineNode line && line.Number > 0)
                into.Add(new LocEntry(line.Speaker, block.Id + "." + line.Number, line.Text, line.Speaker));
            else if (n is DialogueChoiceNode choice)
                foreach (var opt in choice.Options)
                {
                    if (opt.Number > 0)
                        into.Add(new LocEntry("choice", block.Id + "." + opt.Number, opt.Text));
                    CollectNodes(opt.Nodes, block, into);
                }
        }
    }

    public static List<LocEntry> ToEntries(IReadOnlyList<InlineLine> lines)
    {
        var list = new List<LocEntry>();
        foreach (var l in lines)
            if (l.Number > 0)
                list.Add(new LocEntry(l.Actor, l.Key, l.Text, l.Actor,
                                      $"{Path.GetFileName(l.File)}:{l.Line}"));
        return list;
    }

    public static Dictionary<string, int> LoadLedger(string? path = null)
    {
        path ??= LedgerFile;
        if (!File.Exists(path)) return new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path));
            return raw != null
                ? new Dictionary<string, int>(raw, StringComparer.Ordinal)
                : new Dictionary<string, int>(StringComparer.Ordinal);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Stamp] ✘ could not read the ledger: {path} - {e.Message}");
            throw;
        }
    }

    public static void SaveLedger(Dictionary<string, int> ledger, string? path = null)
    {
        path ??= LedgerFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sorted = new SortedDictionary<string, int>(ledger, StringComparer.Ordinal);
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(sorted,
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    public sealed class StampReport
    {
        public int Stamped, Kept, Files;
        public readonly List<string> Details = new();
        public override string ToString() => $"stamped {Stamped}, kept {Kept}, files {Files}";
    }

    public static StampReport Stamp(string? dir = null, string? ledgerPath = null, bool dryRun = false)
    {
        var report = new StampReport();
        var lines = Scan(dir);
        var ledger = LoadLedger(ledgerPath);

        foreach (var l in lines)
            if (l.Number > 0)
            {
                ledger.TryGetValue(l.CutsceneId, out int hi);
                if (l.Number > hi) ledger[l.CutsceneId] = l.Number;
            }

        foreach (var group in lines.GroupBy(l => l.File).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            string src = File.ReadAllText(group.Key);
            var edits = new List<(int Index, int Length, string Replacement)>();

            foreach (Match m in SayCall.Matches(src))
            {
                if (!IsHandle(m.Groups["recv"].Value)) continue;
                if (m.Groups["num"].Success) { report.Kept++; continue; }

                var owner = lines.FirstOrDefault(l => l.File == group.Key && l.Line == LineOf(src, m.Index));
                if (string.IsNullOrEmpty(owner.CutsceneId)) continue;

                ledger.TryGetValue(owner.CutsceneId, out int hi);
                int next = hi + 1;
                ledger[owner.CutsceneId] = next;

                string call = m.Value;
                string replaced = call[..^1] + ", " + next.ToString(CultureInfo.InvariantCulture) + ")";
                edits.Add((m.Index, m.Length, replaced));
                report.Stamped++;
                report.Details.Add($"{Path.GetFileName(group.Key)}:{owner.Line}  {owner.CutsceneId}.{next}  \"{Snip(owner.Text)}\"");
            }

            if (edits.Count == 0) continue;
            report.Files++;
            if (dryRun) continue;

            var sb = new StringBuilder(src);
            edits.Sort((a, b) => b.Index.CompareTo(a.Index));
            foreach (var (idx, len, rep) in edits) { sb.Remove(idx, len); sb.Insert(idx, rep); }
            AtomicFile.WriteAllText(group.Key, sb.ToString());
        }

        if (!dryRun && report.Stamped > 0) SaveLedger(ledger, ledgerPath);
        return report;
    }

    public static int Verify(StoryLibrary lib, string? dir = null, string? ledgerPath = null, bool requireStamped = false)
    {
        int problems = 0;
        var lines = Scan(dir);
        var story = CollectStory(lib);
        var ledger = LoadLedger(ledgerPath);

        var storyKeys = new HashSet<string>(story.Select(e => e.Key), StringComparer.Ordinal);
        foreach (var l in lines)
        {
            if (l.Number <= 0) continue;
            if (storyKeys.Contains(l.Key))
            {
                Console.Error.WriteLine(
                    $"[Stamp] ✘ key collision '{l.Key}' - an inline line ({Path.GetFileName(l.File)}:{l.Line}) and .story share one key. " +
                    "One of the two has to change namespace");
                problems++;
            }
        }

        foreach (var g in lines.Where(l => l.Number > 0).GroupBy(l => l.Key, StringComparer.Ordinal))
            if (g.Count() > 1)
            {
                Console.Error.WriteLine($"[Stamp] ✘ duplicate inline key '{g.Key}' ({g.Count()} places): " +
                    string.Join(" · ", g.Select(l => $"{Path.GetFileName(l.File)}:{l.Line}")));
                problems++;
            }

        foreach (var (f, ln, snip) in FindNonLiteral(dir))
        {
            Console.Error.WriteLine(
                $"[Stamp] ✘ non-literal line {Path.GetFileName(f)}:{ln} - {Snip(snip)}  " +
                "A translation key only works if the source text is constant. Use Loc's {name} interpolation where a value goes " +
                "(the engine even picks the particle - string concatenation cannot do that)");
            problems++;
        }

        foreach (var g in lines.Where(l => l.Number > 0).GroupBy(l => l.CutsceneId, StringComparer.Ordinal))
        {
            int maxUsed = g.Max(l => l.Number);
            ledger.TryGetValue(g.Key, out int hi);
            if (maxUsed > hi)
            {
                Console.Error.WriteLine(
                    $"[Stamp] ✘ the high-water ledger lags for '{g.Key}' - source max {maxUsed}, ledger {hi}. " +
                    "A low ledger revives a deleted number and it wears someone else's translation. Re-run the stamp to sync it");
                problems++;
            }
        }

        foreach (var id in ledger.Keys)
            if (!CutsceneDirector.IsRegistered(id))
            {
                Console.Error.WriteLine(
                    $"[Stamp] ✘ cutscene '{id}' from the ledger is not in the registry - it was renamed or deleted. " +
                    "Left as is, those translated lines are orphaned forever");
                problems++;
            }

        if (requireStamped)
            foreach (var l in lines.Where(l => l.Number <= 0))
            {
                Console.Error.WriteLine(
                    $"[Stamp] ✘ unnumbered line {Path.GetFileName(l.File)}:{l.Line} \"{Snip(l.Text)}\" - no translation can attach");
                problems++;
            }

        return problems;
    }

    private static string StripComments(string src)
    {
        var sb = new StringBuilder(src);
        for (int i = 0; i < sb.Length; i++)
        {
            char ch = sb[i];
            if (ch == '"')
            {
                bool verbatim = i > 0 && sb[i - 1] == '@';
                i++;
                while (i < sb.Length)
                {
                    if (!verbatim && sb[i] == '\\') { i += 2; continue; }
                    if (sb[i] == '"') break;
                    i++;
                }
                continue;
            }
            if (ch != '/' || i + 1 >= sb.Length) continue;

            if (sb[i + 1] == '/')
            {
                while (i < sb.Length && sb[i] != '\n') { sb[i] = ' '; i++; }
                i--;
            }
            else if (sb[i + 1] == '*')
            {
                int end = src.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? sb.Length : end + 2;
                for (int k = i; k < end; k++) if (sb[k] != '\n') sb[k] = ' ';
                i = end - 1;
            }
        }
        return sb.ToString();
    }

    private static int MatchParen(string src, int open)
    {
        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            char ch = src[i];
            if (ch == '"')
            {
                bool verbatim = i > 0 && src[i - 1] == '@';
                i++;
                while (i < src.Length)
                {
                    if (!verbatim && src[i] == '\\') { i += 2; continue; }
                    if (src[i] == '"') break;
                    i++;
                }
                continue;
            }
            if (ch == '(') depth++;
            else if (ch == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    public static List<(string File, int Line, string Snippet)> FindNonLiteral(string? dir = null)
    {
        var bad = new List<(string, int, string)>();
        dir ??= CutsceneDir;
        if (!Directory.Exists(dir)) return bad;

        foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            string src = StripComments(File.ReadAllText(file));
            foreach (Match loose in SayCallLoose.Matches(src))
            {
                if (!IsHandle(loose.Groups["recv"].Value)) continue;

                int open = src.IndexOf('(', loose.Index);
                int close = MatchParen(src, open);
                if (close < 0) continue;

                var strict = SayCall.Match(src, loose.Index);
                bool clean = strict.Success && strict.Index == loose.Index
                             && strict.Index + strict.Length == close + 1;
                if (!clean)
                    bad.Add((file, LineOf(src, loose.Index), src[loose.Index..(close + 1)].Trim()));
            }
        }
        return bad;
    }

    private static IEnumerable<(string Name, int Start, int End)> BodySpans(string src)
    {
        var starts = BodyStart.Matches(src).Cast<Match>().ToList();
        for (int i = 0; i < starts.Count; i++)
        {
            int end = i + 1 < starts.Count ? starts[i + 1].Index : src.Length;
            yield return (starts[i].Groups["name"].Value, starts[i].Index, end);
        }
    }

    private static int LineOf(string src, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < src.Length; i++) if (src[i] == '\n') line++;
        return line;
    }

    private static string Unescape(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n");
    private static string Snip(string s) => s.Length <= 30 ? s : s[..30] + "…";
}
#endif
