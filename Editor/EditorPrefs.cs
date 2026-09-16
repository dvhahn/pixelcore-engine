using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PixelCore.Runtime.Core;

namespace PixelCore.Editor;

public class EditorPrefs
{
    private const string PrefsPath = "editor.prefs.json";

    public string? LastScenePath { get; set; }

    public string? Theme { get; set; }

    public bool DebugCone { get; set; } = true;
    public bool DebugDeadzone { get; set; }
    public bool DebugSortLines { get; set; }
    public bool DebugFrameGraph { get; set; } = true;
    public bool DebugAudio { get; set; }
    public bool DebugCameraTuning { get; set; }
    public bool DebugNav { get; set; }

    public float AudioMaster { get; set; } = 1f;

    public float[]? AudioBuses { get; set; }

    public bool AudioMuted { get; set; }

    public bool GridVisible { get; set; } = true;

    public bool CollidersVisible { get; set; }

    public bool GizmosVisible { get; set; } = true;

    public bool IconMonochrome { get; set; } = true;

    public float SideSplitRatio { get; set; } = 0.42f;

    public bool ConsoleOpen { get; set; }

    public float ConsoleHeight { get; set; } = 190f;

    public Dictionary<string, SceneViewState> SceneViews { get; set; } = new();

    public List<string>? OpenAssetFolders { get; set; }

    public int WindowX { get; set; }
    public int WindowY { get; set; }
    public int WindowW { get; set; }
    public int WindowH { get; set; }

    public static EditorPrefs Load()
    {
        try
        {
            if (File.Exists(PrefsPath))
            {
                var json = File.ReadAllText(PrefsPath);
                return JsonSerializer.Deserialize(json, PrefsJsonContext.Default.EditorPrefs) ?? new EditorPrefs();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EditorPrefs] Load failed: {ex.Message}");
        }
        return new EditorPrefs();
    }

    public void Save()
    {
        try
        {
            PruneSceneViews();
            var json = JsonSerializer.Serialize(this, PrefsJsonContext.Default.EditorPrefs);
            AtomicFile.WriteAllText(PrefsPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EditorPrefs] Save failed: {ex.Message}");
        }
    }

    private void PruneSceneViews()
    {
        if (SceneViews.Count == 0) return;
        foreach (var key in new List<string>(SceneViews.Keys))
            if (!File.Exists(key)) SceneViews.Remove(key);
    }
}

public class SceneViewState
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Zoom { get; set; } = 1f;
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(EditorPrefs))]
[JsonSerializable(typeof(Dictionary<string, SceneViewState>))]
internal partial class PrefsJsonContext : JsonSerializerContext { }
