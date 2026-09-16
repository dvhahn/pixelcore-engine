using System;
using System.Collections.Generic;
using System.IO;

namespace PixelCore.Runtime.Text;

public readonly struct StringEntry
{
    public readonly string Key;
    public readonly string Value;
    public readonly string File;
    public readonly int Line;

    public StringEntry(string key, string value, string file, int line)
    { Key = key; Value = value; File = file; Line = line; }

    public string Namespace
    {
        get
        {
            int dot = Key.IndexOf('.');
            return dot > 0 ? Key[..dot] : Key;
        }
    }
}

public sealed class StringTable
{
    private readonly List<StringEntry> _entries = new();
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
    private readonly List<string> _errors = new();

    public IReadOnlyList<StringEntry> Entries => _entries;
    public IReadOnlyList<string> Errors => _errors;
    public int Count => _entries.Count;

    public bool TryGet(string key, out string value)
    {
        if (_index.TryGetValue(key, out int i)) { value = _entries[i].Value; return true; }
        value = "";
        return false;
    }

    public static StringTable LoadDirectory(string dir)
    {
        var table = new StringTable();
        if (!Directory.Exists(dir))
        {
            table._errors.Add($"folder not found: {dir}");
            return table;
        }

        var files = Directory.GetFiles(dir, "*.strings", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var f in files) table.LoadFile(f);
        return table;
    }

    public void LoadFile(string path)
    {
        string name = Path.GetFileName(path);
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch (Exception e) { _errors.Add($"{name}: read failed - {e.Message}"); return; }

        for (int n = 0; n < lines.Length; n++)
        {
            string raw = lines[n].Trim();
            if (raw.Length == 0 || raw[0] == '#') continue;

            int eq = raw.IndexOf('=');
            if (eq < 0)
            {
                _errors.Add($"{name}:{n + 1} no '=' - {Clip(raw)}");
                continue;
            }

            string key = raw[..eq].Trim();
            string value = Unescape(raw[(eq + 1)..].Trim());

            if (key.Length == 0) { _errors.Add($"{name}:{n + 1} empty key"); continue; }
            if (HasWhitespace(key)) { _errors.Add($"{name}:{n + 1} whitespace in key - '{key}'"); continue; }
            if (value.Length == 0) { _errors.Add($"{name}:{n + 1} empty value - '{key}'"); continue; }

            if (_index.TryGetValue(key, out int prev))
            {
                var p = _entries[prev];
                _errors.Add($"{name}:{n + 1} duplicate key '{key}' - already at {p.File}:{p.Line} (the first one wins)");
                continue;
            }

            _index[key] = _entries.Count;
            _entries.Add(new StringEntry(key, value, name, n + 1));
        }
    }

    public List<string> Lint()
    {
        var problems = new List<string>();
        var buf = new List<string>();
        foreach (var e in _entries)
        {
            buf.Clear();
            TextFormat.Lint(e.Value, buf);
            foreach (var p in buf) problems.Add($"{e.File}:{e.Line} '{e.Key}' — {p}");
        }
        return problems;
    }

    private static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
            char next = s[++i];
            sb.Append(next switch { 'n' => '\n', 't' => '\t', '\\' => '\\', _ => next });
        }
        return sb.ToString();
    }

    private static bool HasWhitespace(string s)
    {
        foreach (char c in s) if (char.IsWhiteSpace(c)) return true;
        return false;
    }

    private static string Clip(string s) => s.Length <= 40 ? s : s[..40] + "…";
}
