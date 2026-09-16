using System;
using System.Collections.Generic;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Runtime.Cutscenes;

public static class CutsceneRegression
{
    public static bool Enabled { get; } =
        Environment.GetEnvironmentVariable("PIXELCORE_CUTSCENE_ALL") == "1";

    public static bool ExitRequested { get; private set; }

    public static int ExitCode { get; private set; }

    public static float TimeoutSeconds { get; set; } = 20f;

    public static float WarmupSeconds { get; set; } = 0.5f;

    internal readonly record struct StartRoom(string SceneId, string Spawn, string Source);

    private static bool _started, _done;
    private static int _index = -1;
    private static float _elapsed;
    private static float _warmup;
    private static readonly List<string> _problems = new();
    private static int _played;
    private static int _gradeMark;

    private static Dictionary<string, StartRoom> _startRooms = new(StringComparer.Ordinal);

    private static readonly List<string> _unknownEvents = new();
    private static bool _awaitingRoom;
    private static float _roomWarmup;
    private static string? _pendingId;

    public static void Update(float unscaledDt, string sceneName)
    {
        if (!Enabled || _done) return;

        if (!_started)
        {
            _warmup += unscaledDt;
            if (_warmup < WarmupSeconds) return;

            _started = true;
            CutsceneDirector.Finished += OnFinished;
            CutsceneDirector.SuppressTriggers = true;
            _startRooms = BuildStartRooms(_problems);
            CollectUnknownEvents();
            CheckWiring();
            Console.WriteLine($"=== playing every cutscene ({CutsceneDirector.Ids.Count}) ===");
        }

        if (CutsceneDirector.IsPlaying)
        {
            _elapsed += unscaledDt;
            CutsceneDirector.RequestSkip();

            if (_elapsed > TimeoutSeconds)
            {
                _problems.Add($"{CutsceneDirector.CurrentId} - did not finish within {TimeoutSeconds:0}s (deadlock?)");
                CutsceneDirector.Stop();
            }
            return;
        }

        if (_awaitingRoom)
        {
            _roomWarmup += unscaledDt;
            if (_roomWarmup < WarmupSeconds) return;

            _awaitingRoom = false;
            _elapsed = 0f;
            _gradeMark = CutsceneLog.SkipCount + CutsceneLog.HaltCount;
            if (!CutsceneDirector.Play(_pendingId!))
                _problems.Add($"{_pendingId} - playback itself was refused");
            return;
        }

        _index++;
        if (_index >= CutsceneDirector.Ids.Count) { Report(); return; }
        Begin(CutsceneDirector.Ids[_index]);
    }

    private static void Begin(string id)
    {
        int n = CutsceneDirector.Ids.Count;

        if (!_startRooms.TryGetValue(id, out var start))
        {
            Console.WriteLine($"--- [{_index + 1}/{n}] {id} - skipped (no start room)");
            _problems.Add($"{id} - the start room could not be decided (no trigger point in any scene). "
                        + "Place a trigger or interactable in a scene, or declare it with Register(id, body, room:, spawn:)");
            return;
        }

        var roomName = SceneDisplayName(start.SceneId);
        Console.WriteLine($"--- [{_index + 1}/{n}] {id} @ {roomName} ({start.Source})");

        if (CutsceneDirector.RoomLoader == null)
        {
            _problems.Add($"{id} - no room loader was injected (CutsceneDirector.RoomLoader)");
            return;
        }

        if (!CutsceneDirector.RoomLoader(start.SceneId, start.Spawn))
        {
            _problems.Add($"{id} - could not open the start room: {roomName} (spawn '{start.Spawn}')");
            return;
        }

        _pendingId = id;
        _awaitingRoom = true;
        _roomWarmup = 0f;
    }

    private static void CheckWiring()
    {
        if (CutsceneDirector.RoomLoader == null)
            _problems.Add("wiring - no room loader was injected (CutsceneDirector.RoomLoader)");

        if (CutsceneDirector.PersistFacing == null)
            _problems.Add("wiring - no persistent facing hook was injected (CutsceneDirector.PersistFacing) "
                        + "- c.Face and c.Walk cannot leave a facing, and it reverts on the first frame after the cutscene ends");
    }

    private static void CollectUnknownEvents()
    {
        _unknownEvents.Clear();
        _unknownEvents.AddRange(UnknownEventNames(_startRooms));
    }

    internal static List<string> UnknownEventNames(IReadOnlyDictionary<string, StartRoom> rooms)
    {
        var found = new List<string>();
        foreach (var (id, start) in rooms)
        {
            if (CutsceneDirector.IsRegistered(id)) continue;
            if (Systems.GameActions.IsRegistered(id)) continue;
            found.Add($"{SceneDisplayName(start.SceneId)} '{start.Spawn}' → {id}");
        }
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    internal static Dictionary<string, StartRoom> BuildStartRooms(List<string> problems)
    {
        var found = new Dictionary<string, List<(string SceneId, string Spawn, string Room, string Source)>>(StringComparer.Ordinal);

        foreach (var (sceneId, entry) in AssetRegistry.Instance.Entries)
        {
            if (!entry.Path.EndsWith(".scene", StringComparison.OrdinalIgnoreCase)) continue;

            var path = SceneInstance.ResolveSceneId(sceneId);
            if (path == null) continue;
            var data = SceneSerializer.LoadFromFile(path);
            if (data == null) continue;
            if (data.Kind == Core.SceneKind.Prefab) continue;

            var room = SceneDisplayName(sceneId);
            foreach (var e in data.Entities)
                foreach (var comp in e.Components)
                {
                    string? id = comp switch
                    {
                        CutsceneTriggerData t when !string.IsNullOrEmpty(t.CutsceneId) => t.CutsceneId,
                        InteractableData it when it.Kind == "Event" && !string.IsNullOrEmpty(it.Event) => it.Event,
                        _ => null,
                    };
                    if (id == null) continue;

                    if (!found.TryGetValue(id, out var list)) found[id] = list = new();
                    list.Add((sceneId, e.Name, room, comp is CutsceneTriggerData ? "trigger" : "interactable"));
                }
        }

        var result = new Dictionary<string, StartRoom>(StringComparer.Ordinal);
        foreach (var (id, list) in found)
        {
            if (list.Count > 1)
            {
                var where = new List<string>();
                foreach (var c in list) where.Add($"{c.Room}/{c.Spawn}");
                where.Sort(StringComparer.Ordinal);
                problems.Add($"{id} - the start room is ambiguous ({list.Count} places): {string.Join(", ", where)}");
                continue;
            }
            result[id] = new StartRoom(list[0].SceneId, list[0].Spawn, list[0].Source);
        }

        foreach (var id in CutsceneDirector.Ids)
        {
            if (result.ContainsKey(id) || found.ContainsKey(id)) continue;
            if (CutsceneDirector.DeclaredRoom(id) is not { } d) continue;

            var sceneId = AssetRegistry.Instance.FindSceneIdByName(d.Room, out var candidates);
            if (sceneId == null)
            {
                problems.Add($"{id} - the declared start room '{d.Room}' was not found"
                           + (candidates.Count > 1 ? $" ({candidates.Count} with the same name)" : ""));
                continue;
            }
            result[id] = new StartRoom(sceneId, d.Spawn, "declared");
        }

        return result;
    }

    private static string SceneDisplayName(string sceneId)
    {
        var p = AssetRegistry.Instance.GetPath(sceneId);
        if (string.IsNullOrEmpty(p)) return sceneId;
        int slash = p.LastIndexOfAny(new[] { '/', '\\' });
        int dot = p.LastIndexOf('.');
        if (dot <= slash) dot = p.Length;
        return p.Substring(slash + 1, dot - slash - 1);
    }

    private static void OnFinished(string id, bool completed)
    {
        _played++;
        if (!completed) _problems.Add($"{id} - did not run to completion (aborted)");

        int grades = CutsceneLog.SkipCount + CutsceneLog.HaltCount - _gradeMark;
        if (grades > 0) _problems.Add($"{id} - {grades} grade-2/3 events (see the log above)");
    }

    private static void Report()
    {
        _done = true;
        CutsceneDirector.Finished -= OnFinished;
        CutsceneDirector.SuppressTriggers = false;

        Console.WriteLine($"=== cutscene sweep result: {_played} played, {_problems.Count} problems ===");
        foreach (var p in _problems) Console.WriteLine($"  ✗ {p}");
        if (_problems.Count == 0) Console.WriteLine("  ✓ all passed");

        Console.WriteLine(_unknownEvents.Count == 0
            ? "⚠ 0 unregistered Event names"
            : $"⚠ {_unknownEvents.Count} unregistered Event names:");
        foreach (var u in _unknownEvents) Console.WriteLine($"    {u}");

        ExitCode = _problems.Count;
        ExitRequested = true;
    }
}
