namespace PixelCore.Runtime.Audio;

public enum AudioBus
{
    Music,

    SFX,

    Ambient,

    Voice,

    UI,
}

internal static class AudioBusRules
{
    public const int Count = 5;

    public static bool PausesWithGame(AudioBus bus)
        => bus is AudioBus.SFX or AudioBus.Ambient or AudioBus.Voice;
}
