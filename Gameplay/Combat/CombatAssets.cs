using Microsoft.Xna.Framework;
using PixelCore.Runtime.Audio;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;

namespace PixelCore.Gameplay.Combat;

public static class CombatAssets
{
    public const string SlimePrefabId = "51a3e0c4";

    public const string SlashFxPrefabId = "f7a2c913";

    public const string SmokeFxPrefabId = "0d6b8e25";

    public const string HeartTexture = "Sprites/UI/Heart.png";

    public const string SwingSound = "Audio/SFX/Combat/Swing";

    public const string HitSound = "Audio/SFX/Combat/Hit";

    public const string HurtSound = "Audio/SFX/Combat/Hurt";

    public const string PopSound = "Audio/SFX/Combat/Pop";

    public const string FxName = "Fx";

    public static Entity SpawnFx(Scene scene, string prefabId, Vector2 position, string? clip = null)
    {
        var fx = scene.CreateEntity(FxName);
        fx.HideFromSerialization = true;
        fx.GetComponent<Transform>()!.Position = position;

        var instance = fx.AddComponent<SceneInstance>();
        instance.SceneId = prefabId;
        instance.Load();

        if (clip != null && fx.GetComponent<Animator>() is { } animator && animator.HasClip(clip))
            animator.Play(clip, restart: true);
        return fx;
    }

    public static void Play(string sound) => AudioManager.Instance.PlaySFX(sound);
}
