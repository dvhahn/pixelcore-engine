#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PixelCore.Runtime.Core;

public static class AtomicFileSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== AtomicFile / ledger save failure self-test ===");

        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_atomic_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            TestHappyPath(dir);
            TestTempStageFailureKeepsOriginal(dir);
            TestMoveStageFailureLeavesNoTemp(dir);
            TestEncodingOverload(dir);
            TestRegistrySaveReturnsFalse(dir);
            TestNoDirectWriteInAuthoredFormats();
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        Console.WriteLine($"=== AtomicFile: {_pass} passed, {_fail} failed ===");
    }

    private static void TestHappyPath(string dir)
    {
        var path = Path.Combine(dir, "happy.json");
        AtomicFile.WriteAllText(path, "{\"a\":1}");
        Check("the contents are written unchanged", File.ReadAllText(path) == "{\"a\":1}");
        Check("★ zero .tmp leftovers (no litter left in authoring folders)", !File.Exists(path + ".tmp"));

        AtomicFile.WriteAllText(path, "{\"a\":2}");
        Check("overwriting works too (File.Move overwrite)", File.ReadAllText(path) == "{\"a\":2}");
        Check("zero .tmp leftovers after overwriting too", !File.Exists(path + ".tmp"));
    }

    private static void TestTempStageFailureKeepsOriginal(string dir)
    {
        var path = Path.Combine(dir, "keep.json");
        File.WriteAllText(path, "OLD");

        Directory.CreateDirectory(path + ".tmp");
        bool threw = false;
        try { AtomicFile.WriteAllText(path, "NEW"); }
        catch { threw = true; }

        Check("★ the failure is thrown to the caller - and that is the evidence the temporary file is path+\".tmp\" in the same folder",
              threw);
        Check($"★ the original is neither truncated nor changed (measured '{File.ReadAllText(path)}')",
              File.ReadAllText(path) == "OLD");

        Directory.Delete(path + ".tmp");
        AtomicFile.WriteAllText(path, "NEW");
        Check("★ control: with the injection removed the same call succeeds", File.ReadAllText(path) == "NEW");
    }

    private static void TestMoveStageFailureLeavesNoTemp(string dir)
    {
        var path = Path.Combine(dir, "movefail.json");
        Directory.CreateDirectory(path);

        bool threw = false;
        try { AtomicFile.WriteAllText(path, "X"); }
        catch { threw = true; }

        Check("★ a move failure throws too", threw);
        Check("★ and the temporary file is cleaned up - otherwise .tmp piles up in authoring folders",
              !File.Exists(path + ".tmp"));
        Check("premise: the injection really was at the move stage (where the temporary write can succeed)",
              Directory.Exists(path));
        Directory.Delete(path);
    }

    private static void TestEncodingOverload(string dir)
    {
        var path = Path.Combine(dir, "bom.csv");
        AtomicFile.WriteAllText(path, "key,source\n", new UTF8Encoding(true));
        var bytes = File.ReadAllBytes(path);
        Check("★ the BOM encoding is preserved (leaking through the default overload makes Excel garble non-ASCII text)",
              bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Check("the BOM path also leaves zero .tmp", !File.Exists(path + ".tmp"));
    }

    private static void TestRegistrySaveReturnsFalse(string dir)
    {
        var regPath = Path.Combine(dir, "reg", "assets.json");
        using var scope = Assets.AssetRegistry.UseTemporary(regPath);
        var reg = Assets.AssetRegistry.Instance;

        reg.Register("aaaa1111", "Sprites/x.png");
        Check("premise: there is something to write (dirty)", reg.IsDirty);

        var errOk = CaptureStderr(() => Check("★ true on success", reg.Save()));
        Check("★ success lowers dirty", !reg.IsDirty);
        Check($"★ success is quiet (complaint '{errOk.Trim()}')", errOk.Length == 0);
        Check("★ true when there is nothing to do (\"did not write\" is not a failure)", reg.Save());

        reg.Register("bbbb2222", "Sprites/y.png");
        Directory.CreateDirectory(regPath + ".tmp");
        bool ok = true;
        var err = CaptureStderr(() => ok = reg.Save());
        Check("★ a write failure is false (it used to be void, so 10 callers assumed it had succeeded)", !ok);
        Check($"★ and the message goes to stderr (measured '{err.Trim()}')",
              err.Contains("[AssetRegistry] registry save failed"));
        Check("★ a failure leaves dirty set - the next save tries again", reg.IsDirty);

        Directory.Delete(regPath + ".tmp");
        Check("★ control: with the injection removed the same call returns true", reg.Save());
    }

    private static readonly string[] AuthoredWriters =
    {
        "Runtime/Serialization/SceneSerializer.cs",
        "Runtime/Assets/AssetRegistry.cs",
        "Runtime/Assets/SpriteAtlas.cs",
        "Runtime/Assets/PostAsset.cs",
        "Runtime/Assets/AssetScanner.cs",
        "Runtime/Animation/AnimationData.cs",
        "Runtime/Text/LocCsv.cs",
        "Editor/EditorPrefs.cs",
        "Runtime/Particles/ParticlePreset.cs",
        "Runtime/Audio/SurfaceLibrary.cs",
        "Runtime/Core/GameSettings.cs",
        "Gameplay/Systems/LocStamp.cs",
        "Editor/Panels/ProjectPanel.cs",
    };

    private static readonly Dictionary<string, string> Exempt = new()
    {
        ["Runtime/Save/SaveFile.cs"] = "its own three-step fallback (primary, _old, _tmp) is thicker than this - moving to the thinner one would lose backup recovery",
        ["Runtime/Core/CrashHandler.cs"] = "it only records on the way down - adding a Move here adds one more failure point (deliberately left out of the follow-up too)",
        ["Runtime/Core/AtomicFile.cs"] = "the implementation itself",
    };

    private static readonly System.Text.RegularExpressions.Regex DirectWrite =
        new(@"\bFile\.WriteAllText\b");

    private static void TestNoDirectWriteInAuthoredFormats()
    {
        var root = FindRepoRoot();
        Check("premise: the repository root was found (if not, everything below is vacuous)", root != null);
        if (root == null) return;

        foreach (var rel in AuthoredWriters)
        {
            var full = Path.Combine(root, rel);
            if (!File.Exists(full)) { Check($"premise: {rel} exists", false); continue; }

            bool inBlock = false, inVerbatim = false;
            int hits = 0;
            foreach (var raw in File.ReadAllLines(full))
                if (DirectWrite.IsMatch(SourceScan.StripCommentsAndStrings(raw, ref inBlock, ref inVerbatim)))
                    hits++;
            Check($"★ {rel} - zero direct overwrites (measured {hits})", hits == 0);
        }

        foreach (var kv in Exempt)
            Check($"exemption exists: {kv.Key} ({kv.Value})", File.Exists(Path.Combine(root, kv.Key)));

        SweepWholeRepo(root);
    }

    private static void SweepWholeRepo(string root)
    {
        var offenders = new List<string>();
        int scanned = 0;

        foreach (var dirName in new[] { "Runtime", "Editor", "Gameplay" })
        {
            var dir = Path.Combine(root, dirName);
            if (!Directory.Exists(dir)) { Check($"premise: {dirName}/ is actually scanned", false); continue; }
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (rel.EndsWith("SelfTest.cs", StringComparison.Ordinal)) continue;
                if (Exempt.ContainsKey(rel)) continue;

                scanned++;
                bool inBlock = false, inVerbatim = false;
                foreach (var raw in File.ReadAllLines(file))
                    if (DirectWrite.IsMatch(SourceScan.StripCommentsAndStrings(raw, ref inBlock, ref inVerbatim)))
                    { offenders.Add(rel); break; }
            }
        }

        Check($"premise: the sweep really scanned files ({scanned}; zero would mean the scanner is dead)", scanned > 100);
        Check($"★ exhaustive: zero direct overwrites outside the exemptions (measured {offenders.Count}{(offenders.Count > 0 ? " - " + string.Join(", ", offenders) : "")})",
              offenders.Count == 0);
    }

    private static string? FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "PixelCore.csproj"))) d = d.Parent;
        return d?.FullName;
    }

    private static string CaptureStderr(Action body)
    {
        var prev = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try { body(); }
        finally { Console.SetError(prev); }
        return sw.ToString();
    }

    private static void Check(string what, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  PASS  {what}"); }
        else { _fail++; Console.WriteLine($"  FAIL  {what}{(detail != null ? $" - {detail}" : "")}"); }
    }
}
#endif
