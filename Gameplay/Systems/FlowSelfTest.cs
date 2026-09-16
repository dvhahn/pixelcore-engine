#if DEBUG
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.IO;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Cutscenes;
using PixelCore.Runtime.Rendering;
using PixelCore.Runtime.Story;
using PixelCore.Runtime.Systems;
using PixelCore.Runtime.UI;

namespace PixelCore.Gameplay.Systems;

public static class FlowSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== day entry global reset self-test ===");
        _pass = _fail = 0;

        TestStartDayResetsRuntimeStatics();
        TestRoomIdentificationBarks();
        TestMissingRoomLatch();
        TestEventTables();
        TestArrivalFacing();
        TestStartDayRollsBackTime();
        TestPhaseWindowsAndTags();
        TestNoIndependentClock();

        Console.WriteLine($"=== Flow: {_pass} passed, {_fail} failed ===");
    }

    private static void TestStartDayResetsRuntimeStatics()
    {
        var scene = new Scene("FlowTest");
        var ctx = new GameContext { Scene = scene, Camera = new Camera(320, 180) };
        var camera = ctx.Camera!;

        var prevLoader = RoomFlow.LoadRoomImpl;
        var prevCostume = GameFlow.Costume;
        var prevLibrary = StoryLibrary.Current;
        var storyDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "pixelcore_flow_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            GameFlow.Bind(ctx, camera);
            RoomFlow.LoadRoomImpl = _ => true;

            CinematicBars.Set(1f);
            ScreenFader.Set(1f);
            DialogueBox.FastForward = true;
            InteractionPrompt.Enabled = false;
            PixelCore.Runtime.Core.Rumble.SuppressDevice = true;
            PixelCore.Runtime.Core.Rumble.Play(1f, 5f);
            PixelCore.Runtime.Core.Rumble.Update(0f);

            StoryLibrary.UseLibrary(BuildPollutedLibrary(storyDir));
            DialogueRunner.Bind(ctx.Blackboard);
            DialogueRunner.Play("test.dirty");

            DialogueRunner.Play("test.dirty.choice");
            DialogueBox.Close();
            DialogueRunner.Update(1f / 60f);

            CutsceneDirector.ClearRegistry();
            CutsceneDirector.Bind(ctx);
            CutsceneDirector.Register("test.longScene", _ => Forever());
            CutsceneDirector.Play("test.longScene");
            ctx.Coroutines.Update(1f / 60f);
            CutsceneDirector.RequestSkip();
            GameFlow.Costume = "pajamas";
            Runtime.Components.Interactor.GlobalLock = true;

            TitleScreen.Open("test", "dirty", new[] { new TitleItem("item", null) });

            Check("premise: the contamination actually took (letterbox)", CinematicBars.Amount == 1f);
            Check("premise: the contamination actually took (the pad is vibrating)",
                  PixelCore.Runtime.Core.Rumble.IsActive);
            Check("premise: the contamination actually took (dialogue box)", DialogueBox.Active);
            Check("premise: the contamination actually took (choices are up)", DialogueBox.ChoiceActive);
            Check("premise: the contamination actually took (the runner is holding a block)",
                  DialogueRunner.CurrentBlockId == "test.dirty.choice", DialogueRunner.CurrentBlockId);
            Check("premise: the contamination actually took (a cutscene is playing and skipping)",
                  CutsceneDirector.IsPlaying && CutsceneDirector.Skipping);
            Check("premise: the contamination actually took (time scale)",
                  ctx.Time.TimeScale != 1f, ctx.Time.TimeScale.ToString());
            Check("premise: the contamination actually took (the title is up)", TitleScreen.Active);

            PixelCore.Runtime.Rendering.FxStack.SetWeather(
                new PixelCore.Runtime.Rendering.FxLayer { FogOpacity = 0.25f });
            PixelCore.Runtime.Rendering.FxStack.Push(
                new PixelCore.Runtime.Rendering.FxLayer { ChromAb = 0.5f });

            var latchErr = new System.IO.StringWriter();
            var prevErr = Console.Error;
            Console.SetError(latchErr);
            Runtime.Serialization.PostData.WarnMovedKeys("{\"chromAb\":0.2}", "test.oldKey");
            Console.SetError(prevErr);
            Check("premise: the contamination actually took (screen distortion stack)",
                  !PixelCore.Runtime.Rendering.FxStack.Current.IsNeutral
                  && PixelCore.Runtime.Rendering.FxStack.HasWeather);
            Check("premise: the contamination actually took (the old key latch barked once)",
                  latchErr.ToString().Contains("chromAb"), latchErr.ToString().Trim());

            bool ok = GameFlow.StartDay(1, DayPhase.Morning);
            Check("premise: StartDay succeeded (if it failed, everything below is vacuous)", ok);
            if (!ok) return;

            Check("* the letterbox is cleared - otherwise the day starts with black bars down",
                  CinematicBars.Amount == 0f, CinematicBars.Amount.ToString());
            Check("* pad rumble stops - rewound while on, only restarting the app clears it",
                  !PixelCore.Runtime.Core.Rumble.IsActive
                  && PixelCore.Runtime.Core.Rumble.LastApplied == 0f,
                  $"active={PixelCore.Runtime.Core.Rumble.IsActive} applied={PixelCore.Runtime.Core.Rumble.LastApplied}");
            Check("* the screen fade is cleared - a save taken during a blackout would be stuck on black",
                  ScreenFader.Alpha == 0f, ScreenFader.Alpha.ToString());
            Check("* the screen distortion stack is emptied - a day must not start with aberration applied",
                  PixelCore.Runtime.Rendering.FxStack.Current.IsNeutral
                  && !PixelCore.Runtime.Rendering.FxStack.HasWeather
                  && PixelCore.Runtime.Rendering.FxStack.PushedCount == 0,
                  $"chromAb={PixelCore.Runtime.Rendering.FxStack.Current.ChromAb} " +
                  $"fog={PixelCore.Runtime.Rendering.FxStack.Current.FogOpacity} " +
                  $"layers={PixelCore.Runtime.Rendering.FxStack.PushedCount}");

            var after = new System.IO.StringWriter();
            var prevErr2 = Console.Error;
            Console.SetError(after);
            Runtime.Serialization.PostData.WarnMovedKeys("{\"chromAb\":0.2}", "test.oldKey");
            Console.SetError(prevErr2);
            Check("* the post warning latch is released - the same file has to bark again for the author to know it is fixed",
                  after.ToString().Contains("chromAb"), $"[{after.ToString().Trim()}]");
            Check("* the dialogue box closes", !DialogueBox.Active);
            Check("* the choice list disappears - continuing mid-choice does not carry it into the new day",
                  !DialogueBox.ChoiceActive);
            Check("* the chosen value does not survive either - it would auto-pick the next block's first choice",
                  !DialogueBox.TryTakeChoice(out _));
            Check("* fast-forward turns off (a session toggle - Close only restores AutoAdvance)",
                  !DialogueBox.FastForward);
            Check("* auto-advance turns off", !DialogueBox.AutoAdvance);
            Check("* the dialogue position is cleared - with the box closed but the runner still holding a line number, "
                + "the next Update pushes the following line into a closed box",
                  DialogueRunner.CurrentBlockId == "", DialogueRunner.CurrentBlockId);
            Check("* the interaction prompt is on - left off, nothing can be examined",
                  InteractionPrompt.Enabled);
            Check("* the costume returns to its default - a new game does not inherit the last session's clothes "
                + "(a save overwrites it AFTER StartDay - that is the delta)",
                  GameFlow.Costume == "default", GameFlow.Costume);
            Check("* the input lock is released (the existing ReleaseLocks guarantee, checked alongside)",
                  !Runtime.Components.Interactor.GlobalLock);
            Check("* the cutscene lock is released", !FreezeState.IsFrozen(FreezeFlags.PlayerInput));
            Check("no cutscene is running", !CutsceneDirector.IsPlaying);
            Check("* the skip state is cleared - left on, the new day runs at x16",
                  !CutsceneDirector.Skipping);
            Check("* the time scale is restored (the value the skip swapped in does not carry over)",
                  ctx.Time.TimeScale == 1f, ctx.Time.TimeScale.ToString());
            Check("* the title closes - start a day with it up and the menu stays over the world, "
                + "and with the gameplay gate closed it looks like the game is frozen",
                  !TitleScreen.Active);
        }
        finally
        {
            StoryLibrary.UseLibrary(prevLibrary);
            DialogueRunner.Stop();
            try { System.IO.Directory.Delete(storyDir, true); } catch {  }
            CutsceneDirector.Abort();
            ctx.Time.TimeScale = 1f;
            RoomFlow.LoadRoomImpl = prevLoader;
            GameFlow.Costume = prevCostume;
            DialogueBox.Close();
            CinematicBars.Set(0f);
            ScreenFader.Set(0f);
            InteractionPrompt.Enabled = true;
            FreezeState.ResetAll();
        }
    }

    private static void TestMissingRoomLatch()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "pixelcore_latch_" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(root);

        var scene = new Scene("LatchA");
        var ctx = new GameContext { Scene = scene, Camera = new Camera(320, 180) };
        var prevLoader = RoomFlow.LoadRoomImpl;
        try
        {
            using var _ = Runtime.Assets.AssetRegistry.UseTemporary(System.IO.Path.Combine(root, "assets.json"));
            GameFlow.Bind(ctx, ctx.Camera!);
            RoomFlow.LoadRoomImpl = _ => true;

            Check("premise: the first call barks (if it does not, everything below is vacuous)",
                  CaptureStderr(() => GameFlow.CurrentRoom()).Contains("[Flow] ✘"));

            var repeat = CaptureStderr(() => { for (int i = 0; i < 60; i++) GameFlow.CurrentRoom(); });
            Check($"* the same name barks only once ({CountBarks(repeat)} lines after asking 60 times)",
                  CountBarks(repeat) == 0, repeat);

            scene.Name = "LatchB";
            var other = CaptureStderr(() => GameFlow.CurrentRoom());
            Check("* control: a different name barks again (the latch is per name)",
                  CountBarks(other) == 1, other);
            Check("   the wording names that name", other.Contains("LatchB"), other);

            Runtime.Assets.AssetRegistry.Instance.Register(Rooms.Village.SceneId, "Scenes/Village.scene");
            GameFlow.StartDay(1, DayPhase.Morning);
            scene.Name = "LatchA";
            var afterReset = CaptureStderr(() => GameFlow.CurrentRoom());
            Check("* after StartDay the same name barks again (the latch is cleared)",
                  CountBarks(afterReset) == 1, afterReset);

            scene.Name = "";
            Check("* an empty name gives 0 lines (the title screen is in this state)",
                  CaptureStderr(() => { for (int i = 0; i < 60; i++) GameFlow.CurrentRoom(); }).Length == 0);
        }
        finally
        {
            RoomFlow.LoadRoomImpl = prevLoader;
            try { System.IO.Directory.Delete(root, true); } catch { }
        }
    }

    private static int CountBarks(string captured)
    {
        int n = 0, i = 0;
        while ((i = captured.IndexOf("[Flow] ✘", i, StringComparison.Ordinal)) >= 0) { n++; i += 8; }
        return n;
    }

    private static void TestRoomIdentificationBarks()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "pixelcore_room_" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(root);

        var scene = new Scene("NoSuchRoom");
        var ctx = new GameContext { Scene = scene, Camera = new Camera(320, 180) };
        var prevLoader = RoomFlow.LoadRoomImpl;
        try
        {
            using var _ = Runtime.Assets.AssetRegistry.UseTemporary(System.IO.Path.Combine(root, "assets.json"));
            GameFlow.Bind(ctx, ctx.Camera!);
            RoomFlow.LoadRoomImpl = _ => true;

            var bark = CaptureStderr(() => GameFlow.CurrentRoom());
            Check("* zero candidates barks too - it used to be completely silent", bark.Contains("[Flow] ✘"), bark);
            Check("* the zero-candidate wording differs from two-or-more (the fix differs)",
                  bark.Contains("no scene named") && !bark.Contains("matches"), bark);
            Check("an empty SceneId still goes out (the symptom stands - saving is not blocked)",
                  GameFlow.CurrentRoom().SceneId == "");

            Runtime.Assets.AssetRegistry.Instance.GetOrCreateId("Scenes/A/Dup.scene");
            Runtime.Assets.AssetRegistry.Instance.GetOrCreateId("Scenes/B/Dup.scene");
            scene.Name = "Dup";
            bark = CaptureStderr(() => GameFlow.CurrentRoom());
            Check("the two-or-more wording is unchanged", bark.Contains("matches") && bark.Contains("2 files"), bark);

            var id = Runtime.Assets.AssetRegistry.Instance.GetOrCreateId("Scenes/GoodRoom.scene");
            scene.Name = "GoodRoom";
            bark = CaptureStderr(() => GameFlow.CurrentRoom());
            Check("when it resolves, it says nothing", bark.Length == 0, bark);
            Check("premise: the normal path really does find the id", GameFlow.CurrentRoom().SceneId == id);

            Runtime.Assets.AssetRegistry.Instance.Register(Rooms.Village.SceneId, "Scenes/Village.scene");
            var data = new Runtime.Save.SaveData
            {
                Room = new Runtime.Save.RoomSave { SceneId = "", Spawn = "", SceneNameHint = "NoSuchRoom" },
                Time = new Runtime.Save.TimeSave { Day = 1, Phase = "Morning" },
            };
            bool applied = false;
            bark = CaptureStderr(() => applied = ctx.Save.ApplyGameState!(data));
            Check("* a save stored with no room barks on load", bark.Contains("stored with no room"), bark);
            Check("* it names the room from the hint (SceneNameHint's documented use)",
                  bark.Contains("NoSuchRoom"), bark);
            Check("* it still enters - refusing would turn an authoring mistake into lost progress", applied);

            data.Room.SceneId = id;
            bark = CaptureStderr(() => ctx.Save.ApplyGameState!(data));
            Check("with a room present it says nothing", !bark.Contains("no room"), bark);
        }
        finally
        {
            RoomFlow.LoadRoomImpl = prevLoader;
            try { System.IO.Directory.Delete(root, true); } catch {  }
        }
    }

    private static void TestStartDayRollsBackTime()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "pixelcore_day_" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(root);

        var scene = new Scene("TimeRewind");
        var ctx = new GameContext { Scene = scene, Camera = new Camera(320, 180) };
        var prevLoader = RoomFlow.LoadRoomImpl;
        try
        {
            using var _ = Runtime.Assets.AssetRegistry.UseTemporary(System.IO.Path.Combine(root, "assets.json"));
            GameFlow.Bind(ctx, ctx.Camera!);

            GameFlow.DebugSetTime(2, DayPhase.Evening);

            bool ok = GameFlow.StartDay(99, DayPhase.Afternoon);
            Check("(a) premise: with no start room it fails", !ok);
            Check("(a) * on failure the date does not change",
                  GameFlow.Day == 2 && GameFlow.Phase == DayPhase.Evening,
                  $"day {GameFlow.Day} {GameFlow.Phase}");

            RoomFlow.LoadRoomImpl = _ => true;
            ok = GameFlow.StartDay(5, DayPhase.Morning, "noSuchId", "");
            Check("(b) premise: a room not in the registry fails", !ok);
            Check("(b) * the date does not change here either",
                  GameFlow.Day == 2 && GameFlow.Phase == DayPhase.Evening,
                  $"day {GameFlow.Day} {GameFlow.Phase}");

            var id = Runtime.Assets.AssetRegistry.Instance.GetOrCreateId("Scenes/SomeRoom.scene");
            RoomFlow.LoadRoomImpl = _ => false;
            ok = GameFlow.StartDay(7, DayPhase.Morning, id, "");
            Check("(c) premise: a failing loader fails", !ok);
            Check("(c) * the date does not change here either",
                  GameFlow.Day == 2 && GameFlow.Phase == DayPhase.Evening,
                  $"day {GameFlow.Day} {GameFlow.Phase}");

            RoomFlow.LoadRoomImpl = _ => true;
            ok = GameFlow.StartDay(7, DayPhase.Morning, id, "");
            Check("(d) premise: the success path really succeeds", ok);
            Check("(d) * control: on success the date does change (the rewind is not unconditional)",
                  GameFlow.Day == 7 && GameFlow.Phase == DayPhase.Morning,
                  $"day {GameFlow.Day} {GameFlow.Phase}");
        }
        finally
        {
            RoomFlow.LoadRoomImpl = prevLoader;
            GameFlow.DebugSetTime(1, DayPhase.Morning);
            try { System.IO.Directory.Delete(root, true); } catch {  }
        }
    }

    private static void TestArrivalFacing()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "pixelcore_facing_" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(root);

        var scene = new Scene("ArrivalRoom");
        var ctx = new GameContext { Scene = scene, Camera = new Camera(320, 180) };
        var camera = ctx.Camera!;
        var prevLoader = RoomFlow.LoadRoomImpl;
        var down = new Microsoft.Xna.Framework.Vector2(0f, 1f);
        var up = new Microsoft.Xna.Framework.Vector2(0f, -1f);
        var left = new Microsoft.Xna.Framework.Vector2(-1f, 0f);

        try
        {
            using var _ = Runtime.Assets.AssetRegistry.UseTemporary(System.IO.Path.Combine(root, "assets.json"));

            var player = scene.CreateEntity(Scene.PlayerName);
            var pc = player.AddComponent<Player.PlayerController>();
            scene.CreateEntity("Spawn_NorthDoor");
            scene.FlushPendingAdds();

            Check("premise: a fresh controller faces down (this was the old 'always down')", pc.Facing == down,
                  pc.Facing.ToString());

            Check("0 * the empty string is not a facing ('unspecified' is not 'faces down')",
                  !Dirs.TryParse("", out Dir _unused0));
            Check("0 all four names parse", Dirs.TryParse("Up", out var du) && du == Dir.Up
                  && Dirs.TryParse("Down", out Dir _unused1) && Dirs.TryParse("Left", out Dir _unused2)
                  && Dirs.TryParse("Right", out Dir _unused3));
            Check("0 there is one spelling - case variants are rejected (so files stay greppable)",
                  !Dirs.TryParse("up", out Dir _unused4));
            Check("0 numeric strings are rejected too (Enum.TryParse would accept them)",
                  !Dirs.TryParse("1", out Dir _unused5));
            Check("0 * saves, doors and the inspector share one list (one vocabulary)",
                  Dirs.Names.Length == 4 && Array.IndexOf(Dirs.Names, "Up") >= 0);

            RoomFlow.PlaceAtSpawn(scene, camera, "Spawn_NorthDoor", "Up");
            Check("1 * when the door states an arrival facing, that is used", pc.Facing == up, pc.Facing.ToString());

            RoomFlow.PlaceAtSpawn(scene, camera, "Spawn_NorthDoor", "");
            Check("2 * with no facing stated it is left alone (old file compatibility)", pc.Facing == up,
                  pc.Facing.ToString());

            pc.Facing = left;
            var bark = CaptureStderr(() => RoomFlow.PlaceAtSpawn(scene, camera, "Spawn_NorthDoor", "Upp"));
            Check("3 * an unreadable facing barks", bark.Contains("arrival facing"), bark);
            Check("3 it lists the valid values (the fix as well)", bark.Contains("Up") && bark.Contains("Left"), bark);
            Check("3 * and it leaves the facing alone (no silent fallback to down)", pc.Facing == left,
                  pc.Facing.ToString());

            GameFlow.Bind(ctx, camera);
            Runtime.Assets.AssetRegistry.Instance.Register(Rooms.Village.SceneId, "Scenes/Village.scene");

            RoomFlow.LoadRoomImpl = _ =>
            {
                foreach (var e in new System.Collections.Generic.List<Entity>(scene.Entities))
                    if (e.Name == Scene.PlayerName) scene.DestroyEntity(e);
                scene.FlushPending();

                var fresh = scene.CreateEntity(Scene.PlayerName);
                fresh.AddComponent<Player.PlayerController>();
                scene.CreateEntity("Spawn_NorthDoor");
                scene.FlushPendingAdds();
                return true;
            };
            var data = new Runtime.Save.SaveData
            {
                Room = new Runtime.Save.RoomSave { SceneId = Rooms.Village.SceneId, Spawn = "Spawn_NorthDoor" },
                Time = new Runtime.Save.TimeSave { Day = 1, Phase = "Morning" },
                Player = new Runtime.Save.PlayerSave { Facing = "Left", Costume = "default" },
            };
            Check("4 premise: entry succeeded (otherwise the rest is vacuous)", ctx.Save.ApplyGameState!(data));

            var after = Player.PlayerController.Of(scene);
            Check("4 premise: the room load really replaced the controller (otherwise ordering is not measured)",
                  after != null && !ReferenceEquals(after, pc));
            Check("4 ** continuing wins with the saved facing (not overwritten by the door default)",
                  after?.Facing == left, after?.Facing.ToString());

            var before5 = Player.PlayerController.Of(scene)!;
            before5.Facing = up;
            Check("5 premise: facing up before leaving", before5.Facing == up, before5.Facing.ToString());

            Check("5 premise: the room transition succeeded",
                  RoomFlow.LoadImmediate(Rooms.Village.SceneId, "Spawn_NorthDoor", scene, camera));
            var after5 = Player.PlayerController.Of(scene);
            Check("5 premise: the controller really is a new object (otherwise carry-over is not measured)",
                  after5 != null && !ReferenceEquals(after5, before5));
            Check("5 ** with no facing stated, the departure facing is carried over", after5?.Facing == up,
                  after5?.Facing.ToString());

            Player.PlayerController.Of(scene)!.Facing = up;
            Check("5 premise: set back to facing up", Player.PlayerController.Of(scene)!.Facing == up);
            RoomFlow.LoadImmediate(Rooms.Village.SceneId, "Spawn_NorthDoor", scene, camera, "Left");
            Check("5 * control: when the door states a facing, that wins",
                  Player.PlayerController.Of(scene)?.Facing == left,
                  Player.PlayerController.Of(scene)?.Facing.ToString());
        }
        finally
        {
            RoomFlow.LoadRoomImpl = prevLoader;
            try { System.IO.Directory.Delete(root, true); } catch {  }
        }
    }

    private static string CaptureStderr(Action body)
    {
        var prev = Console.Error;
        var buf = new System.IO.StringWriter();
        try { Console.SetError(buf); body(); }
        finally { Console.SetError(prev); }
        return buf.ToString();
    }

    private static void TestEventTables()
    {
        var scene = new Scene("EventTest");
        var ctx = new GameContext { Scene = scene, Camera = new Camera(320, 180) };
        var actor = scene.CreateEntity("Actor");
        scene.FlushPendingAdds();

        var prevLoader = RoomFlow.LoadRoomImpl;
        try
        {
            RoomFlow.LoadRoomImpl = _ => true;
            CutsceneDirector.ClearRegistry();
            CutsceneDirector.Bind(ctx);
            Runtime.Systems.GameActions.ClearRegistry();

            int ran = 0;
            Runtime.Systems.GameActions.Register("door.cellar", _ => ran++);
            CutsceneDirector.Register("house.wakeUp", _ => Forever());

            Check("premise: both tables are populated",
                  Runtime.Systems.GameActions.IsRegistered("door.cellar")
                  && CutsceneDirector.IsRegistered("house.wakeUp"));
            Check("the tables do not know each other (they were not merged)",
                  !Runtime.Systems.GameActions.IsRegistered("house.wakeUp")
                  && !CutsceneDirector.IsRegistered("door.cellar"));

            var it = actor.AddComponent<Runtime.Components.Interactable>();
            it.Kind = Runtime.Components.InteractKind.Event;
            it.Event = "door.cellar";
            InteractionDispatcher.Handle(it, actor);
            Check("* a game event name runs the method", ran == 1, ran.ToString());
            Check("* and no cutscene starts as a result (no locks or letterbox tag along)",
                  !CutsceneDirector.IsPlaying);

            CutsceneDirector.Play("house.wakeUp");
            ctx.Coroutines.Update(1f / 60f);
            Check("premise: a cutscene is running", CutsceneDirector.IsPlaying);
            InteractionDispatcher.Handle(it, actor);
            Check("* actions still run mid-cutscene - with one table, Play would refuse and swallow it silently",
                  ran == 2, ran.ToString());

            CutsceneDirector.Abort();

            var rooms = new System.Collections.Generic.Dictionary<string, CutsceneRegression.StartRoom>(StringComparer.Ordinal)
            {
                ["house.wakeUp"] = new("sceneA", "Trigger_Bed", "trigger"),
                ["door.cellar"] = new("sceneA", "CellarDoor", "interaction"),
                ["house.backDoor"] = new("sceneA", "TypoDoor", "interaction"),
            };
            var unknown = CutsceneRegression.UnknownEventNames(rooms);
            Check("* only the name in neither table is caught (the two registered ones stay quiet)",
                  unknown.Count == 1 && unknown[0].Contains("house.backDoor"),
                  string.Join(" / ", unknown));
            Check("* it points at where the call comes from (the entity name)",
                  unknown.Count == 1 && unknown[0].Contains("TypoDoor"), string.Join(" / ", unknown));

            Runtime.Systems.GameActions.Register("house.backDoor", _ => { });
            Check("* control: registering it as a game event silences it (it is not only reading the cutscene table)",
                  CutsceneRegression.UnknownEventNames(rooms).Count == 0);
        }
        finally
        {
            CutsceneDirector.Abort();
            CutsceneDirector.ClearRegistry();
            Runtime.Systems.GameActions.ClearRegistry();
            RoomFlow.LoadRoomImpl = prevLoader;
            FreezeState.ResetAll();
        }
    }

    private static System.Collections.Generic.IEnumerator<Wait> Forever()
    {
        yield return Wait.Seconds(9999f);
    }

    private static StoryLibrary BuildPollutedLibrary(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(dir, "test" + StoryLibrary.Extension),
            "# test.dirty [speaker:Hero]\nFirst line.\nSecond line.\n\n"
            + "@choice\nAsks a question.\n> Yes\n    So it is.\n> No\n    It is not.\n");
        return StoryLibrary.Load(dir);
    }

    private static void TestPhaseWindowsAndTags()
    {
        var scene = new Scene("WindowTest");
        var ctx = new GameContext { Scene = scene, Camera = new Camera(320, 180) };
        var prevLoader = RoomFlow.LoadRoomImpl;
        RoomFlow.LoadRoomImpl = _ => true;
        GameFlow.Bind(ctx, ctx.Camera!);
        try
        {
            foreach (var (phase, hour) in new[]
                { (DayPhase.Morning, 8f), (DayPhase.Afternoon, 14f), (DayPhase.Evening, 19f) })
            {
                GameFlow.StartDay(1, phase);
                var (start, end) = GameFlow.CurrentPhaseWindow;
                Check($"* {phase}: the StartDay hour is inside the window ({hour} ∈ [{start},{end}])",
                    ctx.Clock.Hour >= start && ctx.Clock.Hour <= end, ctx.Clock.Hour.ToString());
                Check($"   {phase}: the window's start is that phase's hour", MathF.Abs(start - hour) < 0.01f);

                GameFlow.SlideHour(end + 5f);
                Check($"   {phase}: sliding outside the window sticks to the end value", MathF.Abs(ctx.Clock.Hour - end) < 0.01f,
                    ctx.Clock.Hour.ToString());
                GameFlow.SlideHour(start + (end - start) * 0.5f);
                Check($"   {phase}: a value inside the window goes in as it is",
                    MathF.Abs(ctx.Clock.Hour - (start + (end - start) * 0.5f)) < 0.01f);
            }

            GameFlow.StartDay(1, DayPhase.Morning);
            Check("* morning tag = Day", GameFlow.CurrentLightingTag() == "Day", GameFlow.CurrentLightingTag());
            GameFlow.StartDay(1, DayPhase.Evening);
            Check("* evening tag = Dusk", GameFlow.CurrentLightingTag() == "Dusk", GameFlow.CurrentLightingTag());

            GameFlow.Weather = "Rain";
            Check("* weather is a dot suffix (not an axis - as an axis the table becomes two-dimensional and the rows multiply)",
                GameFlow.CurrentLightingTag() == "Dusk.Rain", GameFlow.CurrentLightingTag());
            GameFlow.StartDay(1, DayPhase.Evening);
            Check("* a new day does not inherit the weather (a value the save does not even hold must not survive)",
                GameFlow.Weather == "" && GameFlow.CurrentLightingTag() == "Dusk",
                GameFlow.CurrentLightingTag());
        }
        finally
        {
            RoomFlow.LoadRoomImpl = prevLoader;
            GameFlow.Weather = "";
        }
    }

    private static void TestNoIndependentClock()
    {
        var roots = new[] { "Runtime", "Gameplay" };
        var hourWrites = new List<string>();
        var speedWrites = new List<string>();
        int files = 0;

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith("SelfTest.cs", StringComparison.Ordinal)
                 || name.EndsWith("Harness.cs", StringComparison.Ordinal)) continue;
                files++;
                bool inBlock = false;
                int n = 0;
                foreach (var raw in File.ReadAllLines(file))
                {
                    n++;
                    var t = PixelCore.Runtime.Core.SourceScan.StripComments(raw, ref inBlock);
                    if (Regex.IsMatch(t, @"\bHour\s*=[^=]")) hourWrites.Add($"{name}:{n}");
                    if (Regex.IsMatch(t, @"\bSpeed\s*=[^=]") && t.Contains("Clock")) speedWrites.Add($"{name}:{n}");
                }
            }
        }

        Check($"premise: scanned the Runtime+Gameplay source ({files} files)", files >= 100);

        var allowed = new[] { "GameFlow.cs", "TimeOfDay.cs" };
        var stray = hourWrites.Where(h => !allowed.Any(a => h.StartsWith(a, StringComparison.Ordinal))).ToList();
        Check("* the only places that write the hour are StartDay and SetHourClamped"
              + (stray.Count == 0 ? "" : " - " + string.Join(", ", stray)), stray.Count == 0);
        Check($"   premise: those two really exist ({hourWrites.Count} places)", hourWrites.Count >= 2);

        Check("* nobody switches on Clock.Speed (no continuous 24h clock)"
              + (speedWrites.Count == 0 ? "" : " - " + string.Join(", ", speedWrites)), speedWrites.Count == 0);
        Check("* the runtime default is 0 too (if the default is on, code not switching it on does not help)",
            new PixelCore.Runtime.Systems.TimeOfDay().Speed == 0f);
    }

    private static void Check(string label, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  [pass] {label}"); }
        else { _fail++; Console.WriteLine($"  [FAIL] {label}{(detail != null ? $" — {detail}" : "")}"); }
    }
}
#endif
