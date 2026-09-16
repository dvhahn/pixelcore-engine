#if DEBUG
using System;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.UI;

public static class MonologueSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== monologue screen self-test ===");
        _pass = _fail = 0;

        TestOpenCoversScreen();
        TestAdvanceIsConsumedOnce();
        TestSilenceSwallowsInsteadOfQueueing();
        TestNewLineDropsCarriedPress();
        TestHeldKeyDoesNotAdvanceTwice();
        TestClosedScreenIgnoresInput();
        TestResetAllRewinds();

        Console.WriteLine($"=== Monologue: {_pass} passed, {_fail} failed ===");
        MonologueScreen.ResetAll();
    }

    private static void Press()
    {
        int key = InputMap.ScancodesOf(GameAction.Confirm)[0];
        Input.DebugTick();
        Input.DebugSetKey(key, true);
        MonologueScreen.Update(inputLive: true, allowMouse: false);
        Input.DebugTick();
        Input.DebugSetKey(key, false);
    }

    private static void Idle()
    {
        Input.DebugTick();
        MonologueScreen.Update(inputLive: true, allowMouse: false);
    }

    private static void TestOpenCoversScreen()
    {
        MonologueScreen.Backdrop = 0f;
        MonologueScreen.Open();
        Check("* opening makes the backdrop fully black - opening translucent would let the setup (room swap) show through for a frame",
              MonologueScreen.Active && MonologueScreen.Backdrop >= 1f,
              $"Active={MonologueScreen.Active} Backdrop={MonologueScreen.Backdrop}");
        Check("opening shows no text (the previous scene's last line must not remain)",
              MonologueScreen.Text.Length == 0);
    }

    private static void TestAdvanceIsConsumedOnce()
    {
        MonologueScreen.Open();
        MonologueScreen.SetLine("A line.");
        Press();
        Check("premise: pressing sets an advance request (so the check below is not vacuous)",
              MonologueScreen.TakeAdvance());
        Check("* a request is consumed only once - otherwise one press advances two lines",
              !MonologueScreen.TakeAdvance());
    }

    private static void TestSilenceSwallowsInsteadOfQueueing()
    {
        MonologueScreen.Open();
        MonologueScreen.SetLine("The kettle clicks off.");
        MonologueScreen.AcceptInput = false;

        Press(); Press(); Press();

        Check("no advance request is set during a silence (input is swallowed)",
              !MonologueScreen.TakeAdvance());

        MonologueScreen.AcceptInput = true;
        Idle();
        Check("* swallowed input does not accumulate - if it did, the moment the silence ends as many lines as were tapped would advance at once",
              !MonologueScreen.TakeAdvance());

        Press();
        Check("   control: a new press after the silence lifts registers", MonologueScreen.TakeAdvance());
    }

    private static void TestNewLineDropsCarriedPress()
    {
        MonologueScreen.Open();
        MonologueScreen.SetLine("First line.");

        int key = InputMap.ScancodesOf(GameAction.Confirm)[0];
        Input.DebugTick();
        Input.DebugSetKey(key, true);
        MonologueScreen.Update(true, false);
        Check("premise: pressing advances the first line (so the check below is not vacuous)", MonologueScreen.TakeAdvance());

        MonologueScreen.SetLine("Second line.");
        Input.DebugTick();
        MonologueScreen.Update(true, false);
        bool carried = MonologueScreen.TakeAdvance();
        Input.DebugSetKey(key, false);

        Check("* with the key still held, the next line is not carried along - otherwise the script passes two lines at a time",
              !carried);
    }

    private static void TestHeldKeyDoesNotAdvanceTwice()
    {
        MonologueScreen.Open();
        MonologueScreen.SetLine("A line.");

        int key = InputMap.ScancodesOf(GameAction.Confirm)[0];
        Input.DebugTick();
        Input.DebugSetKey(key, true);
        MonologueScreen.Update(true, false);
        bool first = MonologueScreen.TakeAdvance();

        Input.DebugTick();
        MonologueScreen.Update(true, false);
        bool second = MonologueScreen.TakeAdvance();

        Input.DebugSetKey(key, false);
        Check("premise: the first pressed frame registers", first);
        Check("* while held it does not advance further (edges only) - otherwise the script passes in an instant",
              !second);
    }

    private static void TestClosedScreenIgnoresInput()
    {
        MonologueScreen.Close();
        Press();
        Check("a closed screen ignores input (an E pressed after the cutscene ends must not leave a phantom request)",
              !MonologueScreen.TakeAdvance());
    }

    private static void TestResetAllRewinds()
    {
        MonologueScreen.Open();
        MonologueScreen.SetLine("A line that must not remain");
        MonologueScreen.Backdrop = 0.4f;
        MonologueScreen.AcceptInput = false;

        MonologueScreen.ResetAll();

        Check("* rewinding leaves no backdrop - otherwise a session interrupted during the opening starts on a black screen",
              !MonologueScreen.Active);
        Check("rewinding clears the line too", MonologueScreen.Text.Length == 0);
        Check("rewinding lifts the input block (the next scene must not be stuck forever)",
              MonologueScreen.AcceptInput);
    }

    private static void Check(string label, bool ok, string? actual = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  PASS  {label}"); }
        else { _fail++; Console.WriteLine($"  FAIL  {label}" + (actual != null ? $"  (measured {actual})" : "")); }
    }
}
#endif
