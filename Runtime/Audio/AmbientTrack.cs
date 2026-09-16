using System;

namespace PixelCore.Runtime.Audio;

public class AmbientTrack
{
    public string SoundId { get; set; } = "";

    public float Volume { get; set; } = 1f;

    private string? _warnedId;

    public AmbientTrack() { }

    public AmbientTrack(string soundId, float volume = 1f)
    {
        SoundId = soundId;
        Volume = volume;
    }

    public string? ResolvePath()
    {
        if (string.IsNullOrEmpty(SoundId)) return null;

        var path = Assets.AssetRegistry.Instance.GetPath(SoundId);
        if (!string.IsNullOrEmpty(path)) return path;

        if (_warnedId != SoundId)
        {
            _warnedId = SoundId;
            Console.Error.WriteLine(
                $"[Ambient] ✘ audio asset id '{SoundId}' not found in the registry - " +
                "the file was deleted, or assets.json has not been scanned. Pick it again in the inspector");
        }
        return null;
    }
}
