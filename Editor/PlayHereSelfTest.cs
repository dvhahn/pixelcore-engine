using System;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;
using PixelCore.Editor.Panels;

namespace PixelCore.Editor;

public static class PlayHereSelfTest
{
    private static int _pass, _fail;

    private static readonly Vector2 Authored = new(100, 100);
    private static readonly Vector2 Clicked = new(500, 480);

    public static void Run()
    {
        Console.WriteLine("=== PlayHere (play from here) self-test ===");

        var (state, player) = Fresh();
        SceneViewPanel.StartPlayAt(state, Clicked);
        Check("it enters play mode", state.IsPlayMode);
        Check("the player goes to the clicked point", Pos(player) == Clicked);

        state.SetMode(EditorMode.Edit);
        Check("* stopping restores the authored position (the scene stays clean - the reason this tool exists)",
            Pos(player) == Authored);

        var (bad, badPlayer) = Fresh();
        Pos(badPlayer, Clicked);
        bad.SetMode(EditorMode.Play);
        bad.SetMode(EditorMode.Edit);
        Check("* control: reversing the order contaminates the authored position (which is why the order is everything)",
            Pos(badPlayer) == Clicked);

        var empty = new EditorState { CurrentScene = new Scene("EmptyRoom") };
        SceneViewPanel.StartPlayAt(empty, Clicked);
        Check("* with no player it does not enter play (no point starting a session with nothing to place)",
            empty.IsEditMode);

        SceneViewPanel.StartPlayAt(new EditorState(), Clicked);
        Check("it does not blow up with no scene", true);

        var (v, vPlayer) = Fresh();
        vPlayer.GetComponent<Rigidbody2D>()!.Velocity = new Vector2(120, -60);
        SceneViewPanel.StartPlayAt(v, Clicked);
        Check("* velocity is 0 on arrival (without cutting it, it slides away from the clicked spot)",
            vPlayer.GetComponent<Rigidbody2D>()!.Velocity == Vector2.Zero);

        var (p2, p2Player) = Fresh();
        SceneViewPanel.StartPlayAt(p2, Clicked);
        SceneViewPanel.StartPlayAt(p2, new Vector2(300, 300));
        Check("a second click during play also moves it there", Pos(p2Player) == new Vector2(300, 300));
        p2.SetMode(EditorMode.Edit);
        Check("* even after two clicks, stopping gives the authored position (the snapshot is not retaken and hardened)",
            Pos(p2Player) == Authored);

        Console.WriteLine($"=== {_pass} passed, {_fail} failed ===");
    }

    private static (EditorState, Entity) Fresh()
    {
        var scene = new Scene("t");
        var player = scene.CreateEntity(Scene.PlayerName);
        player.AddComponent<Rigidbody2D>();
        Pos(player, Authored);
        scene.Update(0f);
        return (new EditorState { CurrentScene = scene }, player);
    }

    private static Vector2 Pos(Entity e) => e.GetComponent<Transform>()!.Position;
    private static void Pos(Entity e, Vector2 v) => e.GetComponent<Transform>()!.Position = v;

    private static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name);
        if (ok) _pass++; else _fail++;
    }
}
