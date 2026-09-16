using System;
using System.Collections.Generic;
using System.IO;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Editor.Shell;

public static class DoorRefs
{
    public readonly record struct RoomEntry(string SceneId, string Name, string RelDir);

    public readonly record struct DoorEntry(string Owner, string TargetSceneId, string Spawn);

    private sealed class SceneInfo
    {
        public DateTime WriteTime;
        public long CheckedAt;
        public readonly List<string> Spawns = new();
        public readonly HashSet<string> EntityNames = new(StringComparer.Ordinal);
        public readonly List<DoorEntry> Doors = new();
    }

    private static readonly Dictionary<string, SceneInfo> _cache = new(StringComparer.Ordinal);

    private const long RecheckMs = 500;

    public static bool IsSpawnName(string name)
        => name.StartsWith("Spawn", StringComparison.OrdinalIgnoreCase);

    public static List<RoomEntry> Rooms()
    {
        var rooms = new List<RoomEntry>();
        foreach (var (id, entry) in AssetRegistry.Instance.Entries)
        {
            if (!entry.Path.EndsWith(".scene", StringComparison.OrdinalIgnoreCase)) continue;

            var full = SceneInstance.ResolveSceneId(id);
            if (full == null || !File.Exists(full)) continue;
            if (Panels.ProjectPanel.PeekKindIsPrefab(full)) continue;

            var rel = entry.Path.Replace('\\', '/');
            rooms.Add(new RoomEntry(id, Path.GetFileNameWithoutExtension(rel),
                                    Path.GetDirectoryName(rel)?.Replace('\\', '/') ?? ""));
        }
        rooms.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return rooms;
    }

    public static string? RoomName(string? sceneId)
    {
        if (string.IsNullOrEmpty(sceneId)) return null;
        var rel = AssetRegistry.Instance.GetPath(sceneId);
        return rel == null ? null : Path.GetFileNameWithoutExtension(rel);
    }

    public static IReadOnlyList<string>? SpawnNames(string? sceneId) => Info(sceneId)?.Spawns;

    public static bool HasEntity(string? sceneId, string name)
    {
        var info = Info(sceneId);
        return info != null && info.EntityNames.Contains(name);
    }

    public static bool CanRead(string? sceneId) => Info(sceneId) != null;

    public static IReadOnlyList<DoorEntry> Doors(string? sceneId)
        => (IReadOnlyList<DoorEntry>?)Info(sceneId)?.Doors ?? Array.Empty<DoorEntry>();

    public static int Validate()
    {
        int problems = 0;

        foreach (var room in Rooms())
        {
            var info = Info(room.SceneId);
            if (info == null) continue;

            foreach (var door in info.Doors)
            {
                string where = $"door '{door.Owner}' in '{room.Name}'";

                if (string.IsNullOrEmpty(door.TargetSceneId))
                {
                    EditorConsole.Add(LogSeverity.Error, $"[Door] ✘ {where}: the destination is empty");
                    problems++;
                    continue;
                }
                if (!AssetRegistry.LooksLikeId(door.TargetSceneId))
                {
                    EditorConsole.Add(LogSeverity.Error,
                        $"[Door] ✘ {where}: the destination '{door.TargetSceneId}' is not a scene id (a legacy room name)");
                    problems++;
                    continue;
                }

                var targetName = RoomName(door.TargetSceneId);
                if (targetName == null)
                {
                    EditorConsole.Add(LogSeverity.Error,
                        $"[Door] ✘ {where}: destination id {door.TargetSceneId} is not in the registry (the scene was deleted)");
                    problems++;
                    continue;
                }

                if (string.IsNullOrEmpty(door.Spawn)) continue;

                if (!CanRead(door.TargetSceneId))
                {
                    EditorConsole.Add(LogSeverity.Warn,
                        $"[Door] ⚠ {where}: could not read the destination file '{targetName}', so the landing point was not checked");
                    problems++;
                }
                else if (!HasEntity(door.TargetSceneId, door.Spawn))
                {
                    EditorConsole.Add(LogSeverity.Warn,
                        $"[Door] ⚠ {where}: landing point '{door.Spawn}' is not in '{targetName}' " +
                        "- it was renamed or deleted. Pick it again in the inspector's Spawn field");
                    problems++;
                }
            }
        }

        return problems;
    }

    public static void Invalidate() => _cache.Clear();

    private static SceneInfo? Info(string? sceneId)
    {
        if (string.IsNullOrEmpty(sceneId)) return null;

        var full = SceneInstance.ResolveSceneId(sceneId);
        if (full == null || !File.Exists(full)) return null;

        long now = Environment.TickCount64;
        bool haveCache = _cache.TryGetValue(sceneId, out var cached);

        if (haveCache && now - cached!.CheckedAt < RecheckMs) return cached;

        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(full); }
        catch { return null; }

        if (haveCache && cached!.WriteTime == stamp)
        {
            cached.CheckedAt = now;
            return cached;
        }

        var data = SceneSerializer.LoadFromFile(full);
        if (data == null) return null;

        var info = new SceneInfo { WriteTime = stamp, CheckedAt = now };
        foreach (var e in data.Entities)
        {
            info.EntityNames.Add(e.Name);
            if (IsSpawnName(e.Name)) info.Spawns.Add(e.Name);

            foreach (var c in e.Components)
                if (c is InteractableData { Kind: "Door" } d)
                    info.Doors.Add(new DoorEntry(e.Name, d.TargetSceneId ?? "", d.Spawn ?? ""));
        }
        info.Spawns.Sort(StringComparer.OrdinalIgnoreCase);

        _cache[sceneId] = info;
        return info;
    }
}
