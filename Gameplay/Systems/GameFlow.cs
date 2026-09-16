using System;
using System.Collections.Generic;
using System.IO;
using PixelCore.Gameplay.Player;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Rendering;

namespace PixelCore.Gameplay.Systems;

public enum DayPhase
{
    Morning,
    Afternoon,
    Evening,
}

public static class GameFlow
{
    private static GameContext? _ctx;
    private static Camera? _camera;

    public static GameContext? Context => _ctx;

    public static int Day { get; private set; } = 1;
    public static DayPhase Phase { get; private set; } = DayPhase.Morning;

    public static string PhaseName => Phase.ToString();

    public static event Action? DayStarted;

    private static readonly Dictionary<DayPhase, float> PhaseHours = new()
    {
        [DayPhase.Morning] = 8f,
        [DayPhase.Afternoon] = 14f,
        [DayPhase.Evening] = 19f,
    };

    private static readonly Dictionary<DayPhase, (float Start, float End)> PhaseWindows = new()
    {
        [DayPhase.Morning] = (8f, 13f),
        [DayPhase.Afternoon] = (14f, 18f),
        [DayPhase.Evening] = (19f, 22f),
    };

    public static (float Start, float End) CurrentPhaseWindow =>
        PhaseWindows.TryGetValue(Phase, out var w) ? w : (0f, 24f);

    public static void SlideHour(float hour)
    {
        if (_ctx == null) return;
        var (start, end) = CurrentPhaseWindow;
        _ctx.Clock.SetHourClamped(hour, start, end);
    }

    private static readonly Dictionary<DayPhase, string> PhaseTags = new()
    {
        [DayPhase.Morning] = "Day",
        [DayPhase.Afternoon] = "Day",
        [DayPhase.Evening] = "Dusk",
    };

    public static string CurrentLightingTag()
    {
        var tag = PhaseTags.TryGetValue(Phase, out var t) ? t : "Day";
        return string.IsNullOrEmpty(Weather) ? tag : tag + "." + Weather;
    }

    public static string Weather { get; set; } = "";

    private static readonly Dictionary<(int Day, DayPhase Phase), (RoomRef Room, string Spawn)> StartRooms = new()
    {
        [(1, DayPhase.Morning)] = (Rooms.Village, "Spawn_Start"),
        [(1, DayPhase.Afternoon)] = (Rooms.Village, "Spawn_Start"),
        [(1, DayPhase.Evening)] = (Rooms.Village, "Spawn_Start"),
    };

    public static string Costume { get; set; } = "default";

    public static string LastSpawn => RoomFlow.LastSpawn;

    public static void Bind(GameContext ctx, Camera camera)
    {
        _ctx = ctx;
        _camera = camera;

        Flags.TouchAll();

        ctx.FlagExpiry.Bind(ctx.Blackboard, () => (Day, PhaseName));

        ctx.Save.Bind(ctx);
        BindSave(ctx.Save);
    }

    private static void BindSave(Runtime.Save.SaveSystem save)
    {
        save.CaptureGameState = data =>
        {
            var (sceneId, sceneName) = CurrentRoom();
            data.Room = new Runtime.Save.RoomSave
            {
                SceneId = sceneId,
                Spawn = LastSpawn,
                SceneNameHint = sceneName,
            };
            data.Time = new Runtime.Save.TimeSave { Day = Day, Phase = PhaseName };
            data.Player = new Runtime.Save.PlayerSave
            {
                Facing = FacingName(PlayerController.Of(_ctx?.Scene)?.Facing
                                    ?? new Microsoft.Xna.Framework.Vector2(0f, 1f)),
                Costume = Costume,
            };
        };

        save.ApplyGameState = data =>
        {
            if (!TryParsePhase(data.Time.Phase, out var phase)) return false;

            if (string.IsNullOrEmpty(data.Room.SceneId))
                Console.Error.WriteLine(
                    $"[Save] ✘ a save stored with no room ('{(string.IsNullOrEmpty(data.Room.SceneNameHint) ? "no name either" : data.Room.SceneNameHint)}') " +
                    "- starting in the default room. Check whether a [Flow] line appeared at save time");

            if (!StartDay(data.Time.Day, phase, data.Room.SceneId, data.Room.Spawn)) return false;

            var pc = PlayerController.Of(_ctx?.Scene);
            if (pc != null) pc.Facing = ParseFacing(data.Player.Facing);
            else Console.WriteLine("[Save] ⚠ could not restore the facing - the scene has no PlayerController");
            Costume = string.IsNullOrEmpty(data.Player.Costume) ? "default" : data.Player.Costume;
            return true;
        };

        save.GameBusyReason = () => RoomFlow.IsTransitioning ? "a room transition is in progress" : null;
    }

    private static string FacingName(Microsoft.Xna.Framework.Vector2 f)
        => f.X < 0f ? "Left" : f.X > 0f ? "Right" : f.Y < 0f ? "Up" : "Down";

    private static Microsoft.Xna.Framework.Vector2 ParseFacing(string name)
    {
        if (string.IsNullOrEmpty(name)) return new Microsoft.Xna.Framework.Vector2(0f, 1f);
        if (Runtime.Cutscenes.Dirs.TryParse(name, out var dir))
            return Runtime.Cutscenes.Dirs.Vector(dir);
        return WarnUnknownFacing(name);
    }

    private static Microsoft.Xna.Framework.Vector2 WarnUnknownFacing(string name)
    {
        Console.WriteLine($"[Flow] ⚠ unknown facing '{name}' - facing down");
        return new Microsoft.Xna.Framework.Vector2(0f, 1f);
    }

    public static bool StartDay(int day, DayPhase phase, string? roomSceneId = null, string? spawn = null)
    {
        if (_ctx == null || _camera == null)
        {
            Console.WriteLine("[Flow] ✘ StartDay was called before GameFlow.Bind");
            return false;
        }

        var (prevDay, prevPhase) = (Day, Phase);

        Day = day;
        Phase = phase;

        ResetRuntimeStatics();
        ReleaseLocks();

        _ctx.Clock.Hour = PhaseHours.TryGetValue(phase, out var hour) ? hour : 12f;

        _ctx.FlagExpiry.ExpireFor(day, PhaseName);

        string sceneId, spawnName;
        if (!string.IsNullOrEmpty(roomSceneId))
        {
            sceneId = roomSceneId;
            spawnName = spawn ?? "";
        }
        else if (StartRooms.TryGetValue((day, phase), out var start))
        {
            sceneId = start.Room.SceneId;
            spawnName = start.Spawn;
        }
        else
        {
            Console.WriteLine($"[Flow] ✘ no start room in the table for day {day} {phase} " +
                              "- add one to GameFlow.StartRooms (there is no fallback default)");
            (Day, Phase) = (prevDay, prevPhase);
            return false;
        }

        if (!EnterRoom(sceneId, spawnName)) { (Day, Phase) = (prevDay, prevPhase); return false; }

        Console.WriteLine($"[Flow] day {day} {phase} start - room {sceneId} / spawn {(string.IsNullOrEmpty(spawnName) ? "-" : spawnName)}");
        DayStarted?.Invoke();
        return true;
    }

    public static bool AdvancePhase()
    {
        return Phase switch
        {
            DayPhase.Morning => StartDay(Day, DayPhase.Afternoon),
            DayPhase.Afternoon => StartDay(Day, DayPhase.Evening),
            _ => StartDay(Day + 1, DayPhase.Morning),
        };
    }

    public static bool TryParsePhase(string name, out DayPhase phase)
    {
        if (Enum.TryParse(name, ignoreCase: false, out phase) && Enum.IsDefined(phase)) return true;

        Console.WriteLine($"[Flow] ✘ unknown phase '{name}' - this build knows: {string.Join(", ", Enum.GetNames<DayPhase>())}");
        phase = DayPhase.Morning;
        return false;
    }

    private static bool EnterRoom(string sceneId, string spawnName)
    {
        string? path = AssetRegistry.Instance.GetPath(sceneId);
        if (string.IsNullOrEmpty(path))
        {
            Console.WriteLine($"[Flow] ✘ scene asset id '{sceneId}' is not in the registry " +
                              "- the scene was deleted, or assets.json has not been scanned");
            return false;
        }

        if (RoomFlow.LoadRoomImpl == null)
        {
            Console.WriteLine("[Flow] ✘ no scene loader was injected (RoomFlow.LoadRoomImpl)");
            return false;
        }
        if (!RoomFlow.LoadRoomImpl(sceneId))
        {
            Console.Error.WriteLine($"[Flow] ✘ room load failed: id {sceneId} (path {path})");
            return false;
        }

        RoomFlow.RoomChangedDuringPlay = true;

        RoomFlow.PlaceAtSpawn(_ctx!.Scene, _camera!, spawnName);
        return true;
    }

    private static void ResetRuntimeStatics()
    {
        Runtime.Cutscenes.CutsceneDirector.Abort();

        Runtime.Systems.InteractionHighlight.Clear();

        Runtime.Rendering.LightingProfiles.ResetRuntime();

        Runtime.Rendering.FxStack.ResetAll();

        Weather = "";

        Runtime.Story.DialogueRunner.Stop();

        Runtime.UI.MonologueScreen.ResetAll();

        Runtime.UI.DialogueBox.FastForward = false;

        Runtime.Story.DialogueMarkup.ResetWarnings();

        Runtime.UI.DialogueBubble.ResetWarnings();
        Runtime.UI.DialogueBubble.ResetSkinWarning();

        Runtime.UI.BubblePool.ResetAll();

        Runtime.Particles.ParticleEmitter.ResetWarnings();

        Runtime.Rendering.PostProcessor.ResetLutWarnings();
        Runtime.Serialization.PostData.ResetMovedWarnings();

        _missingRoomWarned.Clear();

        Runtime.Core.Rumble.StopAll();

        Runtime.Systems.InteractionPrompt.Enabled = true;

        _ctx?.Scene?.ResetAmbient();

        Runtime.UI.TitleScreen.ResetAll();

        Runtime.UI.PauseScreen.ResetAll();

        Costume = "default";
    }

    private static void ReleaseLocks()
    {
        RoomFlow.Abort();
        Runtime.Cutscenes.FreezeState.ResetAll();
        Runtime.UI.DialogueBox.Close();
        Interactor.GlobalLock = false;
        ScreenFader.Set(0f);
    }

    public static (string SceneId, string Name) CurrentRoom()
    {
        string name = _ctx?.Scene?.Name ?? "";
        if (string.IsNullOrEmpty(name)) return ("", "");

        string? id = AssetRegistry.Instance.FindSceneIdByName(name, out var candidates);
        if (id == null && _missingRoomWarned.Add(name))
        {
            Console.Error.WriteLine(candidates.Count > 1
                ? $"[Flow] ✘ scene name '{name}' matches {candidates.Count} files - the save cannot identify the room"
                : $"[Flow] ✘ no scene named '{name}' in the registry - the save will be stored with no room " +
                  "(the file name and the scene name diverged, or assets.json has not been scanned)");
        }
        return (id ?? "", name);
    }

    private static readonly HashSet<string> _missingRoomWarned = new();

#if DEBUG
    internal static void DebugSetTime(int day, DayPhase phase)
    {
        Day = day;
        Phase = phase;
    }
#endif
}
