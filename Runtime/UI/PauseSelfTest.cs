#if DEBUG
using System;
using System.Collections.Generic;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.UI;

public static class PauseSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== Pause self-test ===");
        _pass = _fail = 0;
        try
        {
            TestOpenCloseAndStack();
            TestValueItems();
            TestClosedHookOnce();
            TestDisabledSkipped();
        }
        finally { PauseScreen.ResetAll(); ClearKeys(); }
        Console.WriteLine($"=== Pause: {_pass} passed, {_fail} failed ===");
    }

    private static void TestOpenCloseAndStack()
    {
        PauseScreen.ResetAll();
        Check("premise: closed at first", !PauseScreen.Active && PauseScreen.Depth == 0);

        PauseScreen.Open("Paused", new List<MenuItem> { new("Continue", null), new("Settings", null) });
        Check("opening brings it up", PauseScreen.Active && PauseScreen.Depth == 1);

        PauseScreen.Open("Paused", new List<MenuItem> { new("Continue", null) });
        Check("* opening again while already open does not stack", PauseScreen.Depth == 1);

        PauseScreen.Push("Settings", new List<MenuItem> { new("Back", null) });
        Check("a sub-page is pushed", PauseScreen.Depth == 2 && PauseScreen.Active);

        bool closed = PauseScreen.Back();
        Check("* back from a sub-page does not close (it returns to the previous page)",
              !closed && PauseScreen.Depth == 1 && PauseScreen.Active);

        closed = PauseScreen.Back();
        Check("* back at the root closes entirely", closed && !PauseScreen.Active && PauseScreen.Depth == 0);
    }

    private static void TestValueItems()
    {
        PauseScreen.ResetAll();
        float v = 0.5f;
        bool confirmed = false;
        PauseScreen.Open("Settings", new List<MenuItem>
        {
            new("Volume", () => $"{(int)(v * 100)}%", () => v -= 0.1f, () => v += 0.1f),
            new("Press item", () => confirmed = true),
        });

        Press(GameAction.MoveRight);
        Check("* right raises the value", Math.Abs(v - 0.6f) < 0.001f, v.ToString("0.00"));
        Press(GameAction.MoveLeft); Press(GameAction.MoveLeft);
        Check("* left lowers the value", Math.Abs(v - 0.4f) < 0.001f, v.ToString("0.00"));

        float before = v;
        Press(GameAction.Confirm);
        Check("* confirm is ignored on a value item (a control whose expected effect differs from person to person)",
              Math.Abs(v - before) < 0.001f && PauseScreen.Depth == 1);

        Press(GameAction.MoveDown);
        Press(GameAction.Confirm);
        Check("* control: confirm registers on a press item", confirmed);
        PauseScreen.ResetAll();
    }

    private static void TestClosedHookOnce()
    {
        PauseScreen.ResetAll();
        int closedCount = 0;
        void OnClosed() => closedCount++;

        PauseScreen.Open("Paused", new List<MenuItem> { new("Continue", null) });
        PauseScreen.Push("Settings", new List<MenuItem> { new("Back", null) });

        Press(GameAction.Pause, OnClosed);
        Check("* leaving a sub-page does not call the close hook (saving the settings hangs off it)",
              closedCount == 0 && PauseScreen.Depth == 1, closedCount.ToString());

        Press(GameAction.Pause, OnClosed);
        Check("* closing entirely calls the close hook", closedCount == 1 && !PauseScreen.Active,
              closedCount.ToString());

        Press(GameAction.Pause, OnClosed);
        Check("* after closing it is not called again", closedCount == 1, closedCount.ToString());
    }

    private static void TestDisabledSkipped()
    {
        PauseScreen.ResetAll();
        PauseScreen.Open("Paused", new List<MenuItem>
        {
            new("Continue", null),
            new("Quit to title", null, enabled: false),
            new("Settings", null),
        });
        Check("premise: the cursor is on the first row", PauseScreen.SelectedIndex == 0);
        Press(GameAction.MoveDown);
        Check("* disabled rows are skipped (the cursor does not stop on a row where pressing does nothing)",
              PauseScreen.SelectedIndex == 2, PauseScreen.SelectedIndex.ToString());
        PauseScreen.ResetAll();
    }

    private static void Press(GameAction action, Action? onClosed = null)
    {
        int code = InputMap.ScancodesOf(action)[0];
        Input.DebugTick();
        Input.DebugSetKey(code, true);
        PauseScreen.Update(onClosed);
        Input.DebugTick();
        Input.DebugSetKey(code, false);
    }

    private static void ClearKeys()
    {
        foreach (GameAction a in Enum.GetValues<GameAction>())
            foreach (int c in InputMap.ScancodesOf(a)) Input.DebugSetKey(c, false);
        Input.DebugTick();
    }

    private static void Check(string label, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  [pass] {label}"); }
        else { _fail++; Console.WriteLine($"  [FAIL] {label}{(detail != null ? $" - {detail}" : "")}"); }
    }
}
#endif
