using PixelCore.Gameplay.Systems;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Particles;

namespace PixelCore.Gameplay.Village;

public static class Weather
{
    public const string Rain = "Rain";

    public static bool IsRaining => GameFlow.Weather == Rain;

    public static void SetRain(bool on) => GameFlow.Weather = on ? Rain : "";
}

public class RainEmitter : Component
{
    public override void Update(float deltaTime)
    {
        bool on = Weather.IsRaining;

        var particles = Entity.GetComponent<ParticleEmitter>();
        if (particles != null) particles.Emitting = on;

        var sound = Entity.GetComponent<SoundEmitter>();
        if (sound != null && sound.Enabled != on) sound.Enabled = on;
    }
}
