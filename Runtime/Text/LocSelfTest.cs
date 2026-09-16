using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PixelCore.Runtime.Text;

public static class LocSelfTest
{
    private static int _pass, _fail;
    private static string _dir = "";

    public static void Run()
    {
        Console.WriteLine("=== Loc self-test ===");

        _dir = Path.Combine(Path.GetTempPath(), "pixelcore_loc_selftest");
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        Directory.CreateDirectory(_dir);

        TestParser();
        TestLookup();
        TestInterpolation();
        TestCsv();
        TestLanguageSwitch();
        TestRealContent();
        TestInteractionPromptLabel();

        Report();
    }

    private static void TestParser()
    {
        Console.WriteLine("--- parser ---");

        string dir = Path.Combine(_dir, "parse");
        Directory.CreateDirectory(dir);
        Write(Path.Combine(dir, "a.strings"), """
            # comment line, ignored
            verb.examine = Examine
            verb.open   =   Open

            system.saved = Saved.
            item.desc = an = inside the value: only the first one separates
            system.multiline = first line\nsecond line
            broken line
            key with spaces = value
            system.empty =
            """);
        Write(Path.Combine(dir, "b.strings"), """
            verb.examine = Look at
            menu.continue = Continue
            """);

        var t = StringTable.LoadDirectory(dir);

        Check("only valid lines are loaded (a 5 + b 1)", t.Count == 6, $"actual {t.Count}");
        Check("whitespace around a value is trimmed", t.TryGet("verb.open", out var open) && open == "Open", open);
        Check("an '=' inside the value is part of the value", t.TryGet("item.desc", out var d) && d.Contains("= inside"), d);
        Check(@"\n escape", t.TryGet("system.multiline", out var m) && m == "first line\nsecond line");

        Check("a line without '=' is an error", HasError(t, "no '='"));
        Check("whitespace in a key is an error", HasError(t, "whitespace in key"));
        Check("an empty value is an error", HasError(t, "empty value"));
        Check("a duplicate key across files is an error", HasError(t, "duplicate key"));
        Check("on a duplicate the first one wins (a.strings)",
            t.TryGet("verb.examine", out var dup) && dup == "Examine", dup);
        Check("the rest still loads despite errors", t.TryGet("menu.continue", out _));

        var lintDir = Path.Combine(_dir, "lint");
        Directory.CreateDirectory(lintDir);
        Write(Path.Combine(lintDir, "x.strings"), """
            bad.unclosed = Next stop: {station
            good.ok = Next stop: {station}
            """);
        var lint = StringTable.LoadDirectory(lintDir).Lint();
        Check("lint catches an unclosed brace", Any(lint, "unclosed"));
        Check("lint stays quiet on valid lines", lint.Count == 1, $"actual {lint.Count}");
    }

    private static void TestLookup()
    {
        Console.WriteLine("--- lookup ---");

        var t = new StringTable();
        string f = Path.Combine(_dir, "lookup.strings");
        Write(f, "verb.examine = Examine\nname.owner = the owner\n");
        t.LoadFile(f);
        Loc.UseTable(t);

        Check("an existing key", Loc.T("verb.examine") == "Examine");
        Check("a missing key appears on screen in angle brackets", Loc.T("menu.missing") == "⟨menu.missing⟩", Loc.T("menu.missing"));
        Check("a missing key is recorded", Loc.Missing.Count == 1 && Loc.Missing[0] == "menu.missing");
        Check("Has()", Loc.Has("name.owner") && !Loc.Has("name.missing"));

        LocalizedString ls = "name.owner";
        string shown = ls;
        Check("LocalizedString converts implicitly both ways", shown == "the owner", shown);
        Check("LocalizedString.Exists", ls.Exists && !new LocalizedString("name.missing").Exists);
        Check("an empty LocalizedString is an empty string", new LocalizedString("").Value == "");
    }

    private static void TestInterpolation()
    {
        Console.WriteLine("--- interpolation ---");

        Check("substitution by name",
            F("{n} task(s) today", ("n", 3)) == "3 task(s) today");
        Check("several arguments, order independent",
            F("from {a} to {b}", ("b", "Old Town"), ("a", "Harbour")) == "from Harbour to Old Town");
        Check("format specifier {value:N0}: the sentence survives a change of word order",
            F("{amount:N0} coins", ("amount", 1200)) == "1,200 coins", F("{amount:N0} coins", ("amount", 1200)));
        Check("{{ }} is a literal brace", F("{{stop}}", ("stop", "Harbour")) == "{stop}");
        Check("timing tokens pass through untouched (the dialogue box owns them)",
            F("Sorry. {.} the bus... {====}", ("n", 1)) == "Sorry. {.} the bus... {====}");

        var problems = new List<string>();
        string got = TextFormat.Apply("Next stop: {stop}", null, problems.Add);
        Check("a missing argument stays as a visible marker", got == "Next stop: ⟨?stop⟩", got);
        Check("a missing argument is reported as a problem", problems.Count == 1 && problems[0].Contains("stop"));

        problems.Clear();
        got = TextFormat.Apply("value: {n", null, problems.Add);
        Check("an unclosed brace is reported rather than erased",
            got == "value: {n" && problems.Count == 1, got);

        Check("a string with no braces is unchanged", TextFormat.Apply("a plain sentence", null) == "a plain sentence");
    }

    private static void TestCsv()
    {
        Console.WriteLine("--- translation CSV ---");

        string path = Path.Combine(_dir, "en.csv");
        var entries = new List<LocEntry>
        {
            new("verb", "verb.examine", "Examine"),
            new("system", "system.saved", "Saved."),
            new("system", "system.todo", "{n} task(s) today"),
        };

        var r1 = LocCsv.Export(path, entries);
        Check("a new CSV: everything is new", r1.Added == 3 && r1.Kept == 0 && r1.Orphaned == 0, r1.ToString());
        string text = File.ReadAllText(path);
        Check("five header columns", text.StartsWith("Key,Speaker,Original,Translated,Notes", StringComparison.Ordinal));
        Check("section headers are present", text.Contains("## verb ##") && text.Contains("## system ##"));

        Write(path,
            "Key,Speaker,Original,Translated,Notes\n" +
            "## verb ##,,,,\n" +
            "verb.examine,,Examine,Untersuchen,\n" +
            "## system ##,,,,\n" +
            "system.saved,,Saved.,\"Saved, at last\n(really)\",translator note\n" +
            "system.todo,,{n} task(s) today,\"{n} Aufgabe(n) \"\"heute\"\"\",\n");

        var rows = LocCsv.Read(path);
        Check("commas, newlines and quotes inside quotes are read correctly",
            rows.Count == 3 && rows[1].Translated == "Saved, at last\n(really)"
            && rows[2].Translated == "{n} Aufgabe(n) \"heute\"",
            rows.Count == 3 ? rows[1].Translated : $"{rows.Count} rows");

        var r2 = LocCsv.Export(path, entries);
        Check("re-exporting keeps the translations", r2.Kept == 3 && r2.Added == 0, r2.ToString());
        Check("translator notes survive too", File.ReadAllText(path).Contains("translator note"));
        Check("special characters round-trip", LocCsv.Read(path)[1].Translated == "Saved, at last\n(really)");

        entries[1] = new LocEntry("system", "system.saved", "Saved!");
        var r3 = LocCsv.Export(path, entries);
        var afterChange = FindRow(path, "system.saved");
        Check("one source change is reported", r3.Changed == 1 && r3.Stale == 1, r3.ToString());
        Check("the translation is not deleted", afterChange?.Translated.StartsWith("Saved", StringComparison.Ordinal) == true);
        Check("the old source text is recorded in the marker",
            afterChange?.Notes.Contains("!!source-changed(was: Saved.)") == true, afterChange?.Notes);

        var r4 = LocCsv.Export(path, entries);
        var afterAgain = FindRow(path, "system.saved");
        Check("the marker survives re-export, until a person removes it",
            afterAgain?.Notes.Contains("!!source-changed") == true && r4.Stale == 1, r4.ToString());
        Check("markers do not stack up",
            CountOccurrences(afterAgain?.Notes ?? "", "!!source-changed") == 1, afterAgain?.Notes);

        entries.RemoveAt(1);
        var r5 = LocCsv.Export(path, entries);
        var orphan = FindRow(path, "system.saved");
        Check("one missing key is reported", r5.Orphaned == 1, r5.ToString());
        Check("the row survives its key disappearing (no translation is lost)",
            orphan != null && orphan.Translated.StartsWith("Saved", StringComparison.Ordinal));
        Check("it moves into the missing-entries section", File.ReadAllText(path).Contains(LocCsv.OrphanSection));
        Check("the unreferenced marker", orphan?.Notes.Contains("!!unreferenced") == true, orphan?.Notes);

        var r6 = LocCsv.Export(path, entries);
        Check("the unreferenced marker does not stack up either",
            CountOccurrences(FindRow(path, "system.saved")?.Notes ?? "", "!!unreferenced") == 1);
        Check("a missing row survives re-export", r6.Orphaned == 1);

        entries.Insert(1, new LocEntry("system", "system.saved", "Saved!"));
        var r7 = LocCsv.Export(path, entries);
        var revived = FindRow(path, "system.saved");
        Check("a returning key revives its translation",
            r7.Orphaned == 0 && revived?.Translated.StartsWith("Saved", StringComparison.Ordinal) == true,
            r7.ToString());
        Check("the unreferenced marker drops off a revived row",
            revived?.Notes.Contains("!!unreferenced") != true, revived?.Notes);
    }

    private static void TestLanguageSwitch()
    {
        Console.WriteLine("--- language switching ---");

        string dir = Path.Combine(_dir, "lang");
        Directory.CreateDirectory(dir);
        Write(Path.Combine(dir, "labels.strings"), """
            verb.examine = Examine
            menu.continue = Continue
            bus.nextStop = Next stop: {stop}
            """);
        Write(Path.Combine(dir, "en.csv"),
            "Key,Speaker,Original,Translated,Notes\n" +
            "## verb ##,,,,\n" +
            "verb.examine,,Examine,Untersuchen,\n" +
            "## owner ##,,,,\n" +
            "storeNight.owner.01,owner,Late again.,Schon wieder spat.,\n");

        Loc.Load(dir);
        Check("no load errors", Loc.Errors.Count == 0, string.Join(" / ", Loc.Errors));
        Check("the source language uses the source text", Loc.T("verb.examine") == "Examine");

        Check("language switch succeeds", Loc.SetLanguage("en"));
        Check("a translated label", Loc.T("verb.examine") == "Untersuchen", Loc.T("verb.examine"));
        Check("an untranslated label falls back to the source text rather than going blank",
            Loc.T("menu.continue") == "Continue", Loc.T("menu.continue"));

        Check("dialogue finds its translation in the same CSV",
            Loc.Line("storeNight.owner.01", "Late again.") == "Schon wieder spat.");
        Check("untranslated dialogue keeps its inline source text",
            Loc.Line("storeNight.hero.01", "Sorry. {.}") == "Sorry. {.}");

        Check("a missing language file returns false rather than silently staying on the source language", !Loc.SetLanguage("fr"));

        Loc.SetLanguage(Loc.SourceLanguage);
        Check("it returns to the source language", Loc.T("verb.examine") == "Examine");

        var extra = new List<LocEntry> { new("owner", "storeNight.owner.01", "Late again.", "owner") };
        var report = Loc.ExportCsv("en", extra);
        Check("labels and dialogue export to one CSV (a single pipeline)",
            report.Total == 4 && report.Kept == 2, report.ToString());
        Check("the speaker travels to the translator",
            FindRow(Path.Combine(dir, "en.csv"), "storeNight.owner.01")?.Speaker == "owner");
    }

    private static void TestRealContent()
    {
        Console.WriteLine("--- real Content/Strings ---");

        Loc.SetLanguage(Loc.SourceLanguage);
        Loc.Load();

        Check($"{Loc.DefaultRoot} exists (the premise of the real table lint)", Directory.Exists(Loc.DefaultRoot));
        if (!Directory.Exists(Loc.DefaultRoot)) return;

        Check($"no errors ({Loc.Count} labels)", Loc.Errors.Count == 0,
            string.Join(" / ", Loc.Errors));

        var table = StringTable.LoadDirectory(Loc.DefaultRoot);
        var lint = table.Lint();
        Check("no syntax lint problems", lint.Count == 0, string.Join(" / ", lint));

        var known = new HashSet<string> { "verb", "menu", "system", "item", "name", "place" };
        var strays = new List<string>();
        foreach (var e in table.Entries)
            if (!known.Contains(e.Namespace)) strays.Add($"{e.Key}({e.File}:{e.Line})");
        Check("namespaces stay within the convention", strays.Count == 0, string.Join(" ", strays));
    }

    private static void TestInteractionPromptLabel()
    {
        Console.WriteLine("--- interaction prompt ---");

        Loc.SetLanguage(Loc.SourceLanguage);
        Loc.Load();

        var scene = new Core.Scene("PromptTest");
        var e = scene.CreateEntity("Desk");
        var it = e.AddComponent<Runtime.Components.Interactable>();
        scene.FlushPendingAdds();

        Check("★ the default is a key, so a newly placed object bakes no display text into the scene",
            it.Prompt == "verb.examine", it.Prompt);

        var label = Runtime.Systems.InteractionPrompt.LabelFor(it);
        Check("★ the on-screen wording comes from the label table (the key is not shown raw)",
            label.Contains("Examine") && !label.Contains("verb.examine"), label);
        Check("the key glyph comes from the binding table",
            label.StartsWith($"[{Core.InputMap.KeyLabel(Core.GameAction.Interact)}]"), label);

        it.Prompt = "verb.open";
        label = Runtime.Systems.InteractionPrompt.LabelFor(it);
        Check("★ control: a different key gives different wording, proving it resolves",
            label.Contains("Open"), label);

        it.Prompt = "verb.nosuch";
        label = Runtime.Systems.InteractionPrompt.LabelFor(it);
        Check("★ a missing key appears on screen in angle brackets rather than going blank",
            label.Contains("⟨verb.nosuch⟩"), label);

        Loc.Load();
    }

    private static string F(string template, params (string name, object? value)[] args)
        => TextFormat.Apply(template, args);

    private static void Write(string path, string content) =>
        File.WriteAllText(path, content, new UTF8Encoding(false));

    private static LocCsvRow? FindRow(string path, string key)
    {
        foreach (var r in LocCsv.Read(path)) if (r.Key == key) return r;
        return null;
    }

    private static bool HasError(StringTable t, string fragment)
    {
        foreach (var e in t.Errors) if (e.Contains(fragment, StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool Any(List<string> list, string fragment)
    {
        foreach (var s in list) if (s.Contains(fragment, StringComparison.Ordinal)) return true;
        return false;
    }

    private static int CountOccurrences(string s, string needle)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    private static void Check(string label, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  ✔ {label}"); }
        else { _fail++; Console.WriteLine($"  ✘ {label}" + (detail != null ? $"  ← {detail}" : "")); }
    }

    private static void Report() => Console.WriteLine($"=== {_pass} passed, {_fail} failed ===");
}
