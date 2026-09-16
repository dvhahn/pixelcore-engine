using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PixelCore.Runtime.Save;

public class SaveData
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = SaveSchema.CurrentVersion;

    [JsonPropertyName("lastAppliedFix")]
    public int LastAppliedFix { get; set; }

    [JsonPropertyName("checkpoint")]
    public string Checkpoint { get; set; } = "";

    [JsonPropertyName("savedAtUtc")]
    public string SavedAtUtc { get; set; } = "";

    [JsonPropertyName("room")]
    public RoomSave Room { get; set; } = new();
    [JsonPropertyName("time")]
    public TimeSave Time { get; set; } = new();
    [JsonPropertyName("player")]
    public PlayerSave Player { get; set; } = new();
    [JsonPropertyName("flags")]
    public FlagsSave Flags { get; set; } = new();

    [JsonPropertyName("expiring")]
    public List<ExpiringFlagSave> Expiring { get; set; } = new();

    [JsonPropertyName("stats")]
    public StatsSave Stats { get; set; } = new();
}

public class RoomSave
{
    [JsonPropertyName("sceneId")]
    public string SceneId { get; set; } = "";

    [JsonPropertyName("spawn")]
    public string Spawn { get; set; } = "";

    [JsonPropertyName("sceneNameHint")]
    public string SceneNameHint { get; set; } = "";
}

public class TimeSave
{
    [JsonPropertyName("day")]
    public int Day { get; set; } = 1;

    [JsonPropertyName("phase")]
    public string Phase { get; set; } = "";
}

public class PlayerSave
{
    [JsonPropertyName("facing")]
    public string Facing { get; set; } = "";

    [JsonPropertyName("costume")]
    public string Costume { get; set; } = "";
}

public class FlagsSave
{
    [JsonPropertyName("bools")]
    public Dictionary<string, bool> Bools { get; set; } = new();
    [JsonPropertyName("ints")]
    public Dictionary<string, int> Ints { get; set; } = new();
    [JsonPropertyName("floats")]
    public Dictionary<string, float> Floats { get; set; } = new();
    [JsonPropertyName("strings")]
    public Dictionary<string, string> Strings { get; set; } = new();

    [JsonIgnore]
    public int Count => Bools.Count + Ints.Count + Floats.Count + Strings.Count;
}

public class ExpiringFlagSave
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "";

    [JsonPropertyName("day")]
    public int Day { get; set; }

    [JsonPropertyName("phase")]
    public string Phase { get; set; } = "";
}

public class StatsSave
{
    [JsonPropertyName("playSeconds")]
    public double PlaySeconds { get; set; }

    [JsonPropertyName("achievements")]
    public List<string> Achievements { get; set; } = new();
}

public static class SaveSchema
{
    public const int CurrentVersion = 1;
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SaveData))]
internal partial class SaveJsonContext : JsonSerializerContext { }
