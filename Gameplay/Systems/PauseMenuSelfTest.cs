#if DEBUG
using System;
using System.Collections.Generic;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.UI;

namespace PixelCore.Gameplay.Systems;

public static class PauseMenuSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== PauseMenu (wiring) self-test ===");
        _pass = _fail = 0;
        try
        {
            TestOpenSurvivesTheFrameItOpened();
            TestGates();
            TestReleaseEscIsPauseOnly();
        }
        finally { PauseScreen.ResetAll(); ClearKeys(); }
        Console.WriteLine($"=== PauseMenu: {_pass} passed, {_fail} failed ===");
    }

    private static void TestOpenSurvivesTheFrameItOpened()
    {
        PauseScreen.ResetAll(); ClearKeys();
        int esc = InputMap.ScancodesOf(GameAction.Pause)[0];

        Input.DebugTick();
        Input.DebugSetKey(esc, true);
        bool active = PauseMenu.Tick(canOpen: true, inputAwake: true);
        Check("* one ESC opens it", active && PauseScreen.Active);

        Input.DebugTick();
        active = PauseMenu.Tick(canOpen: true, inputAwake: true);
        Check("* the same key does not close it immediately on the frame it opened (the bug that never showed a frame on real hardware)",
              active && PauseScreen.Active);

        Input.DebugTick(); Input.DebugSetKey(esc, false);
        PauseMenu.Tick(true, true);
        Input.DebugTick(); Input.DebugSetKey(esc, true);
        active = PauseMenu.Tick(true, true);
        Check("* control: releasing and pressing again closes it", !active && !PauseScreen.Active);
        ClearKeys();
    }

    private static void TestGates()
    {
        int esc = InputMap.ScancodesOf(GameAction.Pause)[0];

        PauseScreen.ResetAll(); ClearKeys();
        Input.DebugTick(); Input.DebugSetKey(esc, true);
        Check("* with canOpen=false it does not open (cutscene, dialogue and not playing all fold into this)",
              !PauseMenu.Tick(canOpen: false, inputAwake: true) && !PauseScreen.Active);

        PauseScreen.ResetAll(); ClearKeys();
        Input.DebugTick(); Input.DebugSetKey(esc, true);
        Check("* without window focus it does not open (a key pressed in the background does not raise the menu)",
              !PauseMenu.Tick(canOpen: true, inputAwake: false) && !PauseScreen.Active);

        PauseScreen.ResetAll(); ClearKeys();
        Input.DebugTick(); Input.DebugSetKey(esc, true);
        PauseMenu.Tick(true, true);
        ClearKeys();
        Check("* a menu that was up stays up when focus is lost", PauseMenu.Tick(true, false) && PauseScreen.Active);
        PauseScreen.ResetAll();
    }

    private static void ClearKeys()
    {
        foreach (GameAction a in Enum.GetValues<GameAction>())
            foreach (int c in InputMap.ScancodesOf(a)) Input.DebugSetKey(c, false);
        Input.DebugTick();
    }

    private static void TestReleaseEscIsPauseOnly()
    {
        const string file = "Game1.cs";
        Check("premise: Game1.cs was read (if it cannot be read, everything below is vacuous)", System.IO.File.Exists(file), file);
        if (!System.IO.File.Exists(file)) return;

        bool inBlock = false;
        var lines = System.Array.ConvertAll(
            System.IO.File.ReadAllLines(file),
            l => Runtime.Core.SourceScan.StripComments(l, ref inBlock));
        var (releaseLines, releaseEsc) = ScanReleaseOnly(lines, "Keys.Escape");

        Check($"premise: a shipping-only region really exists ({releaseLines} lines) - 0 means the scanner is dead",
            releaseLines > 0, releaseLines.ToString());
        Check($"* no Keys.Escape in the shipping-only region (ESC goes only through GameAction.Pause)",
            releaseEsc.Count == 0, string.Join(" / ", releaseEsc));

        var probe = new[]
        {
            "#if DEBUG", "    Foo();", "#else", "    if (k.IsKeyDown(Keys.Escape)) Exit();", "#endif",
        };
        Check("* control: the same scanner catches a planted violation",
            ScanReleaseOnly(probe, "Keys.Escape").Hits.Count == 1);
        Check("   control: the same line in the DEBUG branch is not caught (it looks only at shipping-only code)",
            ScanReleaseOnly(new[] { "#if DEBUG", "    Keys.Escape", "#endif" }, "Keys.Escape").Hits.Count == 0);
        Check("   control: `#if !DEBUG` is shipping-only too",
            ScanReleaseOnly(new[] { "#if !DEBUG", "    Keys.Escape", "#endif" }, "Keys.Escape").Hits.Count == 1);

        var commented = new[] { "#if DEBUG", "  A();", "#else", "  // this used to Exit() on Keys.Escape", "#endif" };
        bool cb = false;
        var stripped = System.Array.ConvertAll(commented, l => Runtime.Core.SourceScan.StripComments(l, ref cb));
        Check("* a comment inside the shipping branch quoting the code is not caught (the strip does its job)",
            ScanReleaseOnly(stripped, "Keys.Escape").Hits.Count == 0,
            string.Join(" / ", ScanReleaseOnly(stripped, "Keys.Escape").Hits));
        Check("   control: without the strip that comment is caught (the check above is not vacuous)",
            ScanReleaseOnly(commented, "Keys.Escape").Hits.Count == 1);
    }

    private static (int Lines, List<string> Hits) ScanReleaseOnly(string[] lines, string needle)
    {
        var stack = new List<bool>();
        var hits = new List<string>();
        int count = 0;
        foreach (var raw in lines)
        {
            var t = raw.Trim();
            if (t.StartsWith("#if"))
            {
                stack.Add(t.Contains("!DEBUG"));
                continue;
            }
            if (t.StartsWith("#elif")) { if (stack.Count > 0) stack[^1] = false; continue; }
            if (t.StartsWith("#else"))
            {
                if (stack.Count > 0) stack[^1] = !stack[^1];
                continue;
            }
            if (t.StartsWith("#endif")) { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); continue; }

            if (!stack.Contains(true)) continue;
            count++;
            if (t.Contains(needle)) hits.Add(t);
        }
        return (count, hits);
    }

    private static void Check(string label, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  [pass] {label}"); }
        else { _fail++; Console.WriteLine($"  [FAIL] {label}{(detail != null ? $" — {detail}" : "")}"); }
    }
}
#endif
