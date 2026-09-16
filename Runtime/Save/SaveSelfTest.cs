using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PixelCore.Runtime.Save;

public static class SaveSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== Save self-test ===");
        _pass = _fail = 0;

        TestRoundTrip();
        TestAtomicWrite();
        TestCorruptFallback();
        TestMigrationScaffold();
        TestFlagLifetime();
        TestFlagExpiry();
        TestCaptureAndApply();
        TestCheckpointGuard();

        Console.WriteLine($"=== {_pass} passed, {_fail} failed ===");
    }

    private static void TestRoundTrip()
    {
        Console.WriteLine("--- round trip ---");
        using var t = new TempSaves();

        var save = Sample();
        var w = SaveFile.Write(SaveFile.AutoSlot, save);
        Check("write succeeded", w.Ok, w.Error);

        var r = SaveFile.Read(SaveFile.AutoSlot);
        Check("read succeeded", r.Ok, string.Join(" / ", r.Problems));
        Check("read from the primary (not a backup fallback)", r.Source == SaveSource.Primary && !r.AutoRecovered);

        var d = r.Data!;
        Check("checkpoint id", d.Checkpoint == "d1_after_dinner", d.Checkpoint);
        Check("room is a scene asset id", d.Room.SceneId == "b83f0d4e", d.Room.SceneId);
        Check("spawn point", d.Room.Spawn == "Spawn_Porch", d.Room.Spawn);
        Check("day", d.Time.Day == 3, d.Time.Day.ToString());
        Check("phase is the enum member name", d.Time.Phase == "Evening", d.Time.Phase);
        Check("player facing", d.Player.Facing == "Down", d.Player.Facing);
        Check("costume", d.Player.Costume == "raincoat", d.Player.Costume);

        Check("bool flag", d.Flags.Bools["house.lampOff"], "came back as false");
        Check("int flag", d.Flags.Ints["story.visit.examine.house"] == 2);
        Check("float flag", Math.Abs(d.Flags.Floats["affinity.elder"] - 0.75f) < 1e-6f);
        Check("string flag", d.Flags.Strings["choice.d1_answer"] == "apologise", d.Flags.Strings["choice.d1_answer"]);
        Check("total flag count", d.Flags.Count == 5, d.Flags.Count.ToString());

        Check("one expiry booking", d.Expiring.Count == 1, d.Expiring.Count.ToString());
        Check("the expiry entry records the moment of setting",
            d.Expiring[0].Key == "house.lampOff" && d.Expiring[0].Scope == "Phase" &&
            d.Expiring[0].Day == 3 && d.Expiring[0].Phase == "Evening");

        Check("play time", Math.Abs(d.Stats.PlaySeconds - 4212.5) < 1e-6);
        Check("achievement list", d.Stats.Achievements.Count == 1 && d.Stats.Achievements[0] == "first_night");

        string raw = File.ReadAllText(SaveFile.PathOf(SaveFile.AutoSlot));
        Check("non-ASCII keys stay readable in the file (diagnosable)", raw.Contains("house.lampOff"),
            "written as unicode escapes and unreadable by eye");
        Check("JSON keys are camelCase", raw.Contains("\"lastAppliedFix\""));
        Check("the derived count field is not written", !raw.Contains("\"count\""));

        var wrong = Sample();
        wrong.Room.SceneNameHint = "Wrong";
        SaveFile.Write(SaveFile.AutoSlot, wrong);
        var rw = SaveFile.Read(SaveFile.AutoSlot);
        Check("* a wrong hint still restores from sceneId", rw.Ok && rw.Data!.Room.SceneId == "b83f0d4e");

        var none = Sample();
        none.Room.SceneNameHint = "";
        SaveFile.Write(SaveFile.AutoSlot, none);
        var rn = SaveFile.Read(SaveFile.AutoSlot);
        Check("* a missing hint still restores from sceneId", rn.Ok && rn.Data!.Room.SceneId == "b83f0d4e");
    }

    private static void TestAtomicWrite()
    {
        Console.WriteLine("--- atomic write ---");
        using var t = new TempSaves();

        var first = Sample();
        first.Checkpoint = "first";
        SaveFile.Write(SaveFile.AutoSlot, first);

        Check("there is no backup after the first save", !File.Exists(SaveFile.OldPathOf(SaveFile.AutoSlot)));
        Check("no partial tmp is left behind", !File.Exists(SaveFile.TmpPathOf(SaveFile.AutoSlot)));

        var second = Sample();
        second.Checkpoint = "second";
        SaveFile.Write(SaveFile.AutoSlot, second);

        Check("the primary is the newest", SaveFile.Read(SaveFile.AutoSlot).Data!.Checkpoint == "second");
        Check("_old is the previous one", File.Exists(SaveFile.OldPathOf(SaveFile.AutoSlot)) &&
            File.ReadAllText(SaveFile.OldPathOf(SaveFile.AutoSlot)).Contains("first"));
        Check("no tmp is left after a second save either", !File.Exists(SaveFile.TmpPathOf(SaveFile.AutoSlot)));
    }

    private static void TestCorruptFallback()
    {
        Console.WriteLine("--- corruption fallback ---");

        {
            using var t = new TempSaves();
            var a = Sample(); a.Checkpoint = "older";
            SaveFile.Write(SaveFile.AutoSlot, a);
            var b = Sample(); b.Checkpoint = "newer";
            SaveFile.Write(SaveFile.AutoSlot, b);

            File.WriteAllText(SaveFile.PathOf(SaveFile.AutoSlot), "{ this is not JSON");

            var r = SaveFile.Read(SaveFile.AutoSlot);
            Check("it opens even with a corrupt primary", r.Ok, string.Join(" / ", r.Problems));
            Check("recovered from _old", r.Source == SaveSource.Old);
            Check("the recovered contents are the previous save", r.Data!.Checkpoint == "older", r.Data.Checkpoint);
            Check("* the recovery is reported, not passed over silently", r.AutoRecovered && r.Problems.Count > 0);
        }

        {
            using var t = new TempSaves();
            var a = Sample(); a.Checkpoint = "only in tmp";
            SaveFile.Write(SaveFile.AutoSlot, a);
            File.Move(SaveFile.PathOf(SaveFile.AutoSlot), SaveFile.TmpPathOf(SaveFile.AutoSlot));
            File.WriteAllText(SaveFile.PathOf(SaveFile.AutoSlot), "corrupt");
            File.WriteAllText(SaveFile.OldPathOf(SaveFile.AutoSlot), "also corrupt");

            var r = SaveFile.Read(SaveFile.AutoSlot);
            Check("it falls back as far as tmp and opens", r.Ok && r.Source == SaveSource.Tmp, string.Join(" / ", r.Problems));
            Check("a tmp recovery is reported too", r.AutoRecovered);
        }

        {
            using var t = new TempSaves();
            SaveFile.Write(SaveFile.AutoSlot, Sample());
            SaveFile.Write(SaveFile.AutoSlot, Sample());
            File.WriteAllText(SaveFile.PathOf(SaveFile.AutoSlot), "corrupt");
            File.WriteAllText(SaveFile.OldPathOf(SaveFile.AutoSlot), "corrupt");

            var r = SaveFile.Read(SaveFile.AutoSlot);
            Check("* all three corrupt surfaces as a failure", !r.Ok);
            Check("* absent is distinguished from unreadable", !r.NotFound);
            Check("the problem list is not empty", r.Problems.Count >= 2, r.Problems.Count.ToString());
        }

        {
            using var t = new TempSaves();
            var r = SaveFile.Read(SaveFile.AutoSlot);
            Check("first run: no save", !r.Ok && r.NotFound);
            Check("a first run is not reported as a problem", r.Problems.Count == 0, string.Join(" / ", r.Problems));
            Check("Exists = false", !SaveFile.Exists(SaveFile.AutoSlot));
        }
    }

    private static void TestMigrationScaffold()
    {
        Console.WriteLine("--- version cursor ---");
        using var t = new TempSaves();

        Check("* MaxFix equals the highest number declared in the enum",
            SaveFixes.MaxFix == SaveFixes.DeclaredMax(),
            $"MaxFix={SaveFixes.MaxFix} vs enum={SaveFixes.DeclaredMax()}");

        var old = Sample();
        old.LastAppliedFix = 0;
        var notes = new List<string>();
        SaveFixes.ApplyAll(old, notes);
        Check("with no fixes the cursor is unchanged", old.LastAppliedFix == SaveFixes.MaxFix && notes.Count == 0);

        var future = Sample();
        future.LastAppliedFix = 99;
        var fnotes = new List<string>();
        SaveFixes.ApplyAll(future, fnotes);
        Check("* a future cursor is surfaced and left untouched",
            future.LastAppliedFix == 99 && fnotes.Count == 1, string.Join(" / ", fnotes));

        var newer = Sample();
        newer.Version = SaveSchema.CurrentVersion + 1;
        SaveFile.Write(SaveFile.AutoSlot, newer);
        var r = SaveFile.Read(SaveFile.AutoSlot);
        Check("* a future-version save cannot be opened", !r.Ok && r.Problems.Count > 0);
        Check("* a future version is not 'absent' either", !r.NotFound);

        SaveFile.Write(SaveFile.AutoSlot, Sample());
        var ok = SaveFile.Read(SaveFile.AutoSlot);
        Check("a normal save opens with the cursor at current", ok.Ok && ok.Data!.LastAppliedFix == SaveFixes.MaxFix);
    }

    private static void TestFlagLifetime()
    {
        Console.WriteLine("--- flag lifetime declarations ---");
        FlagRegistry.ClearForTest();

        var phaseKey = new FlagKey("test.phaseFlag", FlagLifetime.Phase);
        var dayKey = new FlagKey("test.dayFlag", FlagLifetime.Day);
        var foreverKey = new FlagKey("test.foreverFlag");

        Check("constructing a FlagKey registers it", FlagRegistry.DeclaredCount == 3, FlagRegistry.DeclaredCount.ToString());
        Check("the lifetime is recorded (Phase)", FlagRegistry.LifetimeOf(phaseKey) == FlagLifetime.Phase);
        Check("the lifetime is recorded (Day)", FlagRegistry.LifetimeOf(dayKey) == FlagLifetime.Day);
        Check("a declaration with no argument is permanent", FlagRegistry.LifetimeOf(foreverKey) == FlagLifetime.Permanent);

        Check("* an undeclared key is permanent (the safe default)",
            FlagRegistry.LifetimeOf("nobody.declaredThis") == FlagLifetime.Permanent);
        Check("declared and undeclared can be told apart",
            FlagRegistry.IsDeclared(phaseKey) && !FlagRegistry.IsDeclared("nobody.declaredThis"));

        Check("implicit string conversion keeps usage identical to a literal",
            (string)phaseKey == "test.phaseFlag");

        _ = new FlagKey("test.phaseFlag", FlagLifetime.Day);
        Check("a duplicate declaration lets the later one win, with a warning",
            FlagRegistry.LifetimeOf("test.phaseFlag") == FlagLifetime.Day);
    }

    private static void TestFlagExpiry()
    {
        Console.WriteLine("--- expiry queue ---");
        FlagRegistry.ClearForTest();

        var phaseKey = new FlagKey("test.phase", FlagLifetime.Phase);
        var dayKey = new FlagKey("test.day", FlagLifetime.Day);
        var foreverKey = new FlagKey("test.forever");

        var bb = new Core.Blackboard();
        var expiry = new FlagExpiry();
        int day = 1; string phase = "Morning";
        expiry.Bind(bb, () => (day, phase));

        Check("with nothing set, the queue is empty", expiry.PendingCount == 0);

        bb.SetBool(phaseKey, true);
        bb.SetBool(dayKey, true);
        bb.SetBool(foreverKey, true);
        Check("only keys with a lifetime enter the queue", expiry.PendingCount == 2, expiry.PendingCount.ToString());

        var same = expiry.ExpireFor(day, phase);
        Check("* same day and phase: nothing expires", same.Count == 0, string.Join(",", same));
        Check("same phase: the values are unchanged", bb.GetBool(phaseKey) && bb.GetBool(dayKey));

        phase = "Afternoon";
        var afterPhase = expiry.ExpireFor(day, phase);
        Check("* the phase rolled over: Phase lifetimes expire", afterPhase.Count == 1 && afterPhase[0] == "test.phase",
            string.Join(",", afterPhase));
        Check("an expired flag disappears from the blackboard", !bb.Has(phaseKey));
        Check("a Day lifetime survives within the same day", bb.GetBool(dayKey));
        Check("permanent flags survive, of course", bb.GetBool(foreverKey));
        Check("an expired entry leaves the queue too", expiry.PendingCount == 1, expiry.PendingCount.ToString());

        day = 2; phase = "Morning";
        var afterDay = expiry.ExpireFor(day, phase);
        Check("* the day rolled over: Day lifetimes expire", afterDay.Count == 1 && afterDay[0] == "test.day");
        Check("the queue is empty", expiry.PendingCount == 0);
        Check("a permanent flag survives a day change", bb.GetBool(foreverKey));

        bb.SetBool(phaseKey, true);
        Check("setting it again re-enqueues it", expiry.PendingCount == 1);
        var again = expiry.ExpireFor(day, phase);
        Check("just after re-enqueuing, nothing expires in the same phase", again.Count == 0);

        bb.Remove(phaseKey);
        Check("clearing the value clears the booking", expiry.PendingCount == 0);

        FlagRegistry.ClearForTest();
        var keys = new List<FlagKey>();
        for (int i = 0; i < 8; i++) keys.Add(new FlagKey($"test.bulk{i}", FlagLifetime.Phase));
        foreach (var k in keys) bb.SetBool(k, true);
        Check("bulk registration", expiry.PendingCount == 8, expiry.PendingCount.ToString());
        phase = "Evening";
        var bulk = expiry.ExpireFor(day, phase);
        Check("* expiring many at once does not throw (the re-entry latch)", bulk.Count == 8, bulk.Count.ToString());
        Check("the queue is empty after a bulk expiry", expiry.PendingCount == 0);

        TestExpiryPersistence();
    }

    private static void TestExpiryPersistence()
    {
        Console.WriteLine("--- saving the expiry queue ---");
        FlagRegistry.ClearForTest();

        var phaseKey = new FlagKey("test.saved.phase", FlagLifetime.Phase);
        var dayKey = new FlagKey("test.saved.day", FlagLifetime.Day);

        var bb = new Core.Blackboard();
        var expiry = new FlagExpiry();
        int day = 2; string phase = "Evening";
        expiry.Bind(bb, () => (day, phase));

        bb.SetBool(phaseKey, true);
        bb.SetBool(dayKey, true);

        var captured = expiry.Capture();
        Check("two queue entries captured", captured.Count == 2, captured.Count.ToString());
        Check("capture order is stable, so the file diffs cleanly",
            string.CompareOrdinal(captured[0].Key, captured[1].Key) < 0);
        Check("* the recorded day and phase are the moment of setting",
            captured[0].Day == 2 && captured[0].Phase == "Evening");

        var bb2 = new Core.Blackboard();
        var expiry2 = new FlagExpiry();
        int day2 = 2; string phase2 = "Evening";
        expiry2.Bind(bb2, () => (day2, phase2));

        bb2.LoadFrom(new Dictionary<string, object> { [phaseKey] = true, [dayKey] = true });
        expiry2.LoadFrom(captured);

        Check("two queue entries restored", expiry2.PendingCount == 2, expiry2.PendingCount.ToString());

        var none = expiry2.ExpireFor(day2, phase2);
        Check("* right after loading, nothing expires in the same phase", none.Count == 0, string.Join(",", none));

        phase2 = "Morning"; day2 = 3;
        var expired = expiry2.ExpireFor(day2, phase2);
        Check("* a restored booking still expires once time passes", expired.Count == 2, string.Join(",", expired));
        Check("the blackboard is empty after expiry", !bb2.Has(phaseKey) && !bb2.Has(dayKey));

        var expiry3 = new FlagExpiry();
        expiry3.Bind(new Core.Blackboard(), () => (1, "Morning"));
        var problems = new List<string>();
        expiry3.LoadFrom(new[]
        {
            new ExpiringFlagSave { Key = "test.brokenLifetime", Scope = "Fortnight", Day = 1, Phase = "Morning" },
        }, problems);
        Check("* an unknown lifetime is surfaced", problems.Count == 1, string.Join(" / ", problems));
        Check("an unknown entry is discarded rather than disguised as permanent", expiry3.PendingCount == 0);
    }

    private static void TestCaptureAndApply()
    {
        Console.WriteLine("--- capture and apply ---");
        using var t = new TempSaves();
        FlagRegistry.ClearForTest();

        var ctx = new Core.GameContext();
        var save = ctx.Save;
        save.Bind(ctx);
        ctx.FlagExpiry.Bind(ctx.Blackboard, () => (1, "Morning"));

        SaveData? applied = null;
        save.CaptureGameState = d =>
        {
            d.Room = new RoomSave { SceneId = "b83f0d4e", Spawn = "Spawn_House", SceneNameHint = "House" };
            d.Time = new TimeSave { Day = 2, Phase = "Afternoon" };
            d.Player = new PlayerSave { Facing = "Left", Costume = "raincoat" };
        };
        save.ApplyGameState = d => { applied = d; return true; };

        ctx.Blackboard.SetBool("check.flag", true);
        ctx.Blackboard.SetInt("check.count", 7);
        ctx.Blackboard.SetFloat("check.ratio", 0.5f);
        ctx.Blackboard.SetString("check.choice", "left");
        save.Update(12.5f);

        var captured = save.Capture("check.checkpoint");
        Check("the blackboard splits into four dictionaries by type",
            captured.Flags.Bools.Count == 1 && captured.Flags.Ints.Count == 1 &&
            captured.Flags.Floats.Count == 1 && captured.Flags.Strings.Count == 1);
        Check("the game hook fills in room, time and player",
            captured.Room.SceneId == "b83f0d4e" && captured.Time.Day == 2 && captured.Player.Facing == "Left");
        Check("play time is recorded", Math.Abs(captured.Stats.PlaySeconds - 12.5) < 0.001);
        Check("the fix cursor is stamped from the current build", captured.LastAppliedFix == SaveFixes.MaxFix);

        ctx.Blackboard.Set("check.junk", new object());
        var withJunk = save.Capture("check.withJunk");
        Check("* unstorable types are dropped, with a warning",
            withJunk.Flags.Count == 4 && !withJunk.Flags.Strings.ContainsKey("check.junk"));
        ctx.Blackboard.Remove("check.junk");

        Check("the checkpoint saved", save.Checkpoint("check.checkpoint"));

        var ctx2 = new Core.GameContext();
        var save2 = ctx2.Save;
        save2.Bind(ctx2);
        ctx2.FlagExpiry.Bind(ctx2.Blackboard, () => (1, "Morning"));
        SaveData? seen = null;
        save2.ApplyGameState = d => { seen = d; return true; };

        var load = save2.LoadLatest();
        Check("the load succeeded", load.Ok, string.Join(" / ", load.Problems));
        Check("* the whole blackboard comes back",
            ctx2.Blackboard.GetBool("check.flag") && ctx2.Blackboard.GetInt("check.count") == 7 &&
            Math.Abs(ctx2.Blackboard.GetFloat("check.ratio") - 0.5f) < 1e-6f &&
            ctx2.Blackboard.GetString("check.choice") == "left");
        Check("play time carries over", Math.Abs(save2.PlaySeconds - 12.5) < 0.001);
        Check("the checkpoint id remains as a label", save2.LastCheckpoint == "check.checkpoint");
        Check("* entry goes through one game hook, with no per-checkpoint branch",
            seen != null && seen.Time.Day == 2 && seen.Time.Phase == "Afternoon");
        Check("the entry hook receives the room id unchanged", seen!.Room.SceneId == "b83f0d4e");

        var ctx3 = new Core.GameContext();
        ctx3.Save.Bind(ctx3);
        ctx3.FlagExpiry.Bind(ctx3.Blackboard, () => (1, "Morning"));

        ctx3.Blackboard.SetBool("session.inProgress", true);
        ctx3.Blackboard.SetInt("session.count", 3);
        var sessionKey = new FlagKey("session.todayOnly", FlagLifetime.Day);
        ctx3.Blackboard.SetBool(sessionKey, true);
        ctx3.Save.Update(99f);
        Check("premise: there really is session state to roll back (otherwise everything below is vacuous)",
            ctx3.Blackboard.GetBool("session.inProgress") && ctx3.FlagExpiry.Capture().Count == 1
            && Math.Abs(ctx3.Save.PlaySeconds - 99.0) < 0.001);

        ctx3.Save.ApplyGameState = _ => false;
        var failed = ctx3.Save.LoadLatest();
        Check("* a failed entry is a failed load", !failed.Ok && failed.Problems.Count > 0);
        Check("** a failed entry leaves the flags untouched, or a hybrid session hardens at the next checkpoint",
            ctx3.Blackboard.GetBool("session.inProgress") && ctx3.Blackboard.GetInt("session.count") == 3,
            $"inProgress={ctx3.Blackboard.GetBool("session.inProgress")} count={ctx3.Blackboard.GetInt("session.count")}");
        Check("** nor do the save's flags leak in (rolling back only one side is worse)",
            !ctx3.Blackboard.GetBool("check.flag"));
        Check("* the expiry bookings are restored too",
            ctx3.FlagExpiry.Capture().Count == 1 && ctx3.FlagExpiry.Capture()[0].Key == "session.todayOnly");
        Check("* the totals are restored too (play time)", Math.Abs(ctx3.Save.PlaySeconds - 99.0) < 0.001,
            ctx3.Save.PlaySeconds.ToString("0.###"));
        Check("* the checkpoint label is restored too, so a failed load leaves no name",
            ctx3.Save.LastCheckpoint == "" && failed.Checkpoint == "",
            $"'{ctx3.Save.LastCheckpoint}' / '{failed.Checkpoint}'");

        var ctx4 = new Core.GameContext();
        ctx4.Save.Bind(ctx4);
        ctx4.FlagExpiry.Bind(ctx4.Blackboard, () => (1, "Morning"));
        ctx4.Blackboard.SetBool("session.inProgress", true);
        var unwired = ctx4.Save.LoadLatest();
        Check("an unwired entry hook is surfaced too", !unwired.Ok && unwired.Problems.Count > 0);
        Check("* the unwired-hook path leaves the session alone too (there are two failure paths)",
            ctx4.Blackboard.GetBool("session.inProgress") && !ctx4.Blackboard.GetBool("check.flag"));

        var conflict = Sample();
        conflict.Flags.Bools["check.duplicate"] = true;
        conflict.Flags.Strings["check.duplicate"] = "aString";
        SaveFile.Write(SaveFile.AutoSlot, conflict);

        var ctx5 = new Core.GameContext();
        ctx5.Save.Bind(ctx5);
        ctx5.FlagExpiry.Bind(ctx5.Blackboard, () => (1, "Morning"));
        ctx5.Save.ApplyGameState = _ => true;
        var dup = ctx5.Save.LoadLatest();
        Check("* duplicate key: bool wins (a deterministic order)", ctx5.Blackboard.GetBool("check.duplicate"));
        Check("* a duplicate key is surfaced",
            dup.Problems.Exists(p => p.Contains("check.duplicate")), string.Join(" / ", dup.Problems));

        ctx5.Save.ResetForNewGame();
        Check("new-game reset: blackboard and totals are zero", ctx5.Blackboard.All.Count == 0 &&
            ctx5.Save.PlaySeconds == 0 && ctx5.Save.LastCheckpoint == "");
    }

    private static void TestCheckpointGuard()
    {
        Console.WriteLine("--- checkpoint guard ---");
        using var t = new TempSaves();

        var ctx = new Core.GameContext();
        var save = ctx.Save;
        save.Bind(ctx);
        ctx.FlagExpiry.Bind(ctx.Blackboard, () => (1, "Morning"));
        save.CaptureGameState = _ => { };
        save.ApplyGameState = _ => true;

        int okCount = 0, refusedCount = 0;
        save.Saved += n => { if (n.Ok) okCount++; else refusedCount++; };

        Check("normally a checkpoint can be taken", save.CanCheckpoint && save.BusyReason() == null);
        Check("a normal checkpoint succeeds", save.Checkpoint("safe.spot"));
        Check("a save notification arrives (the display hook)", okCount == 1);

        Cutscenes.CutsceneDirector.Bind(ctx);
        Cutscenes.CutsceneDirector.ClearRegistry();
        Cutscenes.CutsceneDirector.Register("check.saveGuard", GuardScene);
        Cutscenes.CutsceneDirector.Play("check.saveGuard");

        Check("a cutscene is genuinely playing", Cutscenes.CutsceneDirector.IsPlaying);
        Check("* a checkpoint is refused during a cutscene", !save.Checkpoint("cutscene.middle"));
        Check("the refusal reason names the cutscene", save.BusyReason()?.Contains("cutscene") == true, save.BusyReason());
        Check("the refusal is notified too (it is not silent)", refusedCount == 1);

        Cutscenes.CutsceneDirector.Abort();
        Check("once the cutscene ends it can be taken again", save.CanCheckpoint);

        var dir = Path.Combine(Path.GetTempPath(), "pixelcore_save_story_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "guard.story"),
            "# check.guard [speaker:hero]\n\nFirst line.\nSecond line.\n", new UTF8Encoding(false));

        var prevLibrary = Story.StoryLibrary.Current;
        Story.StoryLibrary.UseLibrary(Story.StoryLibrary.Load(dir));
        Story.DialogueRunner.Bind(ctx.Blackboard);
        Story.DialogueRunner.Stop();
        UI.DialogueBox.Close();

        Check("the dialogue block started", Story.DialogueRunner.Play("check.guard"));
        Check("dialogue is genuinely running", Story.DialogueRunner.Active);
        Check("* a checkpoint is refused during dialogue", !save.Checkpoint("dialogue.middle"));
        Check("the refusal reason names the dialogue", save.BusyReason()?.Contains("dialogue") == true, save.BusyReason());

        Story.DialogueRunner.Stop();
        UI.DialogueBox.Close();
        Story.StoryLibrary.UseLibrary(prevLibrary);
        try { Directory.Delete(dir, true); } catch {  }

        Check("once dialogue ends it can be taken again", save.CanCheckpoint);

        save.GameBusyReason = () => "a room transition is in progress";
        Check("* a game-side reason refuses too", !save.Checkpoint("transition.middle"));
        save.GameBusyReason = () => null;
        Check("it passes once the reason clears", save.Checkpoint("safe.again"));

        var read = SaveFile.Read(SaveFile.AutoSlot);
        Check("* a refusal does not damage the save file",
            read.Ok && read.Data!.Checkpoint == "safe.again", read.Data?.Checkpoint);
    }

    private static IEnumerator<Core.Wait> GuardScene(Cutscenes.Cutscene c)
    {
        yield return Core.Wait.Seconds(999f);
    }

    private static SaveData Sample() => new()
    {
        Checkpoint = "d1_after_dinner",
        SavedAtUtc = "2024-05-01T09:12:33Z",
        Room = new RoomSave { SceneId = "b83f0d4e", Spawn = "Spawn_Porch", SceneNameHint = "House" },
        Time = new TimeSave { Day = 3, Phase = "Evening" },
        Player = new PlayerSave { Facing = "Down", Costume = "raincoat" },
        Flags = new FlagsSave
        {
            Bools = { ["house.lampOff"] = true, ["cutscene.done.test.house"] = true },
            Ints = { ["story.visit.examine.house"] = 2 },
            Floats = { ["affinity.elder"] = 0.75f },
            Strings = { ["choice.d1_answer"] = "apologise" },
        },
        Expiring =
        {
            new ExpiringFlagSave { Key = "house.lampOff", Scope = "Phase", Day = 3, Phase = "Evening" },
        },
        Stats = new StatsSave { PlaySeconds = 4212.5, Achievements = { "first_night" } },
    };

    private sealed class TempSaves : IDisposable
    {
        private readonly string _dir;
        private readonly IDisposable _scope;

        public TempSaves()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pixelcore_save_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_dir);
            _scope = SaveFile.UseTemporary(_dir);
        }

        public void Dispose()
        {
            _scope.Dispose();
            try { Directory.Delete(_dir, true); } catch {  }
        }
    }

    private static void Check(string label, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  ✔ {label}"); }
        else { _fail++; Console.WriteLine($"  ✘ {label}" + (detail != null ? $"  ← {detail}" : "")); }
    }
}
