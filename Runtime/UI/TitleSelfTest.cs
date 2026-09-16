#if DEBUG
using System;
using System.Collections.Generic;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.UI;

public static class TitleSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== title screen self-test ===");
        _pass = _fail = 0;

        TestOpenPutsCursorOnFirstEnabled();
        TestCursorSkipsDisabled();
        TestAllDisabledDoesNotHang();
        TestConfirmRunsSelectedOnly();
        TestConfirmIgnoresDisabled();
        TestOpenReplacesInsteadOfStacking();
        TestResetAllRewinds();
        TestActionMayCloseFromInside();

        Console.WriteLine($"=== Title: {_pass} passed, {_fail} failed ===");
        TitleScreen.ResetAll();
    }

    private static void Press(GameAction action)
    {
        int key = InputMap.ScancodesOf(action)[0];
        Input.DebugTick();
        Input.DebugSetKey(key, true);
        TitleScreen.Update();
        Input.DebugTick();
        Input.DebugSetKey(key, false);
    }

    private static TitleItem Item(string label, Action? onChosen = null, bool enabled = true)
        => new(label, onChosen, enabled);

    private static void TestOpenPutsCursorOnFirstEnabled()
    {
        TitleScreen.Open("T", "S", new[]
        {
            Item("locked", null, enabled: false),
            Item("open"),
        });
        Check("* opening puts the cursor on the first enabled item - on a disabled row, pressing does nothing",
              TitleScreen.SelectedIndex == 1, TitleScreen.SelectedIndex.ToString());
    }

    private static void TestCursorSkipsDisabled()
    {
        TitleScreen.Open("T", "S", new[]
        {
            Item("A"),
            Item("locked", null, enabled: false),
            Item("C"),
        });
        Check("premise: the cursor starts at 0 (so the rest is not vacuous)", TitleScreen.SelectedIndex == 0);

        Press(GameAction.MoveDown);
        Check("* moving down skips disabled rows", TitleScreen.SelectedIndex == 2,
              TitleScreen.SelectedIndex.ToString());

        Press(GameAction.MoveDown);
        Check("* it wraps at the end", TitleScreen.SelectedIndex == 0, TitleScreen.SelectedIndex.ToString());

        Press(GameAction.MoveUp);
        Check("* moving up skips disabled rows too (the rule must not differ by direction)",
              TitleScreen.SelectedIndex == 2, TitleScreen.SelectedIndex.ToString());
    }

    private static void TestAllDisabledDoesNotHang()
    {
        TitleScreen.Open("T", "S", new[]
        {
            Item("A", null, enabled: false),
            Item("B", null, enabled: false),
        });
        Press(GameAction.MoveDown);
        Check("* all disabled does not hang or freeze (it stays put)", TitleScreen.SelectedIndex == 0,
              TitleScreen.SelectedIndex.ToString());
    }

    private static void TestConfirmRunsSelectedOnly()
    {
        int a = 0, b = 0;
        TitleScreen.Open("T", "S", new[] { Item("A", () => a++), Item("B", () => b++) });

        Press(GameAction.MoveDown);
        Press(GameAction.Confirm);
        Check("* confirm calls only the item the cursor is on", a == 0 && b == 1, $"a={a} b={b}");
    }

    private static void TestConfirmIgnoresDisabled()
    {
        int hit = 0;
        TitleScreen.Open("T", "S", new[] { Item("locked", () => hit++, enabled: false) });
        Check("premise: with every item disabled the cursor sits on a disabled row", TitleScreen.SelectedIndex == 0);

        Press(GameAction.Confirm);
        Check("* a disabled item is not called on confirm - drawing it dimmed and still running it is the worst lie",
              hit == 0, hit.ToString());
    }

    private static void TestOpenReplacesInsteadOfStacking()
    {
        TitleScreen.Open("T", "S", new[] { Item("A"), Item("B"), Item("C") });
        TitleScreen.Open("T2", "S2", new[] { Item("X") });
        Check("* opening again swaps the contents (no nesting) - overlapping leaves no way to close the one underneath",
              TitleScreen.ItemCount == 1, TitleScreen.ItemCount.ToString());
    }

    private static void TestResetAllRewinds()
    {
        TitleScreen.Open("T", "S", new[] { Item("A") });
        Check("premise: the contamination really took (it is up)", TitleScreen.Active);

        TitleScreen.ResetAll();
        Check("* rewinding closes it", !TitleScreen.Active);
        Check("* the items are cleared too - otherwise the next screen shows the old list for a frame",
              TitleScreen.ItemCount == 0, TitleScreen.ItemCount.ToString());
    }

    private static void TestActionMayCloseFromInside()
    {
        bool ran = false;
        TitleScreen.Open("T", "S", new[]
        {
            Item("an item that closes itself", () => { ran = true; TitleScreen.ResetAll(); }),
        });

        Press(GameAction.Confirm);
        Check("* an item closing the screen from inside its own action is safe (new game does exactly that)",
              ran && !TitleScreen.Active, $"ran={ran} active={TitleScreen.Active}");
    }

    private static void Check(string label, bool ok, string? actual = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  PASS  {label}"); }
        else { _fail++; Console.WriteLine($"  FAIL  {label}" + (actual != null ? $"  (actual {actual})" : "")); }
    }
}
#endif
