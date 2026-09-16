using System;
using System.Collections.Generic;
using System.Text.Json;

namespace PixelCore.Runtime.Serialization;

public static class FileFormats
{
    public readonly record struct Format(string Name, JsonSerializerOptions Options, Type[] Roots);

    public static IReadOnlyList<Format> All => new[]
    {
        new Format(".scene", SceneSerializer.JsonOptions,
                   new[] { typeof(SceneData) }),

        new Format(".anim", Animation.AnimJsonContext.Default.Options,
                   new[] { typeof(Animation.AnimationData) }),

        new Format(".surface", Audio.SurfaceJsonContext.Default.Options,
                   new[] { typeof(Audio.SurfaceAsset) }),

        new Format(".atlas·assets.json", Assets.AssetJsonContext.Default.Options,
                   new[] { typeof(Assets.SpriteAtlas),
                           typeof(Dictionary<string, Assets.AssetEntry>) }),

        new Format(".post", Assets.PostAsset.JsonOptions,
                   new[] { typeof(Assets.PostAsset) }),

        new Format(".particle", Particles.ParticleAsset.JsonOptions,
                   new[] { typeof(Particles.ParticleAsset) }),

        new Format(".sky", Rendering.SkyAsset.JsonOptions,
                   new[] { typeof(Rendering.SkyAsset) }),

        new Format(".tileset", Tilemap.TilesetTerrainJsonContext.Default.Options,
                   new[] { typeof(Tilemap.TilesetTerrain) }),

        new Format("profiles.json", Rendering.LightingProfiles.JsonOptions,
                   new[] { typeof(Rendering.LightingProfileTable) }),

        new Format("save", Save.SaveJsonContext.Default.Options,
                   new[] { typeof(Save.SaveData) }),

        new Format("settings", Core.SettingsJsonContext.Default.Options,
                   new[] { typeof(Core.GameSettings) }),
    };
}
