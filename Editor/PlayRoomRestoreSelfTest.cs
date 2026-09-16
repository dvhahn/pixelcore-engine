using System;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Editor;

public static class PlayRoomRestoreSelfTest
{
    private static int _pass, _fail;

    private const string RoomA = "RoomA";
    private const string RoomB = "RoomB";
    private static readonly Vector2 Authored = new(10, 10);
    private static readonly Vector2 Edited = new(99, 99);

    public static void Run()
    {
        Console.WriteLine("=== PlayRoomRestore (stopping after a room change) self-test ===");

        TestDirtyEditSurvives();
        TestCleanSceneUsesDisk();
        TestStashIsSingleUse();
        TestCleanPlayClearsStash();
        TestUntitledSceneSurvives();
        TestNoRoomChangeStillUsesSnapshot();
        TestRebuildClearsHistory();

        Console.WriteLine($"=== PlayRoomRestore: {_pass} passed, {_fail} failed ===");
    }

    private static void TestDirtyEditSurvives()
    {
        var (state, scene) = Fresh();

        Pos(scene, "Spawn", Edited);
        state.MarkDirty();
        Check("premise: it is dirty after the edit", state.IsDirty);

        PlayThenChangeRoomThenStop(state, scene);

        Check("* it returns to the original room", scene.Name == RoomA, scene.Name);
        Check("* the unsaved edit is alive (the whole point of this)",
              Pos(scene, "Spawn") == Edited, Pos(scene, "Spawn").ToString());
        Check("* the dot is still there too (nothing was saved, so it must stay dirty)", state.IsDirty);
        Check("the stash was consumed and is empty", !state.HasPlayEntryStash);
    }

    private static void TestCleanSceneUsesDisk()
    {
        var (state, scene) = Fresh();

        Pos(scene, "Spawn", Edited);
        state.ClearDirty();
        Check("premise: it is not dirty", !state.IsDirty);

        PlayThenChangeRoomThenStop(state, scene);

        Check("* control: when clean it returns to the disk contents (no stash is taken)",
              Pos(scene, "Spawn") == Authored, Pos(scene, "Spawn").ToString());
        Check("* control: dirty stays cleared (disk == the saved file)", !state.IsDirty);
    }

    private static void TestStashIsSingleUse()
    {
        var (state, scene) = Fresh();
        Pos(scene, "Spawn", Edited);
        state.MarkDirty();

        PlayThenChangeRoomThenStop(state, scene);
        Check("premise: the first restore kept the edit", Pos(scene, "Spawn") == Edited);

        var roomB = RoomBData();
        SceneSerializer.FromData(scene, roomB);
        scene.Name = roomB.Name;
        PlayRoomRestore.Run(state, DiskLoader);
        Check("* the second restore falls back to disk (no stash reuse)",
              Pos(scene, "Spawn") == Authored, Pos(scene, "Spawn").ToString());
    }

    private static void TestCleanPlayClearsStash()
    {
        var (state, scene) = Fresh();
        Pos(scene, "Spawn", Edited);
        state.MarkDirty();

        state.SetMode(EditorMode.Play);
        Check("premise: a dirty play session takes a stash", state.HasPlayEntryStash);
        state.SetMode(EditorMode.Edit);
        Check("an unconsumed stash remains (the only consumer is the room-change path)", state.HasPlayEntryStash);

        state.ClearDirty();
        state.SetMode(EditorMode.Play);
        Check("* entering a clean session clears the previous stash (blocking the returning ghost)",
              !state.HasPlayEntryStash);
        state.SetMode(EditorMode.Edit);
    }

    private static void TestUntitledSceneSurvives()
    {
        var (state, scene) = Fresh();
        state.CurrentScenePath = null;
        Pos(scene, "Spawn", Edited);
        state.MarkDirty();

        PlayThenChangeRoomThenStop(state, scene, disk: () => null);

        Check("* a never-saved scene is restored too (the old path silently gave up)",
              scene.Name == RoomA && Pos(scene, "Spawn") == Edited,
              $"{scene.Name} / {Pos(scene, "Spawn")}");
        Check("* an untitled scene stays dirty", state.IsDirty);
    }

    private static void TestNoRoomChangeStillUsesSnapshot()
    {
        var (state, scene) = Fresh();
        state.MarkDirty();

        state.SetMode(EditorMode.Play);
        Pos(scene, "Spawn", Edited);
        state.SetMode(EditorMode.Edit);

        Check("* with no room change the snapshot rewinds it (the stash does not get involved)",
              Pos(scene, "Spawn") == Authored, Pos(scene, "Spawn").ToString());
        Check("* and the scene was not swapped (no recharge happened)", scene.Name == RoomA);
    }

    private static void PlayThenChangeRoomThenStop(EditorState state, Scene scene, Func<SceneData?>? disk = null)
    {
        state.SetMode(EditorMode.Play);

        var roomB = RoomBData();
        SceneSerializer.FromData(scene, roomB);
        scene.Name = roomB.Name;
        Check("premise: the room change swapped the scene", scene.Name == RoomB && scene.FindEntity("Spawn") == null,
              $"{scene.Name} / Spawn={(scene.FindEntity("Spawn") == null ? "missing" : "present")}");

        void OnMode(EditorMode m)
        {
            if (m == EditorMode.Edit) PlayRoomRestore.Run(state, disk ?? DiskLoader);
        }
        state.OnModeChanged += OnMode;
        state.SetMode(EditorMode.Edit);
        state.OnModeChanged -= OnMode;
    }

    private static SceneData? DiskLoader() => RoomAData();

    private static SceneData RoomAData()
    {
        var scene = new Scene(RoomA);
        var e = scene.CreateEntity("Spawn");
        e.GetComponent<Transform>()!.Position = Authored;
        scene.FlushPendingAdds();
        return SceneSerializer.ToData(scene);
    }

    private static SceneData RoomBData()
    {
        var scene = new Scene(RoomB);
        var e = scene.CreateEntity("OtherRoomProp");
        e.GetComponent<Transform>()!.Position = new Vector2(5, 5);
        scene.FlushPendingAdds();
        return SceneSerializer.ToData(scene);
    }

    private static (EditorState, Scene) Fresh()
    {
        var scene = new Scene(RoomA);
        SceneSerializer.FromData(scene, RoomAData());
        var state = new EditorState { CurrentScene = scene, CurrentScenePath = "/tmp/pc_playroom_RoomA.scene" };
        state.ClearDirty();
        return (state, scene);
    }

    private static Vector2 Pos(Scene scene, string name)
        => scene.FindEntity(name)?.GetComponent<Transform>()?.Position ?? new Vector2(float.NaN);

    private static void Pos(Scene scene, string name, Vector2 v)
        => scene.FindEntity(name)!.GetComponent<Transform>()!.Position = v;

    private static void Check(string what, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  [PASS] {what}"); }
        else { _fail++; Console.WriteLine($"  [FAIL] {what}{(detail != null ? $" — {detail}" : "")}"); }
    }

    private static void TestRebuildClearsHistory()
    {
        var (state, scene) = Fresh();
        var spawn = scene.FindEntity("Spawn")!;
        int idBefore = spawn.Id;

        state.ExecuteCommand(new PixelCore.Editor.Commands.MoveEntityCommand(spawn, Authored, Edited));
        Check("premise: a command is on the stack", state.CommandHistory.CanUndo);

        PlayRoomRestore.Recharge(state, RoomAData(), keepDirty: false);
        scene.Update(0f);

        var after = scene.FindEntity("Spawn");
        Check($"★ premise: a rebuild gives the same name a new id (before {idBefore} / after {after?.Id}) - " +
              "this is the reason for clearing",
              after != null && after.Id != idBefore);
        Check("★ so the history is cleared (otherwise Cmd+Z is a dead stack that only prints 'target is gone')",
              !state.CommandHistory.CanUndo && !state.CommandHistory.CanRedo);

        var (state2, scene2) = Fresh();
        var sp2 = scene2.FindEntity("Spawn")!;
        state2.ExecuteCommand(new PixelCore.Editor.Commands.MoveEntityCommand(sp2, Authored, Edited));
        Check("★ control: without a rebuild the history survives", state2.CommandHistory.CanUndo);

        state2.ClearDirty();
        Check("★ control: marking as saved does not clear the history", state2.CommandHistory.CanUndo);
    }
}
