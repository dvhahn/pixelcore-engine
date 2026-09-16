#if DEBUG
using System;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Systems;
using PixelCore.Runtime.Animation;
using PixelCore.Runtime.Cutscenes;

namespace PixelCore.Gameplay.Player;

public static class PlayerAnimSelfTest
{
    private const float Dt = 1f / 60f;
    private static int _pass, _fail;

    private static readonly int[] DirScancodes =
    {
        Input.SDL_SCANCODE_A, Input.SDL_SCANCODE_LEFT,
        Input.SDL_SCANCODE_D, Input.SDL_SCANCODE_RIGHT,
        Input.SDL_SCANCODE_W, Input.SDL_SCANCODE_UP,
        Input.SDL_SCANCODE_S, Input.SDL_SCANCODE_DOWN,
    };

    public static void Run()
    {
        Console.WriteLine("=== Player movement self-test ===");

        var down = new Vector2(0f, 1f);
        var up = new Vector2(0f, -1f);

        {
            var (scene, physics, player, anim) = NewRig(Vector2.Zero);
            var rb = player.GetComponent<Rigidbody2D>()!;
            int framesToFull = -1;
            for (int i = 0; i < 30; i++)
            {
                Step(physics, player, anim, Input.SDL_SCANCODE_S);
                if (framesToFull < 0 && rb.Velocity.Y >= PlayerController.DefaultMoveSpeed - 0.01f) framesToFull = i + 1;
            }
            Check($"walking reaches full speed quickly ({framesToFull} frames, at most 8)", framesToFull > 0 && framesToFull <= 8);
            Check("the walk clip plays while a direction is held", anim.CurrentClip?.Name == "Walk_D");
            Check($"the player really moves (y {player.GetComponent<Transform>()!.Position.Y:0.#} > 24 after half a second)",
                  player.GetComponent<Transform>()!.Position.Y > 24f);
        }

        {
            var (scene, physics, player, anim) = NewRig(Vector2.Zero);
            var rb = player.GetComponent<Rigidbody2D>()!;
            var pc = player.GetComponent<PlayerController>()!;
            for (int i = 0; i < 30; i++) Step(physics, player, anim, Input.SDL_SCANCODE_D, Input.SDL_SCANCODE_S);
            Check($"* a diagonal is normalised ({rb.Velocity.Length():0.0} = {PlayerController.DefaultMoveSpeed}, not 1.41x)",
                  MathF.Abs(rb.Velocity.Length() - PlayerController.DefaultMoveSpeed) < 0.5f);
            Check("   control: both axes move on a diagonal", rb.Velocity.X > 1f && rb.Velocity.Y > 1f);
            Check("a diagonal keeps the current facing when it is one of the two directions",
                  pc.Facing == down, pc.Facing.ToString());
            Check("   control: a mostly horizontal input faces horizontally",
                  PlayerController.FacingFor(Vector2.Normalize(new Vector2(1f, 0.2f)), down) == new Vector2(1f, 0f));
        }

        {
            var (scene, physics, player, anim) = NewRig(Vector2.Zero);
            var rb = player.GetComponent<Rigidbody2D>()!;
            for (int i = 0; i < 30; i++) Step(physics, player, anim, Input.SDL_SCANCODE_S);
            int stopFrames = -1;
            for (int i = 0; i < 30; i++)
            {
                Step(physics, player, anim);
                if (stopFrames < 0 && rb.Velocity == Vector2.Zero) stopFrames = i + 1;
            }
            Check($"releasing stops quickly ({stopFrames} frames, at most 10)", stopFrames > 0 && stopFrames <= 10);
            Check("the idle clip plays once released", anim.CurrentClip?.Name == "Idle_D");
        }

        {
            var (scene, physics, player, anim) = NewRig(Vector2.Zero);
            var pc = player.GetComponent<PlayerController>()!;
            for (int i = 0; i < 20; i++) Step(physics, player, anim, Input.SDL_SCANCODE_S);
            Step(physics, player, anim, Input.SDL_SCANCODE_W);
            Check("* reversing faces the new way on the same frame (no in-between step)",
                  pc.Facing == up && anim.CurrentClip?.Name == "Walk_U", $"{pc.Facing} {anim.CurrentClip?.Name}");
        }

        {
            var (scene, physics, player, anim) = NewRig(Vector2.Zero);
            MakeBox(scene, "WallS", new Vector2(0, 16), new Vector2(48, 8));
            scene.FlushPendingAdds();
            var t = player.GetComponent<Transform>()!;
            for (int i = 0; i < 60; i++) Step(physics, player, anim, Input.SDL_SCANCODE_S);
            Check($"a wall stops the player (y {t.Position.Y:0.#} < 16)", t.Position.Y < 16f);
            Check("   control: it did walk up to the wall", t.Position.Y > 1f);
        }

        TestLockSemantics();
        TestControllerIsComponent();
        TestEditModeGate();
        TestCutsceneFacingPersists();

        Console.WriteLine($"=== {_pass} passed, {_fail} failed ===");
    }

    private static void TestControllerIsComponent()
    {
        var scene = new Scene("CtrlComp");
        var e = scene.CreateEntity(Scene.PlayerName);
        var pc = e.AddComponent<PlayerController>();
        scene.FlushPendingAdds();
        Check("the controller attaches as a Component", pc is Component && e.GetComponent<PlayerController>() != null);
        Check("Of(scene) finds it", ReferenceEquals(PlayerController.Of(scene), pc));

        pc.Facing = new Vector2(-1f, 0f);
        var scene2 = new Scene("CtrlComp2");
        var e2 = scene2.CreateEntity(Scene.PlayerName);
        var pc2 = e2.AddComponent<PlayerController>();
        scene2.FlushPendingAdds();
        Check("* a new scene's controller starts at defaults (as a static it carried over)",
              pc2.Facing == new Vector2(0f, 1f));
        Check("the old scene's instance keeps its own state (they do not mix)",
              pc.Facing == new Vector2(-1f, 0f));

        var captured = Runtime.Serialization.ComponentDataRegistry.CaptureAll(e2, forPersist: true);
        var pcd = captured.Find(c => c is Runtime.Serialization.PlayerControllerData);
        Check("the controller is saved into the scene (the fact of attachment)", pcd != null);

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pc_ctrl_tuning.scene");
        Runtime.Serialization.SceneSerializer.SaveToFile(
            Runtime.Serialization.SceneSerializer.ToData(scene2), path);
        var json = System.IO.File.ReadAllText(path);
        try { System.IO.File.Delete(path); } catch {  }

        Check("the playerController discriminator lands in the scene file", json.Contains("\"playerController\""));
        Check("* tuning values do not go into the file (clause 5 - one owner per value)",
              !json.Contains("MoveSpeed") && !json.Contains("Acceleration")
              && !json.Contains("TapTurn") && !json.Contains("MinAnimSpeed"));

        var scene3 = new Scene("CtrlComp3");
        var e3 = scene3.CreateEntity(Scene.PlayerName);
        pcd!.Apply(e3);
        scene3.FlushPendingAdds();
        Check("loading restores the controller", e3.GetComponent<PlayerController>() != null);
        Check("the restored controller has the code default tuning",
              Math.Abs(e3.GetComponent<PlayerController>()!.MoveSpeed - PlayerController.DefaultMoveSpeed) < 0.01f);
    }

    private static void TestLockSemantics()
    {
        {
            var (scene, physics, player, anim) = NewRig(new Vector2(100, 100));
            using (var _ = new FreezeScope(FreezeFlags.PlayerInput))
            {
                anim.Play("Walk_L");
                for (int i = 0; i < 10; i++) Step(physics, player, anim);
                Check("hard lock: idle does not overwrite the walk clip the cutscene is driving",
                      anim.CurrentClip?.Name == "Walk_L");
                Check("hard lock: inertia is cut (no sliding at the speed held when it locked)",
                      player.GetComponent<Rigidbody2D>()!.Velocity == Vector2.Zero);
            }
            Check("hard lock: leaving the scope releases it",
                  !FreezeState.IsFrozen(FreezeFlags.PlayerInput));
        }

        {
            var (scene, physics, player, anim) = NewRig(new Vector2(100, 100));
            for (int i = 0; i < 20; i++) Step(physics, player, anim, DirScancodes[3]);
            float movingSpeed = player.GetComponent<Rigidbody2D>()!.Velocity.Length();
            Check("soft premise: there is speed while running", movingSpeed > 1f);

            FreezeState.SetSoft(FreezeSource.RoomTransition, true);
            Step(physics, player, anim, DirScancodes[3]);
            float justAfter = player.GetComponent<Rigidbody2D>()!.Velocity.Length();
            Check("soft lock: it decelerates rather than snapping to zero",
                  justAfter > 0f && justAfter < movingSpeed);

            for (int i = 0; i < 40; i++) Step(physics, player, anim, DirScancodes[3]);
            Check("soft lock: it does stop eventually",
                  player.GetComponent<Rigidbody2D>()!.Velocity.Length() < 0.5f);
            Check("soft lock: the idle transition works (a hard lock would freeze it on Walk)",
                  anim.CurrentClip?.Name?.StartsWith("Idle_") == true);

            FreezeState.SetSoft(FreezeSource.RoomTransition, false);
            Check("soft lock: releasing it moves again",
                  !FreezeState.IsFrozen(FreezeFlags.PlayerInputSoft));
        }

        FreezeState.ResetAll();
        FreezeState.SetSoft(FreezeSource.RoomTransition, true);
        FreezeState.SetSoft(FreezeSource.Dialogue, true);
        FreezeState.SetSoft(FreezeSource.RoomTransition, false);
        Check("union: it stays locked while any reason remains",
              FreezeState.IsFrozen(FreezeFlags.PlayerInputSoft));
        FreezeState.SetSoft(FreezeSource.Dialogue, false);
        Check("union: it releases once every reason is cleared",
              !FreezeState.IsFrozen(FreezeFlags.PlayerInputSoft));

        Runtime.UI.DialogueBox.Show(null, "A");
        Runtime.UI.DialogueBox.Show(null, "B");
        Runtime.UI.DialogueBox.Close();
        Check("* idempotent: two Shows then one Close releases completely",
              !FreezeState.IsFrozen(FreezeFlags.PlayerInputSoft));

        Runtime.UI.DialogueBox.Show(null, System.Array.Empty<string>());
        Check("* zero pages does not open the box (a runtime guard independent of the overload)",
              !Runtime.UI.DialogueBox.Active);
        Runtime.UI.DialogueBox.Show(null, "this one shows properly");
        Check("* the two-argument Show opens normally", Runtime.UI.DialogueBox.Active);
        Runtime.UI.DialogueBox.Close();

        var probeScene = new Scene("InteractProbe");
        var probe = probeScene.CreateEntity("Bed");
        var readIt = probe.AddComponent<Interactable>();
        readIt.Kind = InteractKind.Read;
        readIt.Text = "It is a bed.";
        probeScene.FlushPendingAdds();
        Systems.InteractionDispatcher.Handle(readIt, probe);
        Check("* examining inline text really opens the dialogue box (the fixed bug)",
              Runtime.UI.DialogueBox.Active);
        Runtime.UI.DialogueBox.Close();

        Runtime.UI.DialogueBox.Show(null, "");
        Check("Show with empty text creates neither a box nor a lock",
              !Runtime.UI.DialogueBox.Active
              && !FreezeState.IsFrozen(FreezeFlags.PlayerInputSoft));

        Runtime.UI.DialogueBox.Show(null, "open");
        Check("premise: the dialogue box takes a soft lock",
              FreezeState.IsFrozen(FreezeFlags.PlayerInputSoft));
        FreezeState.ResetAll();
        Check("ResetAll clears the soft side too (always released after a load)",
              !FreezeState.IsFrozen(FreezeFlags.PlayerInputSoft));
        Runtime.UI.DialogueBox.Close();

        FreezeState.ResetAll();
        Runtime.UI.DialogueBox.Show(null, "a line");
        using (var _ = new FreezeScope(FreezeFlags.PlayerInput))
        {
            Runtime.UI.DialogueBox.Close();
            Check("closing the dialogue box leaves the cutscene hard lock in place",
                  FreezeState.IsFrozen(FreezeFlags.PlayerInput)
                  && !FreezeState.IsFrozen(FreezeFlags.PlayerInputSoft));
        }
        FreezeState.ResetAll();
    }

    private static void TestEditModeGate()
    {
        var (scene, _, player, anim) = NewRig(Vector2.Zero);

        Input.DebugTick();
        Input.DebugSetKey(Input.SDL_SCANCODE_S, true);
        for (int i = 0; i < 60; i++) { scene.FlushPending(); Input.DebugTick(); }

        Check("* housekeeping alone does not wake components - WASD in edit mode plays no walk animation",
            anim.CurrentClip == null);

        bool sawWalk = false;
        for (int i = 0; i < 30; i++)
        {
            scene.Update(Dt);
            if (anim.CurrentClip?.Name == "Walk_D") sawWalk = true;
            Input.DebugTick();
        }
        Check("* control: one simulated tick puts the same input on a walk clip", sawWalk);

        var born = scene.CreateEntity("BornLater");
        scene.FlushPending();
        Check("* the gate does not block housekeeping - a new entity commits to the list",
            scene.FindEntity("BornLater") == born);

        scene.DestroyEntity(born);
        scene.FlushPending();
        Check("housekeeping commits removals too", scene.FindEntity("BornLater") == null);

        foreach (var k in DirScancodes) Input.DebugSetKey(k, false);
        Input.DebugTick();
    }

    private static void TestCutsceneFacingPersists()
    {
        var up = new Vector2(0f, -1f);
        var down = new Vector2(0f, 1f);

        {
            var (scene, physics, player, anim) = NewRig(Vector2.Zero);
            var pc = player.GetComponent<PlayerController>()!;
            CutsceneDirector.ClearRegistry();
            var during = RunFaceCutscene(scene, physics, player, anim, pc);

            Check("1 premise: during the cutscene it looks like it is facing up (the aim cone is set)",
                  during == up, during.ToString());
            Check("1 * control: with no hook it reverts once the cutscene ends (this was the symptom)",
                  pc.Facing == down, pc.Facing.ToString());
        }

        {
            var (scene, physics, player, anim) = NewRig(Vector2.Zero);
            var pc = player.GetComponent<PlayerController>()!;
            CutsceneDirector.ClearRegistry();
            CutsceneDirector.PersistFacing = Systems.ActorFacing.Set;
            RunFaceCutscene(scene, physics, player, anim, pc);

            Check("2 * with the persist-facing hook, the facing survives the cutscene",
                  pc.Facing == up, pc.Facing.ToString());
            Check("2 * the aim cone points the same way (the controller pushing back gives the same value)",
                  player.GetComponent<Interactor>()?.Facing == up);
        }

        {
            var (scene, physics, player, anim) = NewRig(Vector2.Zero);
            var pc = player.GetComponent<PlayerController>()!;
            CutsceneDirector.ClearRegistry();

            var prev = Console.Out;
            var buf = new System.IO.StringWriter();
            try
            {
                Console.SetOut(buf);
                RunFaceCutscene(scene, physics, player, anim, pc);
                RunFaceCutscene(scene, physics, player, anim, pc);
            }
            finally { Console.SetOut(prev); }

            int warns = CountOccurrences(buf.ToString(), "CutsceneDirector.PersistFacing");
            Check("3 * a missing hook does not pass silently (same clause as RoomLoader)", warns >= 1,
                  $"{warns} times");
            Check("3 * but it barks only once - barking on every walk buries the real first line", warns == 1,
                  $"{warns} times");
        }

        {
            var (scene, physics, player, anim) = NewRig(Vector2.Zero);
            var npc = scene.CreateEntity("Neighbour");
            scene.FlushPendingAdds();
            CutsceneDirector.ClearRegistry();
            CutsceneDirector.PersistFacing = Systems.ActorFacing.Set;

            CutsceneLog.ResetCounters();
            RunFaceCutscene(scene, physics, player, anim, player.GetComponent<PlayerController>()!, actor: "Neighbour");

            Check("4 * with neither a directional clip nor persist-facing it barks at grade 2 (it used to log nothing)",
                  CutsceneLog.SkipCount == 1, $"{CutsceneLog.SkipCount} - {CutsceneLog.LastMessage}");
            Check("4 the message names the clip it looked for (where to fix the script)",
                  CutsceneLog.LastMessage.Contains("Idle_U") && CutsceneLog.LastMessage.Contains("Walk_U"),
                  CutsceneLog.LastMessage);
        }

        {
            var (scene, physics, player, anim) = NewRig(Vector2.Zero);
            var npc = scene.CreateEntity("Neighbour");
            var na = npc.AddComponent<Animator>();
            na.AddClip(new AnimationClip("Idle_U").AddFramesFromRow(0, 0, 1, 16, 16));
            scene.FlushPendingAdds();
            CutsceneDirector.ClearRegistry();
            CutsceneDirector.PersistFacing = Systems.ActorFacing.Set;

            CutsceneLog.ResetCounters();
            RunFaceCutscene(scene, physics, player, anim, player.GetComponent<PlayerController>()!, actor: "Neighbour");

            Check("5 * an NPC with directional clips stays quiet (the clip is the facing)",
                  CutsceneLog.SkipCount == 0, CutsceneLog.LastMessage);
            Check("5 that clip really was selected", na.CurrentClip?.Name == "Idle_U", na.CurrentClip?.Name);
        }

        {
            var scene = new Scene("FacingNoClip");
            var physics = new PhysicsSystem(scene);
            var e = scene.CreateEntity("Player");
            var pc = e.AddComponent<PlayerController>();
            var anim = e.AddComponent<Animator>();
            scene.FlushPendingAdds();

            CutsceneDirector.ClearRegistry();
            CutsceneDirector.PersistFacing = Systems.ActorFacing.Set;

            CutsceneLog.ResetCounters();
            RunFaceCutscene(scene, physics, e, anim, pc);

            Check("6 * with no directional clip but persist-facing applied it stays quiet (the verb did its job)",
                  CutsceneLog.SkipCount == 0, CutsceneLog.LastMessage);
            Check("6 premise: the facing really was applied (otherwise the above is vacuous)",
                  pc.Facing == up, pc.Facing.ToString());
        }

        CutsceneDirector.ClearRegistry();
        FreezeState.ResetAll();
    }

    private static Vector2 RunFaceCutscene(Scene scene, PhysicsSystem physics, Entity player,
                                           Animator anim, PlayerController pc, string actor = "hero")
    {
        if (player.GetComponent<Interactor>() == null) player.AddComponent<Interactor>();

        var ctx = new GameContext { Scene = scene, Camera = new Camera(320, 180) };
        CutsceneDirector.Bind(ctx);
        CutsceneDirector.SetCast("hero", "Player");
        CutsceneDirector.Register("test.facing", c => FaceOnly(c, actor));
        CutsceneDirector.Play("test.facing");

        ctx.Coroutines.Update(Dt);
        var during = player.GetComponent<Interactor>()?.Facing ?? Vector2.Zero;

        for (int i = 0; i < 20; i++) { ctx.Coroutines.Update(Dt); Step(physics, player, anim); }
        return during;
    }

    private static System.Collections.Generic.IEnumerator<Wait> FaceOnly(Cutscene c, string actor)
    {
        using var _ = c.Freeze();
        yield return c.Face(actor, Dir.Up);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    private static void Step(PhysicsSystem physics, Entity player, Animator anim, params int[] downKeys)
    {
        Input.DebugTick();
        foreach (var k in DirScancodes) Input.DebugSetKey(k, false);
        foreach (var k in downKeys) Input.DebugSetKey(k, true);

        var pc = player.GetComponent<PlayerController>()!;
        pc.Update(Dt);
        pc.FixedTick(Dt);
        physics.Update(Dt);
        anim.Update(Dt);
    }

    private static (Scene, PhysicsSystem, Entity, Animator) NewRig(Vector2 start)
    {
        var scene = new Scene("AnimTest");
        var physics = new PhysicsSystem(scene);

        var e = scene.CreateEntity("Player");
        e.GetComponent<Transform>()!.Position = start;
        var cap = e.AddComponent<CapsuleCollider2D>();
        cap.Radius = 3f; cap.Length = 1f; cap.Horizontal = true;
        var rb = e.AddComponent<Rigidbody2D>();
        rb.UseGravity = false;
        e.AddComponent<PlayerController>();
        var anim = e.AddComponent<Animator>();
        foreach (var s in new[] { "D", "U", "L", "R" })
        {
            anim.AddClip(new AnimationClip($"Walk_{s}").AddFramesFromRow(0, 0, 4, 16, 16));
            anim.AddClip(new AnimationClip($"Idle_{s}").AddFramesFromRow(0, 0, 1, 16, 16));
        }

        scene.FlushPendingAdds();

        e.GetComponent<PlayerController>()!.DebugReset(start);
        foreach (var k in DirScancodes) Input.DebugSetKey(k, false);
        Input.DebugTick();
        return (scene, physics, e, anim);
    }

    private static void MakeBox(Scene scene, string name, Vector2 pos, Vector2 size)
    {
        var e = scene.CreateEntity(name);
        e.GetComponent<Transform>()!.Position = pos;
        var box = e.AddComponent<BoxCollider2D>();
        box.Size = size;
    }

    private static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name);
        if (ok) _pass++; else _fail++;
    }

    private static void Check(string name, bool ok, string? detail)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name
                          + (ok || detail == null ? "" : $"  ({detail})"));
        if (ok) _pass++; else _fail++;
    }
}
#endif
