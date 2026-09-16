using System;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Rendering;
using PixelCore.Runtime.Cutscenes;
using PixelCore.Gameplay.Player;

namespace PixelCore.Gameplay.Systems;

public static class RoomFlow
{
    public static Func<string, bool>? LoadRoomImpl;

    public static bool RoomChangedDuringPlay;

    public static string LastSpawn { get; private set; } = "";

    private enum Phase { None, FadeOut, FadeIn }
    private static Phase _phase;
    private static string _sceneId = "", _spawn = "", _facing = "";
    private const float FadeDuration = 0.22f;

    public static bool IsTransitioning => _phase != Phase.None;

    public static void Transition(string sceneId, string spawn, string? source = null,
                                  string facing = "")
    {
        if (IsTransitioning) return;

        string who = string.IsNullOrEmpty(source) ? "" : $" (door '{source}')";
        if (string.IsNullOrEmpty(sceneId))
        {
            Console.Error.WriteLine(
                $"[Room] ✘ the door's Target is empty, so the transition failed{who} - pick a destination room in the inspector");
            return;
        }
        if (!Runtime.Assets.AssetRegistry.LooksLikeId(sceneId))
        {
            Console.Error.WriteLine(
                $"[Room] ✘ the door's Target '{sceneId}' is not a scene id, so the transition failed{who} - " +
                "it is a legacy room name. Pick the room again in the inspector");
            return;
        }
        if (Runtime.Assets.AssetRegistry.Instance.GetPath(sceneId) == null)
        {
            Console.Error.WriteLine(
                $"[Room] ✘ scene asset id '{sceneId}' is not in the registry, so the transition failed{who} - " +
                "the scene was deleted, or assets.json has not been scanned");
            return;
        }

        _sceneId = sceneId;
        _spawn = spawn;
        _facing = facing;
        _phase = Phase.FadeOut;
        FreezeState.SetSoft(FreezeSource.RoomTransition, true);
        Interactor.GlobalLock = true;
        ScreenFader.FadeTo(1f, FadeDuration);
        Console.WriteLine($"[Room] transition: {RoomLabel(sceneId)} (spawn: {(string.IsNullOrEmpty(spawn) ? "-" : spawn)})");
    }

    public static void Abort()
    {
        _phase = Phase.None;
        FreezeState.SetSoft(FreezeSource.RoomTransition, false);
        Interactor.GlobalLock = false;
        ScreenFader.Set(0f);
        Runtime.UI.DialogueBox.Close();
    }

    public static void Update(Scene scene, Camera camera)
    {
        switch (_phase)
        {
            case Phase.FadeOut when ScreenFader.Alpha >= 1f:
                Runtime.UI.DialogueBox.Close();
                if (!LoadImmediate(_sceneId, _spawn, scene, camera, _facing))
                {
                    Console.Error.WriteLine($"[Room] ✘ load failed: {RoomLabel(_sceneId)} - returning to the original room");
                    _phase = Phase.FadeIn;
                    ScreenFader.FadeTo(0f, FadeDuration);
                    break;
                }

                _phase = Phase.FadeIn;
                ScreenFader.FadeTo(0f, FadeDuration);
                break;

            case Phase.FadeIn when ScreenFader.Alpha <= 0f:
                _phase = Phase.None;
                FreezeState.SetSoft(FreezeSource.RoomTransition, false);
                Interactor.GlobalLock = false;
                Console.WriteLine("[Room] transition complete");
                break;
        }
    }

    public static bool LoadImmediate(string sceneId, string spawn, Scene scene, Camera camera,
                                     string facing = "")
    {
        if (LoadRoomImpl == null)
        {
            Console.WriteLine("[Room] ✘ no scene loader was injected (RoomFlow.LoadRoomImpl)");
            return false;
        }

        Runtime.UI.DialogueBox.Close();

        var carried = string.IsNullOrEmpty(facing) ? ActorFacing.Get(scene.FindPlayer()) : null;

        if (!LoadRoomImpl(sceneId)) return false;

        Runtime.Nav.Nav.Invalidate();
        RoomChangedDuringPlay = true;
        PlaceAtSpawn(scene, camera, spawn, facing);

        if (carried is { } keep) ActorFacing.Set(scene.FindPlayer(), keep);
        return true;
    }

    public static void PlaceAtSpawn(Scene scene, Camera camera, string spawn, string facing = "")
    {
        LastSpawn = spawn;

        var player = scene.FindPlayer();
        var pt = player?.GetComponent<Transform>();
        if (pt == null) return;

        var st = string.IsNullOrEmpty(spawn) ? null : scene.FindEntity(spawn)?.GetComponent<Transform>();
        if (st != null) pt.Position = st.Position;
        else if (!string.IsNullOrEmpty(spawn))
            Console.WriteLine($"[Room] no such spawn point: {spawn} - using the scene's player position");

        ApplyArrivalFacing(player, facing);

        camera.Position = pt.Position + camera.FollowOffset;
        if (player != null) WarnIfSpawnBlocked(scene, player, spawn);
    }

    private static void ApplyArrivalFacing(Entity? player, string facing)
    {
        if (player == null || string.IsNullOrEmpty(facing)) return;

        if (!Runtime.Cutscenes.Dirs.TryParse(facing, out var dir))
        {
            Console.Error.WriteLine(
                $"[Room] ✘ could not read the door's arrival facing '{facing}' - leaving the facing alone. " +
                $"valid values: {string.Join(", ", Runtime.Cutscenes.Dirs.Names)}");
            return;
        }

        if (!ActorFacing.Set(player, Runtime.Cutscenes.Dirs.Vector(dir)))
            Console.WriteLine("[Room] ⚠ could not set the arrival facing - the player has no PlayerController");
    }

    private static string RoomLabel(string sceneId)
    {
        var path = Runtime.Assets.AssetRegistry.Instance.GetPath(sceneId);
        return path == null ? sceneId : $"{System.IO.Path.GetFileNameWithoutExtension(path)} ({sceneId})";
    }

    private static void WarnIfSpawnBlocked(Scene scene, Entity player, string spawnName)
    {
        foreach (var pc in player.Components)
        {
            if (pc is not Collider2D self || !self.Enabled || self.IsTrigger) continue;
            var pb = self.GetBounds();

            foreach (var e in scene.Entities)
            {
                if (e == player || !e.ActiveInHierarchy) continue;
                foreach (var oc in e.Components)
                {
                    if (oc is not Collider2D other || !other.Enabled || other.IsTrigger) continue;
                    var ob = other.GetBounds();
                    if (!pb.Intersects(ob)) continue;

                    float ox = MathF.Min(pb.Max.X, ob.Max.X) - MathF.Max(pb.Min.X, ob.Min.X);
                    float oy = MathF.Min(pb.Max.Y, ob.Max.Y) - MathF.Max(pb.Min.Y, ob.Min.Y);
                    Console.WriteLine(
                        $"[Room] ⚠ spawn '{(string.IsNullOrEmpty(spawnName) ? "(scene default)" : spawnName)}' " +
                        $"overlaps the '{e.Name}' collider ({ox:0.#}x{oy:0.#}px) - physics will push the player off the spawn point. " +
                        $"Move the spawn outside the collider.");
                    return;
                }
            }
        }
    }
}
