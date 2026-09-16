using System;
using System.IO;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Gameplay.Systems;

public static class LevelSetup
{
    public const string PlayerPrefabId = "3e9d61a7";

    public const string PlayerRigName = Scene.PlayerName;

    public static RoomRef StartRoom => Rooms.Village;

    public static string? StartRoomPath()
    {
        var rel = AssetRegistry.Instance.GetPath(StartRoom.SceneId);
        if (!string.IsNullOrEmpty(rel)) return Path.Combine(ContentPaths.Root, rel);

        Console.Error.WriteLine($"[Boot] ✘ the start room {StartRoom.Name} ({StartRoom.SceneId}) is not in the registry " +
                                "- the scene was deleted, or assets.json has not been scanned");
        return null;
    }

    public static Entity? LoadStartRoom(Scene scene)
    {
        var path = StartRoomPath();
        if (path == null) return null;

        var data = SceneSerializer.LoadFromFile(path);
        if (data == null)
        {
            Console.Error.WriteLine($"[Boot] ✘ the start room could not be loaded: {path}");
            return null;
        }

        SceneSerializer.FromData(scene, data);
        scene.Name = data.Name;

        var player = scene.FindPlayer();
        if (player == null)
            Console.Error.WriteLine($"[Boot] ✘ the start room {StartRoom.Name} has no '{Scene.PlayerName}' entity");
        return player;
    }

    public static Entity SpawnPlayer(Scene scene, Vector2 position)
    {
        var rig = scene.CreateEntity(Scene.PlayerName);
        rig.GetComponent<Transform>()!.Position = position;

        var inst = rig.AddComponent<SceneInstance>();
        inst.SceneId = PlayerPrefabId;
        inst.Load();
        return rig;
    }

    public static TilemapRenderer? GetTilemap(Scene scene)
        => scene.FindEntity("Tilemap")?.GetComponent<TilemapRenderer>();
}
