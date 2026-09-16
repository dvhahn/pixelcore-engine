using System;

namespace PixelCore.Editor;

public static class GameLoopGuardSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== GameLoopGuard (safety net latch) self-test ===");
        GameLoopGuard.ResetForTest();

        Check("the first occurrence is reported", GameLoopGuard.ShouldReport("update.scene", Caught(ThrowA)));
        Check("* the second at the same surface and site is silent", !GameLoopGuard.ShouldReport("update.scene", Caught(ThrowA)));
        Check("FaultCount counts it even while muted (2)", GameLoopGuard.FaultCount == 2);
        Check("ReportCount counts only what barked (1)", GameLoopGuard.ReportCount == 1);

        Check("a different surface barks again", GameLoopGuard.ShouldReport("draw.world", Caught(ThrowA)));

        GameLoopGuard.ResetForTest();
        Check("premise: A (update.scene) reported", GameLoopGuard.ShouldReport("update.scene", Caught(ThrowA)));
        Check("premise: B (draw.world) reported", GameLoopGuard.ShouldReport("draw.world", Caught(ThrowA)));
        Check("* alternating throws still bark once each - A recurring is silent",
            !GameLoopGuard.ShouldReport("update.scene", Caught(ThrowA)));

        GameLoopGuard.ResetForTest();
        Check("premise: the first site reported", GameLoopGuard.ShouldReport("update.scene", Caught(ThrowA)));
        Check("* the same type at a different site barks again", GameLoopGuard.ShouldReport("update.scene", Caught(ThrowB)));

        GameLoopGuard.ResetForTest();
        Check("premise: a first occurrence with a message is reported", GameLoopGuard.ShouldReport("update.scene", Caught(() => ThrowMsg("index 37"))));
        Check("* the same fault with only a different message is silent (the message is not fingerprinted)",
            !GameLoopGuard.ShouldReport("update.scene", Caught(() => ThrowMsg("index 38"))));

        GameLoopGuard.ResetForTest();
        Check("premise: the first occurrence is reported", GameLoopGuard.ShouldReport("update.scene", Caught(ThrowA)));
        Check("premise: the second is silent", !GameLoopGuard.ShouldReport("update.scene", Caught(ThrowA)));
        GameLoopGuard.Reset();
        Check("* after Reset (a new play session) even the same fault barks again", GameLoopGuard.ShouldReport("update.scene", Caught(ThrowA)));

        GameLoopGuard.ResetForTest();
        Check("an exception with no stack is reported too (it does not blow up)",
            GameLoopGuard.ShouldReport("update.scene", new InvalidOperationException("never thrown")));

        GameLoopGuard.ResetForTest();
        Console.WriteLine($"=== {_pass} passed, {_fail} failed ===");
    }

    private static Exception Caught(Action thrower)
    {
        try { thrower(); }
        catch (Exception ex) { return ex; }
        throw new InvalidOperationException("the thrower did not throw - the test itself is wrong");
    }

    private static void ThrowA() => throw new NullReferenceException("site A");
    private static void ThrowB() => throw new NullReferenceException("site B");
    private static void ThrowMsg(string msg) => throw new ArgumentOutOfRangeException(nameof(msg), msg);

    private static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name);
        if (ok) _pass++; else _fail++;
    }
}
