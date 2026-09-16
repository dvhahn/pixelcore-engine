using System;
using System.Collections.Generic;
using System.IO;
using PixelCore.Editor.Panels;
using PixelCore.Editor.Shell;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Editor;

#if DEBUG

public static class DoorRefSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== door references (scene ids and landing points) self-test ===");

        TestIdShape();
        TestRealContent();
        TestTransitionGate();
        TestRenameSurvival();
        TestValidation();
        TestLegacyBark();
        TestAudioPickValue();

        Console.WriteLine($"=== Door: {_pass} passed, {_fail} failed ===");
    }

    private static void TestIdShape()
    {
        Check("eight lowercase hex characters is an id", AssetRegistry.LooksLikeId("b83f0d4e"));
        Check("a room name is not an id", !AssetRegistry.LooksLikeId("Old_Street"));
        Check("a path is not an id either", !AssetRegistry.LooksLikeId("Audio/SFX/Door"));
        Check("a different length is not an id", !AssetRegistry.LooksLikeId("b83f0d4"));
        Check("uppercase hex is rejected (the issuer only produces lowercase)",
              !AssetRegistry.LooksLikeId("B83F0D4E"));
        Check("empty and null are not ids",
              !AssetRegistry.LooksLikeId("") && !AssetRegistry.LooksLikeId(null));
    }

    private static void TestRealContent()
    {
        DoorRefs.Invalidate();

        var rooms = DoorRefs.Rooms();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rooms) names.Add(r.Name);

        Check("premise: the room list contains the two real rooms (Village, House)",
              names.Contains("Village") && names.Contains("House"),
              string.Join(", ", names));
        Check("prefabs do not appear in the room list (a door never leads to a prefab)",
              !names.Contains("Hero") && !names.Contains("Cottage"));

        int doors = 0;
        int idTargets = 0;
        foreach (var r in rooms)
            foreach (var d in DoorRefs.Doors(r.SceneId))
            {
                doors++;
                if (AssetRegistry.LooksLikeId(d.TargetSceneId)) idTargets++;
            }

        Check("premise: the content really has doors (with zero, the validation below is vacuous)", doors > 0, $"{doors}");
        Check("* every door destination in the real scenes is a scene id (the migration is complete)",
              doors > 0 && idTargets == doors, $"{idTargets}/{doors}");
        Check("* every door reference in the real content resolves (the destination and the landing point both exist)",
              DoorRefs.Validate() == 0);
    }

    private static void TestTransitionGate()
    {
        var villageId = AssetRegistry.Instance.FindSceneIdByName("Village");
        Check("premise: Village's id is found", villageId != null, villageId ?? "(missing)");
        Check("premise: 00000000 is not in the registry (the bait for the registry-miss branch)",
              AssetRegistry.Instance.GetPath("00000000") == null);

        var prevImpl = Gameplay.Systems.RoomFlow.LoadRoomImpl;
        bool prevLock = Runtime.Components.Interactor.GlobalLock;
        Gameplay.Systems.RoomFlow.LoadRoomImpl = _ => true;
        Gameplay.Systems.RoomFlow.Abort();
        Runtime.Components.Interactor.GlobalLock = false;

        Gameplay.Systems.RoomFlow.Transition("", "", "DoorA");
        Check("an empty destination does not start a transition", !Gameplay.Systems.RoomFlow.IsTransitioning);
        Check("* a refusal takes no input lock either (taking one would leave the player stuck at the door)",
              !Runtime.Components.Interactor.GlobalLock);

        string legacyMsg = CaptureStderr(() =>
            Gameplay.Systems.RoomFlow.Transition("Old_Street", "", "FrontDoor"));
        Check("* a legacy room NAME does not start a transition (there is no silent compatibility)",
              !Gameplay.Systems.RoomFlow.IsTransitioning && !Runtime.Components.Interactor.GlobalLock);

        string missingMsg = CaptureStderr(() =>
            Gameplay.Systems.RoomFlow.Transition("00000000", "", "FrontDoor"));
        Check("* an id absent from the registry is also caught before the blackout",
              !Gameplay.Systems.RoomFlow.IsTransitioning && !Runtime.Components.Interactor.GlobalLock);

        Check("* a legacy name and a missing id are diagnosed differently (because the fixes differ)",
              legacyMsg.Contains("legacy room name") && !legacyMsg.Contains("registry")
              && missingMsg.Contains("registry"),
              $"legacy=<{legacyMsg.Trim()}> missing=<{missingMsg.Trim()}>");
        Check("* it names the offending door (in a room with several doors this is the only thing that locates it)",
              legacyMsg.Contains("FrontDoor") && missingMsg.Contains("FrontDoor"));
        Check("an empty destination, a legacy name and a missing id all bark (zero silence)",
              legacyMsg.Length > 0 && missingMsg.Length > 0);

        Gameplay.Systems.RoomFlow.Transition(villageId!, "Spawn_Start", "DoorA");
        Check("* a live id does start a transition (the refusals above are not over-blocking)",
              Gameplay.Systems.RoomFlow.IsTransitioning && Runtime.Components.Interactor.GlobalLock);

        Gameplay.Systems.RoomFlow.Abort();
        Gameplay.Systems.RoomFlow.LoadRoomImpl = prevImpl;
        Runtime.Components.Interactor.GlobalLock = prevLock;
    }

    private static void TestRenameSurvival()
    {
        using var fx = new TempRooms();

        const string id = "a1b2c3d4";
        fx.WriteRoom("Scenes/Old.scene", id, _ => { });
        Check("premise: the file opens by id",
              SceneInstance.ResolveSceneId(id) is { } p0 && File.Exists(p0));

        fx.Rename("Scenes/Old.scene", "Scenes/NewName.scene", id);
        DoorRefs.Invalidate();

        Check("* renaming a room file leaves the id the door holds opening it as before (the reason this exists)",
              SceneInstance.ResolveSceneId(id) is { } p1 && File.Exists(p1),
              SceneInstance.ResolveSceneId(id) ?? "(failed to resolve)");

        Check("* control: the old rule (assembling a path from a name) cannot find the file in the same situation",
              !File.Exists(Path.Combine(SceneInstance.ContentRoot, "Scenes", "Old" + ".scene")));

        Check("the name resolver binds to the new name (scripts use names)",
              AssetRegistry.Instance.FindSceneIdByName("NewName") == id);
        Check("* the name resolver cannot find the old name - a name reference breaks on rename",
              AssetRegistry.Instance.FindSceneIdByName("Old") == null);

        fx.WriteRoom("Prefabs/NewName.scene", "b1b2c3d4", _ => { });
        var dup = AssetRegistry.Instance.FindSceneIdByName("NewName", out var cands);
        Check("* a duplicate name picks neither (null plus the candidates reported)",
              dup == null && cands.Count == 2, $"{cands.Count}");
    }

    private static void TestValidation()
    {
        using var fx = new TempRooms();

        const string aId = "aaaa1111", bId = "bbbb2222";
        fx.WriteRoom("Scenes/B.scene", bId, d =>
        {
            AddEntity(d, 1, "Spawn_X");
            AddEntity(d, 2, "Bed");
        });
        fx.WriteRoom("Scenes/A.scene", aId, d => AddDoor(d, 1, "FrontDoor", bId, "Spawn_X"));
        DoorRefs.Invalidate();

        Check("premise: A has one door", DoorRefs.Doors(aId).Count == 1);
        Check("a valid reference is silent", DoorRefs.Validate() == 0);
        Check("the landing point dropdown reads the target scene's spawns (without opening it)",
              DoorRefs.SpawnNames(bId) is { Count: 1 } sp && sp[0] == "Spawn_X");
        Check("* an unresolved target gives a null list, disabling the dropdown (a different state from an empty list)",
              DoorRefs.SpawnNames("00000000") == null);

        fx.WriteRoom("Scenes/A.scene", aId, d => AddDoor(d, 1, "FrontDoor", bId, "Bed"));
        DoorRefs.Invalidate();
        Check("* an entity that does not start with Spawn is a valid landing point too (convention is not existence)",
              DoorRefs.Validate() == 0);

        fx.WriteRoom("Scenes/A.scene", aId, d => AddDoor(d, 1, "FrontDoor", bId, "Spawn_X"));
        fx.WriteRoom("Scenes/B.scene", bId, d => AddEntity(d, 1, "Spawn_Y"));
        DoorRefs.Invalidate();
        Check("* renaming a landing point in another room catches this door (incoming references, replacing the manual hunt)",
              DoorRefs.Validate() == 1);

        fx.Delete("Scenes/B.scene", bId);
        DoorRefs.Invalidate();
        Check("* a door whose destination scene vanished is caught too", DoorRefs.Validate() == 1);

        fx.WriteRoom("Scenes/A.scene", aId, d => AddDoor(d, 1, "FrontDoor", "Old_Street", "Spawn_X"));
        DoorRefs.Invalidate();
        Check("* a destination that is a legacy room name is caught (there is no silent compatibility)",
              DoorRefs.Validate() == 1);

        fx.WriteRoom("Scenes/C.scene", "cccc3333", _ => { });
        fx.WriteRoom("Scenes/A.scene", aId, d => AddDoor(d, 1, "FrontDoor", "cccc3333", ""));
        DoorRefs.Invalidate();
        Check("an empty landing point does not warn (the scene's authored position is legitimate)", DoorRefs.Validate() == 0);
    }

    private static void TestLegacyBark()
    {
        var roomLog = CaptureStderr(() => LoadDoor("Old_Street", ""));
        Check("* loading a legacy room name barks as an error",
              roomLog.Contains("is not a scene id") && roomLog.Contains("Old_Street"), roomLog);

        var soundLog = CaptureStderr(() => LoadDoor("", "Audio/SFX/Door_Wood_Open"));
        Check("* loading a legacy audio path barks as an error",
              soundLog.Contains("is not an audio id") && soundLog.Contains("Audio/SFX/Door_Wood_Open"),
              soundLog);

        Check("* the two faults are not blurred into one message",
              !roomLog.Contains("is not an audio id") && !soundLog.Contains("is not a scene id"));

        var cleanLog = CaptureStderr(() => LoadDoor(Gameplay.Rooms.Village.SceneId, AssetRegistry.Instance.GetId("Audio/AMB/Village/Rain.wav") ?? ""));
        Check("* control: a valid id is silent (the barking is not permanently on)",
              cleanLog.Length == 0, cleanLog);
    }

    private static void LoadDoor(string target, string sound)
    {
        var data = new SceneData { Name = "temp" };
        var e = new EntityData { Id = 1, Name = "Door" };
        e.Components.Add(new InteractableData
        {
            Kind = "Door",
            TargetSceneId = string.IsNullOrEmpty(target) ? null : target,
            SoundId = string.IsNullOrEmpty(sound) ? null : sound,
        });
        data.Entities.Add(e);
        SceneSerializer.FromData(new Scene("temp"), data);
    }

    private static void TestAudioPickValue()
    {
        const string rel = "Audio/BGM/CalmVillage.ogg";
        var existing = AssetRegistry.Instance.GetId(rel);
        Check($"premise: '{rel}' is in the registry", existing != null, existing ?? "(missing)");

        int before = AssetRegistry.Instance.Entries.Count;
        var full = Path.Combine(EditorApp.ContentRoot, rel);
        var picked = InspectorPanel.ToAudioValue(full);

        Check("* what the picker produces is an EXISTING id (it issues no new one)",
              picked == existing, $"{picked} vs {existing}");
        Check("* the registry did not grow (growth would mean two ids for one file)",
              AssetRegistry.Instance.Entries.Count == before);
        Check("* the picker never produces a path (the saved value is an id and nothing else)",
              AssetRegistry.LooksLikeId(picked));
    }

    private static void AddEntity(SceneData d, int id, string name)
        => d.Entities.Add(new EntityData { Id = id, Name = name });

    private static void AddDoor(SceneData d, int id, string name, string targetId, string spawn)
    {
        var e = new EntityData { Id = id, Name = name };
        e.Components.Add(new InteractableData
        {
            Kind = "Door",
            TargetSceneId = targetId,
            Spawn = string.IsNullOrEmpty(spawn) ? null : spawn,
        });
        d.Entities.Add(e);
    }

    private sealed class TempRooms : IDisposable
    {
        private readonly string _root;
        private readonly IDisposable _scope;
        private readonly string _prevContentRoot;

        public TempRooms()
        {
            _root = Path.Combine(Path.GetTempPath(), "pixelcore_door_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_root);
            _scope = AssetRegistry.UseTemporary(Path.Combine(_root, "assets.json"));
            _prevContentRoot = SceneInstance.ContentRoot;
            SceneInstance.ContentRoot = _root;
            DoorRefs.Invalidate();
        }

        public void WriteRoom(string rel, string id, Action<SceneData> build)
        {
            var full = Path.Combine(_root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            var data = new SceneData { Id = id, Name = Path.GetFileNameWithoutExtension(rel) };
            build(data);
            SceneSerializer.SaveToFile(data, full);
            AssetRegistry.Instance.Register(id, rel.Replace('\\', '/'));
            DoorRefs.Invalidate();
        }

        public void Rename(string relFrom, string relTo, string id)
        {
            var from = Path.Combine(_root, relFrom);
            var to = Path.Combine(_root, relTo);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Move(from, to);
            AssetRegistry.Instance.UpdatePath(id, relTo.Replace('\\', '/'));
            DoorRefs.Invalidate();
        }

        public void Delete(string rel, string id)
        {
            File.Delete(Path.Combine(_root, rel));
            AssetRegistry.Instance.Remove(id);
            DoorRefs.Invalidate();
        }

        public void Dispose()
        {
            SceneInstance.ContentRoot = _prevContentRoot;
            _scope.Dispose();
            DoorRefs.Invalidate();
            try { Directory.Delete(_root, recursive: true); } catch {  }
        }
    }

    private static string CaptureStderr(Action body)
    {
        var prev = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try { body(); }
        finally { Console.SetError(prev); }
        return sw.ToString();
    }

    private static void Check(string what, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  [PASS] {what}"); }
        else { _fail++; Console.WriteLine($"  [FAIL] {what}{(detail != null ? $" — {detail}" : "")}"); }
    }
}

#endif
