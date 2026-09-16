using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Animation;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Cutscenes;

public static class CutsceneSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== Cutscene self-test ===");

        TestEasing();
        TestTween();
        TestDisposeGuarantee();
        TestAbortReleasesNow();
        TestFreezeRefcount();
        TestExceptionContained();
        TestErrorGrades();
        TestDialogueTiming();
        TestDirector();
        TestParallelForkSignal();
        TestVerbsOnScene();
        TestAnchorScope();
        TestGoToRoom();
        TestCameraDamping();
        TestCinematicBars();
        TestShowHide();
        TestChoiceCancelsSkip();
        TestForkOverlapsSay();
#if DEBUG
        TestAnimGlobalFallback();
        TestSfxNameLookup();
        TestBgmVerbs();
        TestStartRooms();
#endif

        Console.WriteLine($"=== {_pass} passed, {_fail} failed ===");
    }

    private static void TestEasing()
    {
        Console.WriteLine("--- easing ---");

        foreach (Ease e in Enum.GetValues<Ease>())
        {
            Check($"{e}: f(0)=0", MathF.Abs(Easing.Apply(e, 0f)) < 0.0001f);
            Check($"{e}: f(1)=1", MathF.Abs(Easing.Apply(e, 1f) - 1f) < 0.0001f);
            Check($"{e}: out-of-range input is clamped too", MathF.Abs(Easing.Apply(e, 5f) - 1f) < 0.0001f);
        }

        Check("Out is fast at the start", Easing.Apply(Ease.Out, 0.25f) > 0.25f);
        Check("In is slow at the start", Easing.Apply(Ease.In, 0.25f) < 0.25f);
        Check("OutBack overshoots 1 and comes back", Easing.Apply(Ease.OutBack, 0.8f) > 1f);
    }

    private static void TestTween()
    {
        Console.WriteLine("--- tweens ---");

        var runner = new CoroutineRunner();
        float v = -1f;
        runner.Start(Tween.For(runner, 1f, Ease.Linear, p => v = p));

        for (int i = 0; i < 10; i++) runner.Update(0.1f);
        Check("a one-second tween is not done at tick 10 (one start frame)", runner.ActiveCount == 1, $"{runner.ActiveCount} remaining");
        runner.Update(0.1f);
        Check("a one-second tween finishes in one second plus the start frame", runner.ActiveCount == 0, $"{runner.ActiveCount} remaining");
        Check("the final value is exactly 1 (no floating-point error)", v == 1f, v.ToString());

        runner = new CoroutineRunner();
        v = -1f;
        runner.Start(Tween.For(runner, 0f, Ease.Linear, p => v = p));
        runner.Update(0.016f);
        Check("a zero-second tween jumps to the final value", v == 1f, v.ToString());

        runner = new CoroutineRunner();
        v = -1f;
        runner.Start(Tween.For(runner, 1f, Ease.Linear, p => v = p));
        runner.Update(0.016f * 16f);
        runner.Update(0.016f * 16f);
        int ticks = 2;
        while (runner.ActiveCount > 0 && ticks < 100) { runner.Update(0.016f * 16f); ticks++; }
        Check("at 16x a one-second tween finishes in about four ticks", ticks <= 6, $"{ticks} ticks");
        Check("even finishing at speed, the final value is exactly 1", v == 1f, v.ToString());
    }

    private static void TestDisposeGuarantee()
    {
        Console.WriteLine("--- cleanup guaranteed on a forced abort (the basis of RAII) ---");

        var runner = new CoroutineRunner();
        bool ranFinally = false;
        var h = runner.Start(Forever(() => ranFinally = true));
        runner.Update(0.016f);
        Check("while running, the finally has not run yet", !ranFinally);
        h.Stop();
        runner.Update(0.016f);
        Check("the finally runs after Stop()", ranFinally);

        runner = new CoroutineRunner();
        ranFinally = false;
        runner.Start(Forever(() => ranFinally = true));
        runner.Update(0.016f);
        runner.StopAll();
        Check("the finally runs after StopAll()", ranFinally);

        FreezeState.ResetAll();
        runner = new CoroutineRunner();
        var handle = runner.Start(FrozenForever());
        runner.Update(0.016f);
        Check("input is locked while the cutscene runs", FreezeState.CountOf(FreezeFlags.PlayerInput) == 1);
        handle.Stop();
        runner.Update(0.016f);
        Check("* the lock is released even on a forced abort",
            FreezeState.CountOf(FreezeFlags.PlayerInput) == 0,
            $"{FreezeState.CountOf(FreezeFlags.PlayerInput)} remaining");

        FreezeState.ResetAll();
        var orphan = FrozenForever();
        orphan.MoveNext();
        Check("planted failure: skipping Dispose leaves the lock held",
            FreezeState.CountOf(FreezeFlags.PlayerInput) == 1,
            $"{FreezeState.CountOf(FreezeFlags.PlayerInput)} remaining");
        orphan.Dispose();
        Check("planted failure cleanup: disposing finally releases it",
            FreezeState.CountOf(FreezeFlags.PlayerInput) == 0);
    }

    private static void TestAbortReleasesNow()
    {
        Console.WriteLine("--- Abort executes on the spot ---");

        var (ctx, scene) = NewWorld();
        scene.CreateEntity("actor");
        scene.FlushPendingAdds();

        FreezeState.ResetAll();
        CutsceneDirector.ClearRegistry();
        CutsceneDirector.Bind(ctx);
        bool disposed = false;
        CutsceneDirector.Register("check.firstCutscene", c => LongFrozenNoting(c, () => disposed = true));
        CutsceneDirector.Register("check.secondCutscene", LongFrozenScene);

        CutsceneDirector.Play("check.firstCutscene");
        Pump(ctx, 1);
        Check("the first cutscene locked input (premise)",
            FreezeState.CountOf(FreezeFlags.PlayerInput) == 1,
            $"{FreezeState.CountOf(FreezeFlags.PlayerInput)}");

        bool disposedAtAbort;
        int afterNext;
        var prev = Console.Out;
        var buf = new StringWriter();
        try
        {
            Console.SetOut(buf);
            CutsceneDirector.Abort();
            disposedAtAbort = disposed;

            CutsceneDirector.Play("check.secondCutscene");
            Pump(ctx, 1);
            afterNext = FreezeState.CountOf(FreezeFlags.PlayerInput);
        }
        finally { Console.SetOut(prev); }

        Check("by the time Abort returns, the first cutscene's finally has already run (executed, not scheduled)",
            disposedAtAbort);
        Check("* a cutscene started right after Abort runs with the lock held", afterNext == 1, $"{afterNext}");
        Check("★ no spurious 'released more times than acquired' warning (StopAll precedes ResetAll)",
            !buf.ToString().Contains("released more times than acquired"), buf.ToString());

        ctx.Coroutines.StopAll();
        CutsceneDirector.ClearRegistry();
        FreezeState.ResetAll();
    }

    private static IEnumerator<Wait> Forever(Action onFinally)
    {
        try { while (true) yield return Wait.NextFrame; }
        finally { onFinally(); }
    }

    private static IEnumerator<Wait> FrozenForever()
    {
        using var _ = new FreezeScope(FreezeFlags.PlayerInput);
        while (true) yield return Wait.NextFrame;
    }

    private static void TestFreezeRefcount()
    {
        Console.WriteLine("--- freeze refcounts ---");

        FreezeState.ResetAll();
        var outer = new FreezeScope(FreezeFlags.Cutscene);
        var inner = new FreezeScope(FreezeFlags.PlayerInput);

        Check("two levels - an input count of 2", FreezeState.CountOf(FreezeFlags.PlayerInput) == 2);
        inner.Dispose();
        Check("* the inner one ending leaves input still locked", FreezeState.IsFrozen(FreezeFlags.PlayerInput));
        Check("the letterbox too", FreezeState.IsFrozen(FreezeFlags.Letterbox));
        outer.Dispose();
        Check("it releases only once the outer one ends", !FreezeState.IsFrozen(FreezeFlags.PlayerInput));
        Check("the letterbox with it", !FreezeState.IsFrozen(FreezeFlags.Letterbox));

        FreezeState.ResetAll();
        using (var _ = new FreezeScope(FreezeFlags.DialogueOnly))
        {
            Check("DialogueOnly: input locked", FreezeState.IsFrozen(FreezeFlags.PlayerInput));
            Check("DialogueOnly: the letterbox does not come down", !FreezeState.IsFrozen(FreezeFlags.Letterbox));
        }
        FreezeState.ResetAll();
    }

    private static void TestExceptionContained()
    {
        Console.WriteLine("--- exception isolation ---");

        FreezeState.ResetAll();
        var runner = new CoroutineRunner();
        bool otherKeptRunning = false;

        runner.Start(ThrowsAfterFreeze(), "cutscene:thrower");
        runner.Start(Counter(() => otherKeptRunning = true), "cutscene:healthy");

        runner.Update(0.016f);
        runner.Update(0.016f);
        runner.Update(0.016f);

        Check("the runner swallowed the exception (the game does not die)", runner.FaultCount == 1, $"{runner.FaultCount}");
        Check("* the lock is restored even after dying to an exception",
            FreezeState.CountOf(FreezeFlags.PlayerInput) == 0,
            $"{FreezeState.CountOf(FreezeFlags.PlayerInput)} remaining");
        Check("the neighbouring coroutine keeps running", otherKeptRunning);
    }

    private static IEnumerator<Wait> ThrowsAfterFreeze()
    {
        using var _ = new FreezeScope(FreezeFlags.PlayerInput);
        yield return Wait.NextFrame;
        throw new InvalidOperationException("deliberate throw (self-test)");
    }

    private static IEnumerator<Wait> Counter(Action tick)
    {
        while (true) { tick(); yield return Wait.NextFrame; }
    }

    private static void TestErrorGrades()
    {
        Console.WriteLine("--- the three error grades ---");

        var (ctx, scene) = NewWorld();
        scene.CreateEntity("actor");
        scene.FlushPendingAdds();

        CutsceneLog.ResetCounters();
        CutsceneDirector.ClearRegistry();
        CutsceneDirector.Bind(ctx);

        bool reachedEnd = false;
        CutsceneDirector.Register("check.walk", c => MissingAnchor(c, () => reachedEnd = true));
        CutsceneDirector.Play("check.walk");
        Pump(ctx, 30);

        Check("one grade-2 event recorded", CutsceneLog.SkipCount == 1, $"{CutsceneLog.SkipCount}");
        Check("* a missing anchor still lets the cutscene run to the end", reachedEnd);
        Check("the message came from the anchor step (not the actor step)",
            CutsceneLog.LastMessage.Contains("Walk(anchor)"), CutsceneLog.LastMessage);
        Check("the message contains the name that was looked for",
            CutsceneLog.LastMessage.Contains("missingAnchor") && CutsceneLog.LastMessage.Contains("Anchor_missingAnchor"),
            CutsceneLog.LastMessage);
        Check("the message contains the cutscene id", CutsceneLog.LastMessage.Contains("check.walk"), CutsceneLog.LastMessage);
        Check("grade 2 does not prevent the completion record", CutsceneDirector.HasPlayed("check.walk"));

        CutsceneLog.ResetCounters();
        CutsceneDirector.ClearRegistry();
        CutsceneDirector.Register("check.negativeDuration", c => NegativeDuration(c));
        CutsceneDirector.Play("check.negativeDuration");
        Pump(ctx, 10);
        Check("one grade-1 event recorded", CutsceneLog.ArgCount == 1, $"{CutsceneLog.ArgCount}");
        Check("grade 1 does not kill the verb", CutsceneDirector.HasPlayed("check.negativeDuration"));

        CutsceneLog.ResetCounters();
        CutsceneDirector.ClearRegistry();
        CutsceneDirector.Register("check.halt", c => Halts(c));
        CutsceneDirector.Play("check.halt");
        Pump(ctx, 10);
        Check("one grade-3 event recorded", CutsceneLog.HaltCount == 1, $"{CutsceneLog.HaltCount}");
        Check("* an aborted cutscene is not recorded as completed", !CutsceneDirector.HasPlayed("check.halt"));
        Check("the director is empty after an abort", !CutsceneDirector.IsPlaying);

        CutsceneDirector.ClearRegistry();
        FreezeState.ResetAll();
    }

    private static IEnumerator<Wait> MissingAnchor(Cutscene c, Action onEnd)
    {
        yield return c.Walk("actor", "missingAnchor");
        onEnd();
    }

    private static IEnumerator<Wait> NegativeDuration(Cutscene c)
    {
        yield return c.FadeTo(0f, -1f);
    }

    private static IEnumerator<Wait> Halts(Cutscene c)
    {
        CutsceneLog.Halt(c.Id, "check", "deliberate halt");
        yield break;
    }

    private static void TestDialogueTiming()
    {
        Console.WriteLine("--- the dialogue character schedule ---");

        var T = typeof(UI.DialogueTiming);
        Check("an empty string is 0 seconds", UI.DialogueTiming.ReadSeconds("") == 0f);

        Check("five CJK characters is 0.40s",
            MathF.Abs(UI.DialogueTiming.ReadSeconds("一二三四五") - 0.40f) < 0.001f,
            UI.DialogueTiming.ReadSeconds("一二三四五").ToString());

        Check("five Latin characters is 0.20s (half of CJK)",
            MathF.Abs(UI.DialogueTiming.ReadSeconds("hello") - 0.20f) < 0.001f);

        Check("one punctuation mark is 0.25s",
            MathF.Abs(UI.DialogueTiming.ReadSeconds(".") - 0.25f) < 0.001f);
        Check("two CJK characters plus one punctuation mark is 0.41s",
            MathF.Abs(UI.DialogueTiming.ReadSeconds("今日.") - 0.41f) < 0.001f);

        Check("page time = reading plus a one-second tail",
            MathF.Abs(UI.DialogueTiming.PageSeconds("一二三四五") - 1.40f) < 0.001f);

        Check("the same meaning takes different time in different languages",
            UI.DialogueTiming.ReadSeconds("また遅刻") != UI.DialogueTiming.ReadSeconds("Late again"));
    }

    private static void TestDirector()
    {
        Console.WriteLine("--- the director ---");

        var (ctx, _) = NewWorld();
        CutsceneDirector.ClearRegistry();
        CutsceneDirector.Bind(ctx);

        int ran = 0;
        CutsceneDirector.Register("check.short", c => ShortScene(() => ran++));

        Check("playing a missing cutscene returns false", !CutsceneDirector.Play("check.missing"));
        Check("playing a registered cutscene returns true", CutsceneDirector.Play("check.short"));
        Check("the playing flag", CutsceneDirector.IsPlaying);
        Check("* an overlapping playback is refused", !CutsceneDirector.Play("check.short"));

        Pump(ctx, 20);
        Check("once it ends, it is not playing", !CutsceneDirector.IsPlaying);
        Check("it ran exactly once", ran == 1, $"{ran}");
        Check("completion is recorded on the blackboard",
            ctx.Blackboard.GetBool(CutsceneDirector.DoneKeyPrefix + "check.short"));
        Check("HasPlayed reads it", CutsceneDirector.HasPlayed("check.short"));

        FreezeState.ResetAll();
        CutsceneDirector.Register("check.long", c => LongFrozenScene(c));
        CutsceneDirector.Play("check.long");
        Pump(ctx, 3);
        Check("the long cutscene locked input", FreezeState.IsFrozen(FreezeFlags.PlayerInput));
        CutsceneDirector.Stop();
        Pump(ctx, 2);
        Check("* the lock is restored after an abort", !FreezeState.IsFrozen(FreezeFlags.PlayerInput));
        Check("an abort is not recorded as completion", !CutsceneDirector.HasPlayed("check.long"));

        CutsceneDirector.Play("check.long");
        Pump(ctx, 2);
        float before = ctx.Time.TimeScale;
        CutsceneDirector.RequestSkip();
        Check("a skip scales time", ctx.Time.TimeScale == CutsceneDirector.SkipScale, ctx.Time.TimeScale.ToString());
        Check("during a skip the dialogue box does not wait for input", UI.DialogueBox.FastForward);
        CutsceneDirector.Stop();
        Pump(ctx, 2);
        Check("* the time scale is restored when the cutscene ends", ctx.Time.TimeScale == before, ctx.Time.TimeScale.ToString());
        Check("fast-forward turns off too", !UI.DialogueBox.FastForward);

        CutsceneDirector.ClearRegistry();
        FreezeState.ResetAll();
    }

    private static IEnumerator<Wait> ShortScene(Action onRun)
    {
        onRun();
        yield return Wait.Seconds(0.05f);
    }

    private static IEnumerator<Wait> LongFrozenScene(Cutscene c)
    {
        using var _ = c.Freeze();
        yield return Wait.Seconds(600f);
    }

    private static IEnumerator<Wait> LongFrozenNoting(Cutscene c, Action onDisposed)
    {
        using var _ = c.Freeze();
        try { yield return Wait.Seconds(600f); }
        finally { onDisposed(); }
    }

    private static void TestParallelForkSignal()
    {
        Console.WriteLine("--- concurrency ---");

        var (ctx, scene) = NewWorld();
        CutsceneDirector.ClearRegistry();
        CutsceneDirector.Bind(ctx);

        bool joined = false;
        CutsceneDirector.Register("check.parallel", c => ParallelScene(c, () => joined = true));
        CutsceneDirector.Play("check.parallel");

        Pump(ctx, 5);
        Check("while the slower branch is unfinished, the join is unfinished", !joined);
        Pump(ctx, 40);
        Check("* Parallel continues only when all have finished (a join)", joined);

        int forkTicks = 0;
        CutsceneDirector.Register("check.fork", c => ForkScene(c, () => forkTicks++));
        CutsceneDirector.Play("check.fork");
        Pump(ctx, 5);
        int atEnd = forkTicks;
        Check("a forked branch really runs", forkTicks > 0);
        Pump(ctx, 10);
        Check("* remaining forks are reaped when the cutscene ends", forkTicks == atEnd, $"{atEnd} → {forkTicks}");

        bool met = false;
        CutsceneDirector.Register("check.marker", c => SignalScene(c, () => met = true));
        CutsceneDirector.Play("check.marker");
        Pump(ctx, 40);
        Check("* two branches meet at a rendezvous marker", met);

        CutsceneDirector.ClearRegistry();
        FreezeState.ResetAll();
    }

    private static void TestForkOverlapsSay()
    {
        Console.WriteLine("--- overlap (Fork plus Say) ---");

        var (ctx, scene) = NewWorld();
        CutsceneDirector.ClearRegistry();
        CutsceneDirector.Bind(ctx);
        Story.DialogueRunner.Bind(new Blackboard());
        Story.DialogueRunner.Stop();
        UI.DialogueBox.Close();

        int ticks = 0;
        CutsceneDirector.Register("check.overlap", c => ForkOverlapScene(c, () => ticks++));
        CutsceneDirector.Play("check.overlap");

        PumpWithDialogue(ctx, 3);
        Check("* a line is up", UI.DialogueBox.Active, UI.DialogueBox.CurrentText);
        int during = ticks;
        Check("* the forked branch runs meanwhile (an animation overlaps the line)", during > 0,
            $"{during} ticks");

        PumpWithDialogue(ctx, 5);
        Check("* dialogue ticks keep increasing while the fork runs (no interference)", ticks > during,
            $"{during} → {ticks}");

        UI.DialogueBox.Close();
        PumpWithDialogue(ctx, 5);
        Check("* dismissing the line leaves the fork alive", ticks > 0 && !UI.DialogueBox.Active);

        PumpWithDialogue(ctx, 40);
        int atEnd = ticks;
        PumpWithDialogue(ctx, 10);
        Check("* the overlapping branch is reaped when the cutscene ends", ticks == atEnd, $"{atEnd} → {ticks}");
        Check("the cutscene really ended", !CutsceneDirector.IsPlaying);

        Story.DialogueRunner.Stop();
        UI.DialogueBox.Close();
        CutsceneDirector.ClearRegistry();
        FreezeState.ResetAll();
    }

    private static void PumpWithDialogue(GameContext ctx, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            ctx.Coroutines.Update(1f / 60f);
            ctx.Scene?.FlushPendingAdds();
            Story.DialogueRunner.Update(1f / 60f);
        }
    }

    private static IEnumerator<Wait> ForkOverlapScene(Cutscene c, Action tick)
    {
        using var _ = c.Freeze();
        c.Fork(new Act(Counter(tick)));
        yield return c.Actor("actor").Say("this line stays up while they overlap");
    }

    private static IEnumerator<Wait> ParallelScene(Cutscene c, Action onJoin)
    {
        yield return c.Parallel(
            new Act(WaitFor(0.05f)),
            new Act(WaitFor(0.5f)));
        onJoin();
    }

    private static IEnumerator<Wait> ForkScene(Cutscene c, Action tick)
    {
        c.Fork(new Act(Counter(tick)));
        yield return Wait.Seconds(0.05f);
    }

    private static IEnumerator<Wait> SignalScene(Cutscene c, Action onMet)
    {
        c.Fork(new Act(SignalAfter(c, 0.1f)));
        yield return c.WaitSignal("arrived");
        onMet();
    }

    private static IEnumerator<Wait> SignalAfter(Cutscene c, float delay)
    {
        yield return Wait.Seconds(delay);
        yield return c.Signal("arrived");
    }

    private static IEnumerator<Wait> WaitFor(float seconds)
    {
        yield return Wait.Seconds(seconds);
    }

    private static void TestAnchorScope()
    {
        Console.WriteLine("--- anchor resolution ---");

        {
            var (ctx, scene) = NewWorld();
            var lone = scene.CreateEntity("Anchor_bed");
            scene.FlushPendingAdds();

            var c = new Cutscene("check.unique", ctx);
            Check("1. a single global hit is accepted (existing behaviour preserved)", ReferenceEquals(c.Find("bed"), lone));
        }

        {
            var (ctx, scene) = NewWorld();
            var gA = scene.CreateEntity("!check.A");
            var a = scene.CreateEntity("Anchor_doorway"); a.SetParent(gA);
            var gB = scene.CreateEntity("!check.B");
            var b = scene.CreateEntity("Anchor_doorway"); b.SetParent(gB);
            scene.FlushPendingAdds();

            var c = new Cutscene("check.A", ctx);
            Check("2. * a different folder with the same name is not picked (a folder is not a scope)",
                c.Find("doorway") == null, c.PositionOf("doorway")?.ToString() ?? "(null)");

            c.TryFind("doorway", out _, out var why);
            Check("2. * both candidate paths are present - which folder each belongs to is immediately visible",
                why != null && why.Contains("!check.A/Anchor_doorway") && why.Contains("!check.B/Anchor_doorway"),
                why ?? "(none)");
            Check("2. the reason is ambiguity", why != null && why.Contains("ambiguous"), why ?? "(none)");
        }

        {
            var (ctx, scene) = NewWorld();
            scene.CreateEntity("Anchor_window");
            var room = scene.CreateEntity("room");
            var a2 = scene.CreateEntity("Anchor_window"); a2.SetParent(room);
            scene.FlushPendingAdds();

            var c = new Cutscene("check.ambiguous", ctx);
            Check("3. with two global hits it does not hand back whichever came first", c.Find("window") == null);

            c.TryFind("window", out _, out var why);
            Check("3. a root anchor and a child anchor are told apart by path",
                why != null && why.Contains("room/Anchor_window"), why ?? "(none)");
        }

        {
            var (ctx, scene) = NewWorld();
            scene.CreateEntity("Anchor_bed");
            scene.CreateEntity("Anchor_bed");
            var decoy = scene.CreateEntity("bed");
            decoy.GetComponent<Transform>()!.Position = new Vector2(777f, 0f);
            scene.FlushPendingAdds();

            var c = new Cutscene("check.fallthrough", ctx);
            Check("4. * an ambiguous anchor does not fall through to the name-as-written step (it does not take the decoy)",
                c.Find("bed") == null, c.PositionOf("bed")?.ToString() ?? "(null)");

            c.TryFind("bed", out _, out var why);
            Check("4. the candidates are the two anchors, not the decoy",
                why != null && why.Contains("Anchor_bed") && !why.Contains(", bed"), why ?? "(none)");
            Check("4. premise: the decoy really is unique (falling through would have caught it)",
                scene.FindEntities("bed").Count == 1);
        }

        {
            var (ctx, scene) = NewWorld();
            var actor = scene.CreateEntity("hero");
            scene.CreateEntity("Anchor_doorway");
            scene.CreateEntity("Anchor_doorway");
            scene.FlushPendingAdds();

            CutsceneLog.ResetCounters();
            CutsceneDirector.ClearRegistry();
            CutsceneDirector.Bind(ctx);
            CutsceneDirector.Register("check.ambiguousWalk", cc => WalkScene(cc));
            CutsceneDirector.Play("check.ambiguousWalk");
            Pump(ctx, 10);

            Check("5. walking to an ambiguous anchor gives one grade-2 event", CutsceneLog.SkipCount == 1, $"{CutsceneLog.SkipCount}");
            Check("5. * the Skip message says ambiguous rather than absent - the cures differ",
                CutsceneLog.LastMessage.Contains("ambiguous") && !CutsceneLog.LastMessage.Contains("no such anchor"),
                CutsceneLog.LastMessage);
            Check("5. the actor did not move", actor.GetComponent<Transform>()!.Position == Vector2.Zero);
        }
    }

    private static void TestVerbsOnScene()
    {
        Console.WriteLine("--- verbs (on a scene) ---");

        var (ctx, scene) = NewWorld();
        var actor = scene.CreateEntity("hero");
        actor.GetComponent<Transform>()!.Position = Vector2.Zero;
        var anchor = scene.CreateEntity("Anchor_doorway");
        anchor.GetComponent<Transform>()!.Position = new Vector2(48f, 0f);
        scene.FlushPendingAdds();

        CutsceneDirector.ClearRegistry();
        CutsceneDirector.Bind(ctx);

        var c = new Cutscene("check.verb", ctx);
        Check("anchor convention: the script name maps to Anchor_ plus that name", c.Find("doorway") == anchor);
        Check("a name with no prefix is found by the fallback", c.Find("hero") == actor);
        Check("a missing name gives null", c.Find("missing") == null);

        CutsceneDirector.Register("check.walking", cc => WalkScene(cc));
        CutsceneDirector.Play("check.walking");
        Pump(ctx, (int)(24f / Cutscene.DefaultWalkSpeed * 60f));
        var mid = actor.GetComponent<Transform>()!.Position;
        Check("walking - departed and not yet arrived", mid.X > 1f && mid.X < 47f, mid.X.ToString());
        Pump(ctx, 120);
        Check("* Walk arrives exactly at the anchor",
            actor.GetComponent<Transform>()!.Position == new Vector2(48f, 0f),
            actor.GetComponent<Transform>()!.Position.ToString());

        CutsceneDirector.Register("check.placement", cc => PutScene(cc));
        CutsceneDirector.Play("check.placement");
        Pump(ctx, 3);
        Check("Put moves without consuming time",
            actor.GetComponent<Transform>()!.Position == new Vector2(48f, 0f));

        Check("moving right gives R", Cutscene.Suffix(Cutscene.DirOf(new Vector2(10f, 1f))) == "R");
        Check("moving up gives U", Cutscene.Suffix(Cutscene.DirOf(new Vector2(0f, -10f))) == "U");
        Check("on a diagonal the larger axis wins", Cutscene.Suffix(Cutscene.DirOf(new Vector2(-10f, 9f))) == "L");

        CutsceneDirector.ClearRegistry();
        FreezeState.ResetAll();
    }

    private static IEnumerator<Wait> WalkScene(Cutscene c)
    {
        yield return c.Walk("hero", "doorway");
    }

    private static IEnumerator<Wait> PutScene(Cutscene c)
    {
        yield return c.Put("hero", "doorway");
    }

    private static void TestGoToRoom()
    {
        Console.WriteLine("--- GoToRoom (changing rooms) ---");

        var roomId = Assets.AssetRegistry.Instance.FindSceneIdByName(Room);
        Check($"premise: the room '{Room}' is in the registry", roomId != null, roomId ?? "(none)");

        var (ctx, scene) = NewWorld();
        var actor = scene.CreateEntity("hero");
        var oldAnchor = scene.CreateEntity("Anchor_oldRoom");
        oldAnchor.GetComponent<Transform>()!.Position = new Vector2(10f, 0f);
        scene.FlushPendingAdds();

        CutsceneDirector.ClearRegistry();
        CutsceneDirector.Bind(ctx);

        int before = CutsceneLog.SkipCount;
        CutsceneDirector.Register("check.noRoom", cc => GoRoomScene(cc, Room));
        CutsceneDirector.Play("check.noRoom");
        Pump(ctx, 5);
        Check("* 1. a missing loader is reported (not a quiet fallback)", CutsceneLog.SkipCount > before);
        Check("1. the cutscene still does not die (grade 2 - aborting mid-fade would trap a black screen)",
              !CutsceneDirector.IsPlaying);

        CutsceneDirector.RoomLoader = (sceneId, spawn) => false;
        before = CutsceneLog.SkipCount;
        CutsceneDirector.Play("check.noRoom");
        Pump(ctx, 5);
        Check("* 2. a load failure is reported too", CutsceneLog.SkipCount > before);

        bool called = false;
        CutsceneDirector.RoomLoader = (sceneId, spawn) => { called = true; return true; };
        before = CutsceneLog.SkipCount;
        CutsceneDirector.Register("check.emptyRoom", cc => GoRoomScene(cc, ""));
        CutsceneDirector.Play("check.emptyRoom");
        Pump(ctx, 5);
        Check("3. an empty room name reports without calling the loader", !called && CutsceneLog.SkipCount > before);

        called = false;
        before = CutsceneLog.SkipCount;
        CutsceneDirector.Register("check.emptySpawn", cc => GoRoomNoSpawn(cc));
        CutsceneDirector.Play("check.emptySpawn");
        Pump(ctx, 5);
        Check("* 3b. an empty spawn reports without calling the loader (it would contaminate the save resume point)",
              !called && CutsceneLog.SkipCount > before);

        var newScene = new Scene("newRoom");
        var newActor = newScene.CreateEntity("hero");
        var newAnchor = newScene.CreateEntity("Anchor_newRoom");
        newAnchor.GetComponent<Transform>()!.Position = new Vector2(99f, 0f);
        newScene.FlushPendingAdds();

        string? seenRoom = null, seenSpawn = null;
        CutsceneDirector.RoomLoader = (sceneId, spawn) =>
        {
            seenRoom = sceneId; seenSpawn = spawn;
            ctx.Scene = newScene;
            return true;
        };

        CutsceneDirector.Register("check.roomChange", GoRoomThenPut);
        CutsceneDirector.Play("check.roomChange");
        Pump(ctx, 5);

        Check("* 4. the script's room name arrives at the loader as a scene id",
              seenRoom != null && Assets.AssetRegistry.LooksLikeId(seenRoom), seenRoom ?? "(null)");
        Check("* 4. resolving that id back gives the room the script named",
              seenRoom != null &&
              System.IO.Path.GetFileNameWithoutExtension(
                  Assets.AssetRegistry.Instance.GetPath(seenRoom) ?? "") == Room,
              Assets.AssetRegistry.Instance.GetPath(seenRoom ?? "") ?? "(no path)");
        Check("4. the spawn name travels as a name (this being the only name is by design)", seenSpawn == "Spawn_doorway");
        Check("* 4. a Put after the room change catches the new room's actor and anchor (the scene is not cached)",
              newActor.GetComponent<Transform>()!.Position == new Vector2(99f, 0f),
              newActor.GetComponent<Transform>()!.Position.ToString());
        Check("* 4. the old room's actor did not move after the change (still looking at the old scene is caught here)",
              actor.GetComponent<Transform>()!.Position == new Vector2(10f, 0f),
              actor.GetComponent<Transform>()!.Position.ToString());
        Check("4. the old room's anchor is no longer found", new Cutscene("x", ctx).Find("oldRoom") == null);

        Check("5. GoToRoom consumes no time (the next verb also finished within one tick)",
              !CutsceneDirector.IsPlaying);

        called = false;
        CutsceneDirector.RoomLoader = (sceneId, spawn) => { called = true; return true; };
        before = CutsceneLog.SkipCount;
        CutsceneDirector.Register("check.missingRoom", cc => GoRoomScene(cc, "missingRoom_ZZZ"));
        CutsceneDirector.Play("check.missingRoom");
        Pump(ctx, 5);
        Check("* 7. a room name absent from the registry reports without calling the loader",
              !called && CutsceneLog.SkipCount > before);
        Check("7. the cutscene still does not die (grade 2)", !CutsceneDirector.IsPlaying);

        CutsceneDirector.ClearRegistry();
        Check("6. ClearRegistry clears the loader too (preventing contamination between checks)",
              CutsceneDirector.RoomLoader == null);
        FreezeState.ResetAll();
    }

    private const string Room = "Village";

    private static IEnumerator<Wait> GoRoomScene(Cutscene c, string room)
    {
        yield return c.GoToRoom(room, "anywhere");
    }

    private static IEnumerator<Wait> GoRoomNoSpawn(Cutscene c)
    {
        yield return c.GoToRoom(Room, "");
    }

    private static IEnumerator<Wait> GoRoomThenPut(Cutscene c)
    {
        yield return c.Put("hero", "oldRoom");

        yield return c.GoToRoom(Room, "Spawn_doorway");
        yield return c.Put("hero", "newRoom");
    }

    private static void TestCameraDamping()
    {
        Console.WriteLine("--- per-axis camera damping ---");

        var cam = new Camera(320, 180);
        Check("the default is 6 on both axes",
            cam.FollowDamping == new Vector2(6f, 6f), cam.FollowDamping.ToString());
        Check("the scalar facade moves both axes together",
            SetAndRead(cam, 4f) == new Vector2(4f, 4f));

        var scene = new Scene("CamTest");
        var target = scene.CreateEntity("Target");
        scene.FlushPendingAdds();
        target.GetComponent<Transform>()!.Position = new Vector2(100f, 100f);

        cam = new Camera(320, 180) { FollowDamping = new Vector2(2f, 0f) };
        cam.SetTarget(target, snap: false);
        cam.Position = Vector2.Zero;
        cam.Update(1f / 60f);

        Check("* vertical damping 0 means immediate arrival (target Y plus FollowOffset)",
            cam.Position.Y == 100f + cam.FollowOffset.Y, cam.Position.Y.ToString());
        Check("the horizontal axis is damped (not yet arrived)", cam.Position.X < 10f, cam.Position.X.ToString());

        cam.FollowDamping = new Vector2(2.3f, 2.3f);
        using (var _ = new CameraDampingScope(cam, new Vector2(0.4f, 0.4f)))
            Check("inside the scope, the new value", cam.FollowDamping == new Vector2(0.4f, 0.4f));
        Check("* leaving the scope restores the original value", cam.FollowDamping == new Vector2(2.3f, 2.3f));

        cam = new Camera(320, 180);
        cam.Position = new Vector2(500f, 500f);
        cam.SetTarget(target, snap: false);
        cam.Update(1f / 60f);
        Check("* snap:false does not teleport", cam.Position.X > 400f, cam.Position.X.ToString());

        cam = new Camera(320, 180);
        cam.Position = new Vector2(500f, 500f);
        cam.Target = target;
        cam.Update(1f / 60f);
        Check("the property path still snaps (the three play-entry paths are preserved; the snap position is target plus FollowOffset)",
            cam.Position == new Vector2(100f, 100f) + cam.FollowOffset, cam.Position.ToString());
    }

    private static Vector2 SetAndRead(Camera cam, float scalar)
    {
        cam.FollowSpeed = scalar;
        return cam.FollowDamping;
    }

    private static void TestCinematicBars()
    {
        Console.WriteLine("--- the letterbox ---");

        FreezeState.ResetAll();
        CinematicBars.Set(0f);
        CinematicBars.Update(1f);
        Check("with no freeze it does not come down", CinematicBars.Amount == 0f);

        var scope = new FreezeScope(FreezeFlags.Letterbox);
        CinematicBars.Update(CinematicBars.Duration * 0.5f);
        Check("coming down", CinematicBars.Amount > 0f && CinematicBars.Amount < 1f, CinematicBars.Amount.ToString());
        CinematicBars.Update(CinematicBars.Duration);
        Check("it comes all the way down", CinematicBars.Amount == 1f);

        scope.Dispose();
        CinematicBars.Update(CinematicBars.Duration * 2f);
        Check("* releasing the freeze raises it", CinematicBars.Amount == 0f, CinematicBars.Amount.ToString());

        using (var _ = new FreezeScope(FreezeFlags.Cutscene))
        {
            CinematicBars.Update(CinematicBars.Duration);
            Check("the c.Freeze() default lowers the bars", CinematicBars.Amount == 1f);
        }
        CinematicBars.Update(CinematicBars.Duration);
        CinematicBars.Set(0f);
        FreezeState.ResetAll();
    }

    private static void TestChoiceCancelsSkip()
    {
        Console.WriteLine("--- a choice cancels the skip ---");

        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_skipchoice_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var prevLibrary = Story.StoryLibrary.Current;
        try
        {
            File.WriteAllText(Path.Combine(dir, "question.story"), """
                # check.skip

                @question
                hero: Which way is it.
                > Left
                    hero: To the left.
                > Right
                    hero: To the right.
                """);
            Story.StoryLibrary.UseLibrary(Story.StoryLibrary.Load(dir));

            var (ctx, scene) = NewWorld();
            scene.FlushPendingAdds();
            Story.DialogueRunner.Bind(ctx.Blackboard);
            Story.DialogueRunner.Stop();
            UI.DialogueBox.Close();

            CutsceneDirector.ClearRegistry();
            CutsceneDirector.Bind(ctx);
            CutsceneDirector.Register("check.skipCutscene", c => SayOnly(c, "check.skip.question"));
            CutsceneDirector.Play("check.skipCutscene");

            Pump(ctx, 2);
            CutsceneDirector.RequestSkip();
            Check("premise: the skip really turned on (otherwise the rest is vacuous)",
                  CutsceneDirector.Skipping && UI.DialogueBox.FastForward);

            for (int i = 0; i < 30 && !UI.DialogueBox.ChoiceActive; i++)
            {
                ctx.Coroutines.Update(1f / 60f);
                UI.DialogueBox.Update(1f / 60f);
                Story.DialogueRunner.Update(1f / 60f);
            }

            Check("* even during a skip it flows as far as the choice (fast-forward dismisses the first line)",
                  UI.DialogueBox.ChoiceActive);
            Check("** the skip is cancelled the moment a choice appears (otherwise an unchosen answer gets picked)",
                  !CutsceneDirector.Skipping);
            Check("* fast-forward turns off with it (cancelling only the time scale would page straight through the list)",
                  !UI.DialogueBox.FastForward);
            Check("* the time scale is back to normal (the player is not left waiting at 16x)",
                  Math.Abs(ctx.Time.TimeScale - 1f) < 0.001f, ctx.Time.TimeScale.ToString("0.##"));

            for (int i = 0; i < 10; i++)
            {
                UI.DialogueBox.Update(1f / 60f);
                Story.DialogueRunner.Update(1f / 60f);
            }
            Check("* and it waits for a person to choose", UI.DialogueBox.ChoiceActive);
        }
        finally
        {
            CutsceneDirector.Abort();
            Story.DialogueRunner.Stop();
            UI.DialogueBox.Close();
            UI.DialogueBox.FastForward = false;
            Story.StoryLibrary.UseLibrary(prevLibrary);
            CutsceneDirector.ClearRegistry();
            FreezeState.ResetAll();
            try { Directory.Delete(dir, true); } catch {  }
        }
    }

    private static IEnumerator<Wait> SayOnly(Cutscene c, string blockId)
    {
        using var _ = c.Freeze();
        yield return c.Say(blockId);
    }

    private static void TestShowHide()
    {
        Console.WriteLine("--- show and hide ---");

        {
            var (ctx, scene) = NewWorld();
            CutsceneDirector.ClearRegistry();
            CutsceneDirector.Bind(ctx);

            var npc = scene.CreateEntity("neighbour");
            npc.AddComponent<Transform>();
            scene.FlushPendingAdds();

            CutsceneLog.ResetCounters();
            CutsceneDirector.Register("check.hide", c => HideOnly(c, "neighbour"));
            CutsceneDirector.Play("check.hide");
            Pump(ctx, 5);

            Check("1. * Hide really turns it off", !npc.Active);
            Check("1. zero grade logs (the normal path is silent)", CutsceneLog.SkipCount == 0, CutsceneLog.LastMessage);

            CutsceneDirector.Register("check.show", c => ShowOnly(c, "neighbour"));
            CutsceneDirector.Play("check.show");
            Pump(ctx, 5);
            Check("1. * Show turns it back on (the pair matches)", npc.Active);
        }

        {
            var (ctx, scene) = NewWorld();
            CutsceneDirector.ClearRegistry();
            CutsceneDirector.Bind(ctx);
            scene.FlushPendingAdds();

            CutsceneLog.ResetCounters();
            CutsceneDirector.Register("check.missingActor", c => HideOnly(c, "nobody"));
            CutsceneDirector.Play("check.missingActor");
            Pump(ctx, 5);

            Check("2. * a missing actor is skipped as grade 2 (it does not kill the cutscene with an NRE)",
                CutsceneLog.SkipCount == 1, $"{CutsceneLog.SkipCount} - {CutsceneLog.LastMessage}");
            Check("2. it is not grade 3 (an abort)", CutsceneLog.HaltCount == 0, $"{CutsceneLog.HaltCount}");
            Check("2. the message names what was looked for",
                CutsceneLog.LastMessage.Contains("nobody"), CutsceneLog.LastMessage);
        }

        {
            var (ctx, scene) = NewWorld();
            CutsceneDirector.ClearRegistry();
            CutsceneDirector.Bind(ctx);

            var after = scene.CreateEntity("nextPerson");
            after.AddComponent<Transform>();
            scene.FlushPendingAdds();

            CutsceneLog.ResetCounters();
            CutsceneDirector.Register("check.continue", c => HideTwo(c, "nobody", "nextPerson"));
            CutsceneDirector.Play("check.continue");
            Pump(ctx, 5);

            Check("3. * one missing actor still lets the next verb run (why grade 2 exists)",
                !after.Active, "the next entity is still active = the cutscene died on the first line");
        }

        {
            var (ctx, scene) = NewWorld();
            CutsceneDirector.ClearRegistry();
            CutsceneDirector.Bind(ctx);
            scene.FlushPendingAdds();

            CutsceneLog.ResetCounters();
            CutsceneDirector.Register("check.emptyName", c => HideOnly(c, ""));
            CutsceneDirector.Play("check.emptyName");
            Pump(ctx, 5);

            Check("4. an empty name is grade 2 too", CutsceneLog.SkipCount == 1, $"{CutsceneLog.SkipCount}");
        }

        CutsceneDirector.ClearRegistry();
        FreezeState.ResetAll();
    }

    private static IEnumerator<Wait> HideOnly(Cutscene c, string actor)
    {
        yield return c.Hide(actor);
    }

    private static IEnumerator<Wait> ShowOnly(Cutscene c, string actor)
    {
        yield return c.Show(actor);
    }

    private static IEnumerator<Wait> HideTwo(Cutscene c, string first, string second)
    {
        yield return c.Hide(first);
        yield return c.Hide(second);
    }

#if DEBUG

    private static void TestAnimGlobalFallback()
    {
        Console.WriteLine("--- the Anim global fallback ---");

        using (var t = new TempClips())
        {
            t.WriteAtlas("atlasA", "A.atlas");
            t.WriteClip("WakeUp", "clip0001", "atlasA", loop: false);

            var (ctx, animator, actor) = t.NewActor();
            Check("1. starting state: zero clips registered on the actor", animator.ClipCount == 0);

            var log = t.PlayAnim(ctx, "check.fallback", "WakeUp", frames: 60);

            Check("1. * an unregistered clip plays by name alone",
                animator.CurrentClip?.Name == "WakeUp", animator.CurrentClip?.Name ?? "(none)");
            Check("1. zero grade-2 events (a fallback is not a failure)", CutsceneLog.SkipCount == 0, $"{CutsceneLog.SkipCount}");
            Check("1. the fallback is recorded on the console (actor, clip, source path)",
                log.Contains("global fallback") && log.Contains("hero") && log.Contains("WakeUp.anim"), log);
            Check("1. a non-looping fallback clip was waited on to completion (the cutscene ended)", !CutsceneDirector.IsPlaying);

            Check("1. * a temporary clip does not enter the actor's list", animator.ClipCount == 0, $"{animator.ClipCount}");

            var data = new Serialization.AnimatorData();
            data.Capture(actor);
            Check("3. * saving while playing does not bake a one-off animation into the scene file",
                data.Clips.Count == 0, string.Join(",", data.Clips));
        }

        using (var t = new TempClips())
        {
            t.WriteAtlas("atlasA", "A.atlas");
            t.WriteClip("Hug", "clip0002", "atlasA", loop: false, sub: "Hero_Summer");
            t.WriteClip("Hug", "clip0003", "atlasA", loop: false, sub: "Hero_Winter");

            var (ctx, animator, _) = t.NewActor();
            t.PlayAnim(ctx, "check.ambiguous", "Hug", frames: 10);

            Check("2. * an ambiguous name is not played", animator.CurrentClip == null,
                animator.CurrentClip?.Name ?? "(none)");
            Check("2. one grade-2 event", CutsceneLog.SkipCount == 1, $"{CutsceneLog.SkipCount}");
            Check("2. the message says ambiguous", CutsceneLog.LastMessage.Contains("ambiguous"), CutsceneLog.LastMessage);
            Check("2. * both candidate paths are in the message",
                CutsceneLog.LastMessage.Contains("Hero_Summer/Hug.anim")
                && CutsceneLog.LastMessage.Contains("Hero_Winter/Hug.anim"), CutsceneLog.LastMessage);
        }

        using (var t = new TempClips())
        {
            t.WriteAtlas("atlasA", "A.atlas");
            t.WriteClip("Idle_D", "clip0004", "atlasA", loop: true);

            var (ctx, animator, _) = t.NewActor();
            animator.AddClip(new AnimationClip("Idle_D"));
            t.PlayAnim(ctx, "check.absent", "Sneeze", frames: 10);

            Check("4. one grade-2 event", CutsceneLog.SkipCount == 1, $"{CutsceneLog.SkipCount}");
            Check("4. the message contains the registered count (half of why it is missing)",
                CutsceneLog.LastMessage.Contains("the 1 clips registered"), CutsceneLog.LastMessage);
            Check("4. the message says zero globally (the other half)",
                CutsceneLog.LastMessage.Contains("0 globally"), CutsceneLog.LastMessage);
            Check("4. the name looked for is in the message", CutsceneLog.LastMessage.Contains("Sneeze"), CutsceneLog.LastMessage);
        }

        using (var t = new TempClips())
        {
            t.WriteAtlas("atlasA", "A.atlas");
            t.WriteClip("Idle_D", "clip0005", "atlasA", loop: true, sub: "Hero_Summer");
            t.WriteClip("Idle_D", "clip0006", "atlasA", loop: true, sub: "Hero_Winter");

            var (ctx, animator, _) = t.NewActor();
            var registered = new AnimationClip("Idle_D") { Loop = true };
            registered.AddFrame(new AnimationFrame(new Rectangle(0, 0, 8, 8), 0.1f));
            animator.AddClip(registered);

            var log = t.PlayAnim(ctx, "check.priority", "Idle_D", frames: 10);

            Check("5. * a registered clip beats a global namesake",
                ReferenceEquals(animator.CurrentClip, registered), animator.CurrentClip?.Name ?? "(none)");
            Check("5. * the global set is not consulted at all - no ambiguity decision is made",
                CutsceneLog.SkipCount == 0, CutsceneLog.LastMessage);
            Check("5. no fallback line is printed either", !log.Contains("global fallback"), log);
        }
    }

    private static void TestSfxNameLookup()
    {
        Console.WriteLine("--- unique Sfx name matching ---");

        using (var t = new TempClips())
        {
            t.WriteAudio("sfx00001", "Audio/SFX/Normal/Alarm.wav");
            var log = t.PlaySfx("check.unique", "Alarm");

            Check("1. * a unique name attempts playback (zero grade-2 events)",
                CutsceneLog.SkipCount == 0, CutsceneLog.LastMessage);
            Check("1. * what audio received is a path, not a name (the chain connected end to end)",
                log.Contains("Audio/SFX/Normal/Alarm.wav"), log);
        }

        using (var t = new TempClips())
        {
            t.WriteAudio("sfx00002", "Audio/SFX/Normal/Door.wav");
            t.WriteAudio("sfx00003", "Audio/SFX/Winter/Door.wav");
            t.PlaySfx("check.ambiguous", "Door");

            Check("2. * an ambiguous name is skipped (a sound the author does not know about is worse than silence)",
                CutsceneLog.SkipCount == 1, $"{CutsceneLog.SkipCount}");
            Check("2. the message says ambiguous",
                CutsceneLog.LastMessage.Contains("ambiguous"), CutsceneLog.LastMessage);
            Check("2. * both candidate paths are in the message (a list of ids would not say which file)",
                CutsceneLog.LastMessage.Contains("Audio/SFX/Normal/Door.wav")
                && CutsceneLog.LastMessage.Contains("Audio/SFX/Winter/Door.wav"), CutsceneLog.LastMessage);
        }

        using (var t = new TempClips())
        {
            t.WriteAudio("sfx00004", "Audio/SFX/Normal/Alarm.wav");
            t.PlaySfx("check.absent", "Sneeze");

            Check("3. a missing name is skipped", CutsceneLog.SkipCount == 1, $"{CutsceneLog.SkipCount}");
            Check("3. the message contains zero-globally and the name looked for",
                CutsceneLog.LastMessage.Contains("0 globally")
                && CutsceneLog.LastMessage.Contains("Sneeze"), CutsceneLog.LastMessage);
        }

        using (var t = new TempClips())
        {
            t.WriteAudio("sfx00005", "Audio/SFX/Normal/Alarm.wav");
            var log = t.PlaySfx("check.path", "Audio/SFX/Normal/Alarm");

            Check("4. * a path input is skipped (an old call does not quietly survive)",
                CutsceneLog.SkipCount == 1, $"{CutsceneLog.SkipCount}");
            Check("4. the message prescribes names only",
                CutsceneLog.LastMessage.Contains("names only"), CutsceneLog.LastMessage);
            Check("4. * a refusal means no playback attempt at all (even a path that happens to be right stays silent)",
                !log.Contains("Sound not found"), log);
        }

        using (var t = new TempClips())
        {
            t.WriteAudio("sfx00007", "Audio/SFX/Normal/Horn.mp3");
            var log = t.PlaySfx("check.mp3", "Horn");

            Check("4b. * .mp3 is not a candidate - the engine cannot read it, so it must not pretend it exists",
                CutsceneLog.SkipCount == 1, $"{CutsceneLog.SkipCount}");
            Check("4b. the message says zero globally (absent, not ambiguous)",
                CutsceneLog.LastMessage.Contains("0 globally"), CutsceneLog.LastMessage);
            Check("4b. * no playback attempt at all (it does not leak into the path where AudioManager reports instead)",
                !log.Contains("Sound not found"), log);
        }

        using (var t = new TempClips())
        {
            t.WriteAudio("sfx00006", "Audio/SFX/Normal/Bell.wav");
            AssetRegistry.Instance.Register("img00001", "Sprites/Environments/Bell.png");
            var log = t.PlaySfx("check.extension", "Bell");

            Check("5. * a png with the same name is not a candidate (only audio extensions are considered)",
                CutsceneLog.SkipCount == 0, CutsceneLog.LastMessage);
            Check("5. the audio path really was passed",
                log.Contains("Audio/SFX/Normal/Bell.wav"), log);
        }
    }

    private static void TestBgmVerbs()
    {
        Console.WriteLine("--- Bgm / StopBgm ---");

        using (var t = new TempClips())
        {
            t.WriteAudio("bgm00001", "Audio/BGM/Theme.wav");
            var log = t.PlayBgm("check.musicUnique", "Theme");

            Check("1. * a unique name attempts playback (zero grade-2 events)",
                CutsceneLog.SkipCount == 0, CutsceneLog.LastMessage);
            Check("1. * what audio received is a path, not a name (the chain connected end to end)",
                log.Contains("Audio/BGM/Theme.wav"), log);
        }

        using (var t = new TempClips())
        {
            t.WriteAudio("bgm00002", "Audio/BGM/Town.wav");
            t.WriteAudio("bgm00003", "Audio/BGM/Winter/Town.wav");
            t.PlayBgm("check.musicAmbiguous", "Town");

            Check("2. * an ambiguous name is skipped", CutsceneLog.SkipCount == 1, $"{CutsceneLog.SkipCount}");
            Check("2. every candidate path is in the message",
                CutsceneLog.LastMessage.Contains("Audio/BGM/Town.wav")
                && CutsceneLog.LastMessage.Contains("Audio/BGM/Winter/Town.wav"), CutsceneLog.LastMessage);
        }

        using (var t = new TempClips())
        {
            t.WriteAudio("bgm00004", "Audio/BGM/Field.wav");
            var log = t.PlayBgm("check.musicPath", "Audio/BGM/Field");

            Check("3. * a path input is skipped (the same rule as Sfx)",
                CutsceneLog.SkipCount == 1, $"{CutsceneLog.SkipCount}");
            Check("3. the message prescribes names only",
                CutsceneLog.LastMessage.Contains("names only"), CutsceneLog.LastMessage);
            Check("3. * a refusal means no playback attempt at all", !log.Contains("Sound not found"), log);
        }

        using (var t = new TempClips())
        {
            t.WriteAudio("bgm00005", "Audio/BGM/Dawn.wav");
            t.PlayBgm("check.musicMissing", "Nocturne");

            Check("4. a missing name is grade 2", CutsceneLog.SkipCount == 1, $"{CutsceneLog.SkipCount}");
            Check("4. the message contains zero-globally and the name looked for",
                CutsceneLog.LastMessage.Contains("0 globally")
                && CutsceneLog.LastMessage.Contains("Nocturne"), CutsceneLog.LastMessage);
        }

        using (var t = new TempClips())
        {
            t.WriteAudio("dup00001", "Audio/SFX/Chime.wav");
            t.WriteAudio("dup00002", "Audio/BGM/Chime.wav");

            t.PlaySfx("check.control", "Chime");
            int sfxSkips = CutsceneLog.SkipCount;
            string sfxMsg = CutsceneLog.LastMessage;

            t.PlayBgm("check.control", "Chime");
            int bgmSkips = CutsceneLog.SkipCount;
            string bgmMsg = CutsceneLog.LastMessage;

            Check("5. premise: that input really is ambiguous (otherwise the rest is vacuous)", sfxSkips == 1, $"{sfxSkips}");
            Check("5. * Sfx and Bgm reach the same decision on the same input (one copy of the resolution)",
                sfxSkips == bgmSkips, $"Sfx {sfxSkips} / Bgm {bgmSkips}");
            Check("5. * only the verb name differs; the diagnostic wording is the same",
                sfxMsg.Replace("Sfx", "§") == bgmMsg.Replace("Bgm", "§"),
                $"{sfxMsg}  ||  {bgmMsg}");
        }

        {
            var (ctx, _) = NewWorld();
            CutsceneDirector.ClearRegistry();
            CutsceneDirector.Bind(ctx);

            CutsceneLog.ResetCounters();
            CutsceneDirector.Register("check.musicStop", c => StopBgmOnly(c, 0f));
            CutsceneDirector.Play("check.musicStop");
            Pump(ctx, 5);
            Check("6. StopBgm is silent (there is no name to resolve)",
                CutsceneLog.SkipCount == 0 && CutsceneLog.ArgCount == 0, CutsceneLog.LastMessage);

            CutsceneLog.ResetCounters();
            CutsceneDirector.Register("check.musicStopNegative", c => StopBgmOnly(c, -1f));
            CutsceneDirector.Play("check.musicStopNegative");
            Pump(ctx, 5);
            Check("6. * a negative fade reports as grade 1 and continues with 0 (it does not kill the verb)",
                CutsceneLog.ArgCount == 1 && CutsceneLog.SkipCount == 0,
                $"①{CutsceneLog.ArgCount} ②{CutsceneLog.SkipCount}");
        }

        Audio.AudioManager.Instance.StopBGM(0f);
        CutsceneDirector.ClearRegistry();
        FreezeState.ResetAll();
    }

    private static IEnumerator<Wait> StopBgmOnly(Cutscene c, float fade)
    {
        yield return c.StopBgm(fade);
    }

    private static void TestStartRooms()
    {
        Console.WriteLine("--- start rooms for the sweep harness ---");

        using (var t = new TempRooms())
        {
            t.WriteRoom("Scenes/House.scene", "room0001",
                (1, "Trigger_wakeUp", "cutsceneTrigger", "house.wakeUp"),
                (2, "Back Door", "interactable", "house.backDoor"));
            t.WriteRoom("Scenes/main.scene", "room0002",
                (3, "BackDoor", "interactable", "cellar.door"));
            t.WritePrefab("Prefabs/Hero.scene", "pref0001",
                (4, "Trigger_ghost", "cutsceneTrigger", "ghost.cutscene"));

            var problems = new List<string>();
            var map = CutsceneRegression.BuildStartRooms(problems);

            Check("1. derived from a trigger", map.TryGetValue("house.wakeUp", out var a)
                && a.SceneId == "room0001" && a.Spawn == "Trigger_wakeUp" && a.Source == "trigger",
                map.TryGetValue("house.wakeUp", out var d1) ? $"{d1.SceneId}/{d1.Spawn}/{d1.Source}" : "(none)");
            Check("1. * derived from an interactable (Kind=Event) too - there are two doors",
                map.TryGetValue("house.backDoor", out var b)
                && b.SceneId == "room0001" && b.Spawn == "Back Door" && b.Source == "interactable",
                map.TryGetValue("house.backDoor", out var d2) ? $"{d2.SceneId}/{d2.Spawn}/{d2.Source}" : "(none)");
            Check("1. a cutscene in another room maps to that room", map.TryGetValue("cellar.door", out var c) && c.SceneId == "room0002");
            Check("1. * a prefab is not a room (a trigger inside a prefab is not counted)",
                !map.ContainsKey("ghost.cutscene"));
            Check("1. a normal derivation gives zero problems", problems.Count == 0, string.Join(" / ", problems));
        }

        using (var t = new TempRooms())
        {
            t.WriteRoom("Scenes/House.scene", "room0001", (1, "Trigger_A", "cutsceneTrigger", "overlap.cutscene"));
            t.WriteRoom("Scenes/World.scene", "room0002", (2, "Trigger_B", "cutsceneTrigger", "overlap.cutscene"));

            var problems = new List<string>();
            var map = CutsceneRegression.BuildStartRooms(problems);

            Check("2. * trigger points in two rooms are not resolved arbitrarily", !map.ContainsKey("overlap.cutscene"));
            Check("2. it is recorded as one problem", problems.Count == 1, string.Join(" / ", problems));
            Check("2. * it names which rooms (with a fixed sort order)",
                problems.Count == 1 && problems[0].Contains("House/Trigger_A") && problems[0].Contains("World/Trigger_B"),
                problems.Count > 0 ? problems[0] : "(none)");
        }

        using (var t = new TempRooms())
        {
            t.WriteRoom("Scenes/House.scene", "room0001", (1, "justAProp", "interactable", null));

            var problems = new List<string>();
            var map = CutsceneRegression.BuildStartRooms(problems);
            Check("3. * a cutscene with no trigger point is absent from the map (no quiet fallback to the current scene)",
                !map.ContainsKey("codeOnly.cutscene") && map.Count == 0, $"{map.Count}");
        }

        using (var t = new TempRooms())
        {
            t.WriteRoom("Scenes/House.scene", "room0001");

            CutsceneDirector.ClearRegistry();
            CutsceneDirector.Register("codeOnly.cutscene", _ => Empty(), room: "House", spawn: "Spawn_House");

            var problems = new List<string>();
            var map = CutsceneRegression.BuildStartRooms(problems);
            Check("4. a declaration is used when present (only when there is no trigger point)",
                map.TryGetValue("codeOnly.cutscene", out var v)
                && v.SceneId == "room0001" && v.Spawn == "Spawn_House" && v.Source == "declared",
                map.TryGetValue("codeOnly.cutscene", out var d) ? $"{d.SceneId}/{d.Spawn}/{d.Source}" : "(none)");

            CutsceneDirector.ClearRegistry();
            CutsceneDirector.Register("codeOnly.cutscene", _ => Empty(), room: "missingRoom_ZZZ");
            problems.Clear();
            map = CutsceneRegression.BuildStartRooms(problems);
            Check("4. a declared room that does not exist is recorded as a problem (it does not pass silently)",
                !map.ContainsKey("codeOnly.cutscene") && problems.Count == 1
                && problems[0].Contains("missingRoom_ZZZ"), string.Join(" / ", problems));

            CutsceneDirector.ClearRegistry();
        }

        using (var t = new TempRooms())
        {
            t.WriteRoom("Scenes/House.scene", "room0001", (1, "Trigger_real", "cutsceneTrigger", "both.cutscene"));
            t.WriteRoom("Scenes/World.scene", "room0002");

            CutsceneDirector.ClearRegistry();
            CutsceneDirector.Register("both.cutscene", _ => Empty(), room: "World", spawn: "staleDeclaration");

            var problems = new List<string>();
            var map = CutsceneRegression.BuildStartRooms(problems);
            Check("5. * scene data beats a hand declaration (a stale declaration does not cover the truth)",
                map.TryGetValue("both.cutscene", out var v) && v.SceneId == "room0001" && v.Source == "trigger",
                map.TryGetValue("both.cutscene", out var d) ? $"{d.SceneId}/{d.Spawn}/{d.Source}" : "(none)");
            CutsceneDirector.ClearRegistry();
        }
    }

    private static IEnumerator<Wait> Empty() { yield break; }

    private sealed class TempRooms : IDisposable
    {
        private readonly string _root;
        private readonly IDisposable _scope;
        private readonly string _prevRoot;

        public TempRooms()
        {
            _root = Path.Combine(Path.GetTempPath(), "pixelcore_rooms_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_root);
            _scope = AssetRegistry.UseTemporary(Path.Combine(_root, "assets.json"));
            _prevRoot = Components.SceneInstance.ContentRoot;
            Components.SceneInstance.ContentRoot = _root;
        }

        public void WriteRoom(string rel, string id, params (int Id, string Name, string Type, string? Cutscene)[] spots)
            => Write(rel, id, "Level", spots);

        public void WritePrefab(string rel, string id, params (int Id, string Name, string Type, string? Cutscene)[] spots)
            => Write(rel, id, "Prefab", spots);

        private void Write(string rel, string id, string kind, (int Id, string Name, string Type, string? Cutscene)[] spots)
        {
            var ents = new List<string>();
            foreach (var s in spots)
            {
                string comp = s.Type == "cutsceneTrigger"
                    ? $$"""{ "type": "cutsceneTrigger", "cutsceneId": "{{s.Cutscene}}" }"""
                    : s.Cutscene == null
                        ? """{ "type": "interactable", "kind": "Read" }"""
                        : $$"""{ "type": "interactable", "kind": "Event", "event": "{{s.Cutscene}}" }""";
                ents.Add($$"""
                    { "id": {{s.Id}}, "name": "{{s.Name}}", "components": [ {{comp}} ] }
                """);
            }

            var body = string.Join(",\n", ents);
            var full = Path.Combine(_root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, $$"""
            {
              "id": "{{id}}",
              "name": "{{Path.GetFileNameWithoutExtension(rel)}}",
              "kind": "{{kind}}",
              "schemaVersion": {{Serialization.SceneData.CurrentSchemaVersion}},
              "entities": [
            {{body}}
              ]
            }
            """);
            AssetRegistry.Instance.Register(id, rel);
        }

        public void Dispose()
        {
            Components.SceneInstance.ContentRoot = _prevRoot;
            _scope.Dispose();
            try { Directory.Delete(_root, true); } catch {  }
        }
    }

    private sealed class TempClips : IDisposable
    {
        private readonly string _root;
        private readonly IDisposable _scope;
        private readonly string _prevRoot;

        public TempClips()
        {
            _root = Path.Combine(Path.GetTempPath(), "pixelcore_animfb_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_root);
            _scope = AssetRegistry.UseTemporary(Path.Combine(_root, "assets.json"));
            _prevRoot = AnimClipCache.ContentRoot;
            AnimClipCache.ContentRoot = _root;
            AnimClipCache.Clear();
            CutsceneLog.ResetCounters();
        }

        public void WriteAtlas(string id, string rel)
        {
            var atlas = new SpriteAtlas { Id = id, TexturePath = Path.ChangeExtension(rel, ".png"), CellWidth = 8, CellHeight = 8 };
            for (int i = 0; i < 4; i++)
                atlas.Slices.Add(new SpriteSlice { Name = $"sprite_{i}", X = i * 8, Width = 8, Height = 8 });
            var full = Path.Combine(_root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            atlas.Save(full);
            AssetRegistry.Instance.Register(id, rel);
        }

        public void WriteClip(string name, string id, string atlasId, bool loop, string? sub = null)
        {
            var data = new AnimationData { Id = id, Name = name, AtlasId = atlasId, Loop = loop };
            data.Frames.Add(new FrameData { SliceIndex = 0, Duration = 0.1f });
            data.Frames.Add(new FrameData { SliceIndex = 1, Duration = 0.1f });

            var rel = sub == null ? name + ".anim" : sub + "/" + name + ".anim";
            var full = Path.Combine(_root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            data.Save(full);
            AssetRegistry.Instance.Register(id, rel);
        }

        public void WriteAudio(string id, string rel) => AssetRegistry.Instance.Register(id, rel);

        public string PlaySfx(string cutsceneId, string name)
        {
            CutsceneLog.ResetCounters();
            var (ctx, _) = NewWorld();
            CutsceneDirector.ClearRegistry();
            CutsceneDirector.Bind(ctx);

            var prev = Console.Out;
            var buf = new StringWriter();
            try
            {
                Console.SetOut(buf);
                CutsceneDirector.Register(cutsceneId, c => SfxOnly(c, name));
                CutsceneDirector.Play(cutsceneId);
                PumpWorld(ctx, 10);
            }
            finally { Console.SetOut(prev); }
            return buf.ToString();
        }

        private static IEnumerator<Wait> SfxOnly(Cutscene c, string name)
        {
            yield return c.Sfx(name);
        }

        public string PlayBgm(string cutsceneId, string name)
        {
            CutsceneLog.ResetCounters();
            var (ctx, _) = NewWorld();
            CutsceneDirector.ClearRegistry();
            CutsceneDirector.Bind(ctx);

            var prev = Console.Out;
            var buf = new StringWriter();
            try
            {
                Console.SetOut(buf);
                CutsceneDirector.Register(cutsceneId, c => BgmOnly(c, name));
                CutsceneDirector.Play(cutsceneId);
                PumpWorld(ctx, 10);
            }
            finally { Console.SetOut(prev); }
            return buf.ToString();
        }

        private static IEnumerator<Wait> BgmOnly(Cutscene c, string name)
        {
            yield return c.Bgm(name, 0f);
        }

        private GameContext? _ctx;

        public (GameContext Ctx, Animator Animator, Entity Actor) NewActor()
        {
            var (ctx, scene) = NewWorld();
            _ctx = ctx;
            var actor = scene.CreateEntity("hero");
            var animator = actor.AddComponent<Animator>();
            scene.FlushPendingAdds();

            CutsceneDirector.ClearRegistry();
            CutsceneDirector.Bind(ctx);
            return (ctx, animator, actor);
        }

        public string PlayAnim(GameContext ctx, string cutsceneId, string clip, int frames)
        {
            CutsceneLog.ResetCounters();
            var prev = Console.Out;
            var buf = new StringWriter();
            try
            {
                Console.SetOut(buf);
                CutsceneDirector.Register(cutsceneId, c => AnimOnly(c, clip));
                CutsceneDirector.Play(cutsceneId);
                PumpWorld(ctx, frames);
            }
            finally { Console.SetOut(prev); }
            return buf.ToString();
        }

        private static void PumpWorld(GameContext ctx, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                ctx.Coroutines.Update(1f / 60f);
                ctx.Scene?.Update(1f / 60f);
            }
        }

        private static IEnumerator<Wait> AnimOnly(Cutscene c, string clip)
        {
            yield return c.Anim("hero", clip);
        }

        public void Dispose()
        {
            CutsceneDirector.Stop();
            for (int i = 0; i < 4 && CutsceneDirector.IsPlaying; i++)
                _ctx?.Coroutines.Update(1f / 60f);
            CutsceneDirector.ClearRegistry();
            FreezeState.ResetAll();
            AnimClipCache.Clear();
            AnimClipCache.ContentRoot = _prevRoot;
            _scope.Dispose();
            try { Directory.Delete(_root, true); } catch {  }
        }
    }
#endif

    private static (GameContext, Scene) NewWorld()
    {
        var scene = new Scene("CutsceneTest");
        var ctx = new GameContext { Scene = scene, Camera = new Camera(320, 180) };
        return (ctx, scene);
    }

    private static void Pump(GameContext ctx, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            ctx.Coroutines.Update(1f / 60f);
            ctx.Scene?.FlushPendingAdds();
        }
    }

    private static void Check(string label, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  ✓ {label}"); }
        else { _fail++; Console.WriteLine($"  ✗ {label}{(detail != null ? $"  ({detail})" : "")}"); }
    }
}
