#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Cutscenes;
using PixelCore.Runtime.Story;

namespace PixelCore.Gameplay.Systems;

public static class LocStampSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== stamp tool self-test ===");
        _pass = _fail = 0;

        string root = Path.Combine(Path.GetTempPath(), "pixelcore_stamp_selftest");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        string src = Path.Combine(root, "Cutscenes");
        Directory.CreateDirectory(src);
        string ledger = Path.Combine(root, "_stamp.json");

        var saveIds = new List<string>(CutsceneDirector.Ids);
        CutsceneDirector.ClearRegistry();
        CutsceneDirector.Register("test.stamp", TestScene);

        File.WriteAllText(Path.Combine(src, "TestCutscenes.cs"), """
            using System.Collections.Generic;
            namespace X;
            public static class T
            {
                private static IEnumerator<Wait> TestScene(Cutscene c)
                {
                    var hero = c.Actor("hero");
                    yield return hero.Say("First line");
                    yield return c.Say("examine.somewhere");
                    yield return hero.Say("Second line");
                }
            }
            """);

        var lib = StoryLibrary.Load(Path.Combine(root, "nope"));

        var found = LocStamp.Scan(src);
        Check("only inline Say is matched (c.Say is a .story reference and excluded)", found.Count == 2, $"{found.Count} lines");
        Check("the cutscene id comes from the registration table", found.Count == 2 && found[0].CutsceneId == "test.stamp");
        Check("the text is read verbatim", found.Count == 2 && found[0].Text == "First line", found.Count > 0 ? found[0].Text : "");

        var r1 = LocStamp.Stamp(src, ledger);
        Check("both lines get a number", r1.Stamped == 2, r1.ToString());
        var after = LocStamp.Scan(src);
        Check("* 1 and 2 are assigned in source order (so the CSV does not read backwards)",
            after.Count == 2 && after[0].Number == 1 && after[1].Number == 2,
            after.Count == 2 ? $"{after[0].Number},{after[1].Number}" : "?");
        Check("* the high-water mark is stored in the ledger (no derivation)",
            LocStamp.LoadLedger(ledger).TryGetValue("test.stamp", out int hi) && hi == 2, "");

        string before = File.ReadAllText(Path.Combine(src, "TestCutscenes.cs"));
        var r2 = LocStamp.Stamp(src, ledger);
        Check("* running twice stamps nothing (idempotent)", r2.Stamped == 0 && r2.Kept == 2, r2.ToString());
        Check("* the file bytes are unchanged",
            File.ReadAllText(Path.Combine(src, "TestCutscenes.cs")) == before);

        const string anchor = "yield return c.Say(\"examine.somewhere\");";
        File.WriteAllText(Path.Combine(src, "TestCutscenes.cs"),
            before.Replace(anchor, anchor + "\n        yield return hero.Say(\"Inserted line\");"));
        LocStamp.Stamp(src, ledger);
        var mid = LocStamp.Scan(src);
        Check("* the inserted line gets 3 (a number is an identity, not an ordinal)",
            mid.Count == 3 && mid[0].Number == 1 && mid[1].Number == 3 && mid[2].Number == 2,
            string.Join(",", mid.ConvertAll(l => l.Number.ToString())));

        LocStamp.SaveLedger(new Dictionary<string, int>(StringComparer.Ordinal) { ["test.stamp"] = 1 }, ledger);
        Check("* the gate catches a ledger lower than the source (preventing revived dead numbers)",
            LocStamp.Verify(lib, src, ledger) > 0);

        LocStamp.SaveLedger(new Dictionary<string, int>(StringComparer.Ordinal)
            { ["test.stamp"] = 3, ["test.deleted"] = 9 }, ledger);
        Check("* a ledger cutscene missing from the registration table is caught (rename or delete)",
            LocStamp.Verify(lib, src, ledger) > 0);

        LocStamp.SaveLedger(new Dictionary<string, int>(StringComparer.Ordinal) { ["test.stamp"] = 3 }, ledger);
        Check("a healthy state passes", LocStamp.Verify(lib, src, ledger) == 0);
        File.AppendAllText(Path.Combine(src, "Extra.cs"), "");
        Check("* the unstamped gate is opt-in (unstamped is the normal state while authoring)",
            LocStamp.Verify(lib, src, ledger, requireStamped: true) == 0);

        string bad = Path.Combine(src, "Bad.cs");
        foreach (var (label, expr) in new[]
        {
            ("interpolation", "$\"interp {1}\""),
            ("concatenation", "\"con\" + \"cat\""),
            ("verbatim", "@\"verbatim\""),
            ("variable", "someText"),
        })
        {
            File.WriteAllText(bad,
                "class B { static IEnumerator<Wait> TestScene(Cutscene c) { var hero = c.Actor(\"hero\");\n" +
                "  yield return hero.Say(" + expr + ");\n} }");
            Check($"* non-literal dialogue is caught ({label})", LocStamp.FindNonLiteral(src).Count == 1,
                $"{LocStamp.FindNonLiteral(src).Count}");
            Check($"  the gate counts it as a problem ({label})", LocStamp.Verify(lib, src, ledger) > 0);
        }

        File.WriteAllText(bad,
            "class B {\n  // example: hero.Say($\"this is a comment {x}\");\n" +
            "  /// <c>hero.Say(variable)</c>\n}");
        Check("* a Say inside a comment is not a false positive", LocStamp.FindNonLiteral(src).Count == 0,
            $"{LocStamp.FindNonLiteral(src).Count}");
        File.Delete(bad);

        CutsceneDirector.ClearRegistry();
        try { Directory.Delete(root, true); } catch { }
        Console.WriteLine($"=== Stamp: {_pass} passed, {_fail} failed ===");
    }

    private static IEnumerator<Wait> TestScene(Cutscene c) { yield break; }

    private static void Check(string label, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  ✓ {label}"); }
        else { _fail++; Console.WriteLine($"  ✗ {label}{(detail != null ? $"  ({detail})" : "")}"); }
    }
}
#endif
