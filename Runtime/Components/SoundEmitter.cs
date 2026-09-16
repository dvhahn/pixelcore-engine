using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using PixelCore.Runtime.Audio;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Components;

[ComponentCategory(ComponentCategories.Sound)]
public class SoundEmitter : Component
{
    public string SoundId { get; set; } = "";

    public float Radius { get; set; } = 160f;

    public float Volume { get; set; } = 1f;

    public AudioBus Bus { get; set; } = AudioBus.Ambient;

    public float PanStrength { get; set; } = 1f;

    public float FadeIn { get; set; } = 0.35f;

    public float FadeOut { get; set; } = 0.35f;

    private const float RestartMargin = 0.95f;

    private SoundEffectInstance? _instance;

    private string? _warnedId;

    public bool IsPlaying => _instance != null && !_instance.IsDisposed
                             && _instance.State != SoundState.Stopped;

    public override void Update(float deltaTime)
    {
        if (string.IsNullOrEmpty(SoundId) || Radius <= 0f) { Stop(); return; }

        var transform = Entity?.GetComponent<Transform>();
        if (transform == null) { Stop(); return; }

        var audio = AudioManager.Instance;
        if (!audio.ListenerActive) { Stop(); return; }

        var path = ResolvePath();
        if (path == null) { Stop(); return; }

        var delta = transform.Position - audio.ListenerPosition;
        float dist = delta.Length();

        if (dist >= Radius) { Stop(); return; }

        if (!IsPlaying)
        {
            if (dist > Radius * RestartMargin) return;
            _instance = audio.PlayLoop(path, Bus, volume: 0f, fadeIn: FadeIn, spatial: true);
            if (_instance == null) return;
        }

        audio.SetLoopSpatial(_instance,
                             Attenuation(dist, Radius) * Math.Clamp(Volume, 0f, 1f),
                             PanFor(delta.X, Radius, PanStrength));
    }

    private string? ResolvePath()
    {
        var path = Assets.AssetRegistry.Instance.GetPath(SoundId);
        if (!string.IsNullOrEmpty(path)) return path;

        if (_warnedId != SoundId)
        {
            _warnedId = SoundId;
            Console.Error.WriteLine(
                $"[SoundEmitter] ✘ audio asset id '{SoundId}' is not in the registry " +
                $"({Entity?.Name ?? "?"}) - the file may have been deleted, or assets.json may not " +
                "have been rescanned. Pick the sound again in the inspector.");
        }
        return null;
    }

    internal static float Attenuation(float distance, float radius)
    {
        if (radius <= 0f) return 0f;
        float t = 1f - Math.Clamp(distance / radius, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    internal static float PanFor(float dx, float radius, float strength)
    {
        if (radius <= 0f) return 0f;
        return Math.Clamp(dx / radius, -1f, 1f) * Math.Clamp(strength, 0f, 1f);
    }

    private void Stop()
    {
        if (_instance == null) return;
        AudioManager.Instance.StopLoopInstance(_instance, FadeOut);
        _instance = null;
    }

    public override void OnDisable() => Stop();
    public override void OnDestroy() => Stop();

    public static void StopAllIn(Scene? scene)
    {
        if (scene == null) return;
        foreach (var e in scene.FindEntitiesWithComponent<SoundEmitter>())
            e.GetComponent<SoundEmitter>()?.Stop();
    }
}
