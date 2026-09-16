using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PixelCore.Runtime.Audio;

namespace PixelCore.Runtime.Core;

public sealed class GameSettings
{
    [JsonPropertyName("masterVolume")]
    public float MasterVolume { get; set; } = 1f;
    [JsonPropertyName("musicVolume")]
    public float MusicVolume { get; set; } = 1f;
    [JsonPropertyName("sfxVolume")]
    public float SfxVolume { get; set; } = 1f;
    [JsonPropertyName("fullscreen")]
    public bool Fullscreen { get; set; } = true;
    [JsonPropertyName("rumbleEnabled")]
    public bool RumbleEnabled { get; set; } = true;

    [JsonPropertyName("highlight")]
    public Components.InteractableHighlight Highlight { get; set; }
        = Components.InteractableHighlight.None;

    private const string FileName = "settings.json";

    public static string PathOf() => Path.Combine(Runtime.Save.SaveFile.Dir, FileName);

    private static GameSettings? _current;

    public static GameSettings Current => _current ??= Load();

    private static bool _dirty;

    public static void MarkDirty() => _dirty = true;

    public static GameSettings Load()
    {
        try
        {
            var path = PathOf();
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJsonContext.Default.GameSettings);
                if (s != null) { s.Clamp(); return s; }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Settings] could not read settings - starting with defaults: {ex.Message}");
        }
        return new GameSettings();
    }

    public static void SaveIfDirty()
    {
        if (!_dirty) return;
        _dirty = false;
        try
        {
            Current.Clamp();
            Directory.CreateDirectory(Runtime.Save.SaveFile.Dir);
            AtomicFile.WriteAllText(PathOf(),
                JsonSerializer.Serialize(Current, SettingsJsonContext.Default.GameSettings));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Settings] could not write settings: {ex.Message}");
        }
    }

    private void Clamp()
    {
        MasterVolume = Math.Clamp(MasterVolume, 0f, 1f);
        MusicVolume = Math.Clamp(MusicVolume, 0f, 1f);
        SfxVolume = Math.Clamp(SfxVolume, 0f, 1f);
    }

    public void Apply()
    {
        var audio = AudioManager.Instance;
        audio.MasterVolume = MasterVolume;
        audio.SetBusVolume(AudioBus.Music, MusicVolume);
        audio.SetBusVolume(AudioBus.SFX, SfxVolume);
        audio.SetBusVolume(AudioBus.Ambient, SfxVolume);
        audio.SetBusVolume(AudioBus.UI, SfxVolume);
        Core.Rumble.Enabled = RumbleEnabled;
        Systems.InteractionHighlight.Mode = Highlight;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
                             UseStringEnumConverter = true)]
[JsonSerializable(typeof(GameSettings))]
public partial class SettingsJsonContext : JsonSerializerContext { }
