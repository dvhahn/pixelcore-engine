using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Animation;

public class AnimationData
{
    [System.Text.Json.Serialization.JsonPropertyName("Id")]
    public string Id { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("Name")]
    public string Name { get; set; } = "New Animation";

    [System.Text.Json.Serialization.JsonPropertyName("AtlasId")]
    public string AtlasId { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("AtlasPath")]
    public string AtlasPath { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("Frames")]
    public List<FrameData> Frames { get; set; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("Loop")]
    public bool Loop { get; set; } = true;

    [System.Text.Json.Serialization.JsonIgnore]
    public float TotalDuration
    {
        get
        {
            float total = 0;
            foreach (var frame in Frames)
                total += frame.Duration;
            return total;
        }
    }

    public int GetFrameAtTime(float time)
    {
        if (Frames.Count == 0) return -1;

        float totalDuration = TotalDuration;
        if (totalDuration <= 0) return 0;

        if (Loop && time > totalDuration)
            time %= totalDuration;
        else if (!Loop && time >= totalDuration)
            return Frames.Count - 1;

        float accumulated = 0;
        for (int i = 0; i < Frames.Count; i++)
        {
            accumulated += Frames[i].Duration;
            if (time < accumulated)
                return i;
        }

        return Frames.Count - 1;
    }

    public void Save(string filePath)
    {
        var json = JsonSerializer.Serialize(this, AnimJsonContext.Default.AnimationData);
        AtomicFile.WriteAllText(filePath, json);
    }

    private static readonly HashSet<string> _nameNoted = new();

    public static AnimationData? Load(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            var json = File.ReadAllText(filePath);
            var data = JsonSerializer.Deserialize(json, AnimJsonContext.Default.AnimationData);
            if (data == null) return null;

            var stem = Path.GetFileNameWithoutExtension(filePath);
            if (data.Name != stem)
            {
                if (_nameNoted.Add(filePath))
                    System.Console.WriteLine(
                        $"[AnimationData] in-file name '{data.Name}' is ignored; the clip name is the "
                        + $"file stem '{stem}' ({filePath}). It will be corrected on the next save.");
                data.Name = stem;
            }
            return data;
        }
        catch (System.Exception ex)
        {
            System.Console.WriteLine($"[AnimationData] Load failed: {filePath} — {ex.Message}");
            return null;
        }
    }

    public AnimationClip ToClip(SpriteAtlas atlas)
    {
        var clip = new AnimationClip(Name)
        {
            Texture = atlas.Texture,
            Loop = Loop,
            SourceId = Id,
        };

        bool pivotTaken = false;
        int dropped = 0;

        foreach (var frame in Frames)
        {
            if (frame.SliceIndex < 0 || frame.SliceIndex >= atlas.Slices.Count)
            {
                dropped++;
            }
            else
            {
                var slice = atlas.Slices[frame.SliceIndex];
                clip.AddFrame(new AnimationFrame(
                    new Rectangle(slice.X, slice.Y, slice.Width, slice.Height),
                    frame.Duration,
                    frame.EventName
                ));

                if (!pivotTaken)
                {
                    clip.PivotX = slice.PivotX;
                    clip.PivotY = slice.PivotY;
                    pivotTaken = true;
                }
            }
        }

        if (dropped > 0)
            System.Console.WriteLine($"[AnimationData] ⚠ clip '{Name}': dropped {dropped} frame(s) that fall "
                + $"outside the atlas slice count ({atlas.Slices.Count}) — the sheet layout differs (Id {Id})");

        return clip;
    }
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(AnimationData))]
internal partial class AnimJsonContext : System.Text.Json.Serialization.JsonSerializerContext { }

public class FrameData
{
    [System.Text.Json.Serialization.JsonPropertyName("SliceIndex")]
    public int SliceIndex { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("Duration")]
    public float Duration { get; set; } = 0.1f;

    [System.Text.Json.Serialization.JsonPropertyName("EventName")]
    public string? EventName { get; set; }
}
