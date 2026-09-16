using System;
using System.Collections.Generic;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.UI;

namespace PixelCore.Gameplay.Systems;

public static class PauseMenu
{
    private const float VolumeStep = 0.1f;

    public static Action? ReturnToTitleRequested;

    public static bool Tick(bool canOpen, bool inputAwake)
    {
        if (!inputAwake) return PauseScreen.Active;

        if (!PauseScreen.Active)
        {
            if (canOpen && InputMap.IsPressed(GameAction.Pause)) Open();
            return PauseScreen.Active;
        }

        PauseScreen.Update(Closed);
        return PauseScreen.Active;
    }

    public static void Open()
    {
        PauseScreen.Open("Paused", new List<MenuItem>
        {
            new("Resume", PauseScreen.Close),
            new("Settings", OpenOptions),
            new("Quit to title", () => ReturnToTitleRequested?.Invoke(),
                enabled: ReturnToTitleRequested != null),
        });
    }

    public static void OpenOptions()
    {
        var s = GameSettings.Current;
        PauseScreen.Push("Settings", new List<MenuItem>
        {
            Volume("Master", () => s.MasterVolume, v => s.MasterVolume = v),
            Volume("Music",   () => s.MusicVolume,  v => s.MusicVolume  = v),
            Volume("Sound effects", () => s.SfxVolume,    v => s.SfxVolume    = v),
            Toggle("Fullscreen", () => s.Fullscreen, v => { s.Fullscreen = v; FullscreenRequested?.Invoke(v); }),
            Toggle("Vibration",     () => s.RumbleEnabled, v => s.RumbleEnabled = v),
            new("Back", () => PauseScreen.Back()),
        });
    }

    public static Action<bool>? FullscreenRequested;

    private static MenuItem Volume(string label, Func<float> get, Action<float> set)
        => new(label,
            () => $"{(int)MathF.Round(get() * 100f)}%",
            () => Apply(set, Math.Clamp(get() - VolumeStep, 0f, 1f)),
            () => Apply(set, Math.Clamp(get() + VolumeStep, 0f, 1f)));

    private static MenuItem Toggle(string label, Func<bool> get, Action<bool> set)
        => new(label,
            () => get() ? "On" : "Off",
            () => Apply(set, false),
            () => Apply(set, true));

    private static void Apply<T>(Action<T> set, T value)
    {
        set(value);
        GameSettings.Current.Apply();
        GameSettings.MarkDirty();
    }

    public static void Closed() => GameSettings.SaveIfDirty();
}
