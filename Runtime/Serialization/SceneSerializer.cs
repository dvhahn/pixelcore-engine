using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Tilemap;

namespace PixelCore.Runtime.Serialization;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SceneData))]
[JsonSerializable(typeof(ComponentData))]
[JsonSerializable(typeof(System.Text.Json.Nodes.JsonObject))]
internal partial class SceneJsonContext : JsonSerializerContext { }

public static class SceneSerializer
{
    internal static readonly JsonSerializerOptions JsonOptions =
        new(SceneJsonContext.Default.Options)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

    private static System.Text.Json.Serialization.Metadata.JsonTypeInfo<SceneData> SceneTypeInfo
        => (System.Text.Json.Serialization.Metadata.JsonTypeInfo<SceneData>)
           JsonOptions.GetTypeInfo(typeof(SceneData));

    internal static System.Text.Json.Serialization.Metadata.JsonTypeInfo<System.Text.Json.Nodes.JsonObject> NodeTypeInfo
        => (System.Text.Json.Serialization.Metadata.JsonTypeInfo<System.Text.Json.Nodes.JsonObject>)
           JsonOptions.GetTypeInfo(typeof(System.Text.Json.Nodes.JsonObject));

    public static SceneData ToData(Scene scene)
    {
        var data = new SceneData
        {
            SchemaVersion = SceneData.CurrentSchemaVersion,
            Name = scene.Name,
            Kind = scene.Kind,
            LightingEnabled = scene.LightingEnabled,
            AmbientR = scene.AmbientLight.R,
            AmbientG = scene.AmbientLight.G,
            AmbientB = scene.AmbientLight.B,
            BackR = scene.BackColor.R,
            BackG = scene.BackColor.G,
            BackB = scene.BackColor.B,
            Exterior = scene.Exterior,
            ViewZoom = scene.ViewZoom,
            CameraFixed = scene.CameraFixed,
            CameraFixedX = scene.CameraFixedPos.X,
            CameraFixedY = scene.CameraFixedPos.Y,
            CameraBoundsEnabled = scene.CameraBoundsEnabled,
            CameraDampingEnabled = scene.CameraDampingEnabled,
            CameraDampingX = scene.CameraDamping.X,
            CameraDampingY = scene.CameraDamping.Y,
            CameraBoundsX = scene.CameraBounds.X,
            CameraBoundsY = scene.CameraBounds.Y,
            CameraBoundsW = scene.CameraBounds.Width,
            CameraBoundsH = scene.CameraBounds.Height,
            Post = scene.Post.IsNeutral ? null : PostData.From(scene.Post),
            PostPresets = scene.PostPresets.Count == 0
                ? null
                : scene.PostPresets.ConvertAll(p => new PostPresetData
                {
                    Key = p.Key,
                    AssetId = p.IsShared ? p.AssetId : null,
                    Profile = p.IsShared ? null : PostData.From(p.Profile),
                }),
            ActivePost = scene.ActivePostKey,
            DefaultSurface = string.IsNullOrEmpty(scene.DefaultSurfaceId) ? null : scene.DefaultSurfaceId,
            Sky = scene.SkyId,
            Ambients = scene.Ambients.Count == 0
                ? null
                : scene.Ambients.ConvertAll(t => new AmbientTrackData { SoundId = t.SoundId, Volume = t.Volume }),
        };

        var ordered = new List<Entity>();
        void Walk(Entity e) { ordered.Add(e); foreach (var c in e.Children) Walk(c); }
        foreach (var root in scene.Entities)
            if (root.IsRoot) Walk(root);

        scene.NextSerializedId = AssignSerializedIds(ordered, scene.NextSerializedId);
        data.NextEntityId = scene.NextSerializedId;

        foreach (var entity in ordered)
        {
            if (entity.HideFromSerialization) continue;

            var tilemap = entity.GetComponent<TilemapRenderer>();
            if (tilemap != null)
            {
                data.TilemapActive = entity.Active;
                foreach (var layer in tilemap.Layers)
                    data.TilemapLayers.Add(CaptureTilemapLayer(layer, tilemap.TileSize));
                continue;
            }

            data.Entities.Add(new EntityData
            {
                Id = entity.SerializedId,
                ParentId = SerializedParentId(entity),
                Name = entity.Name,
                Active = entity.Active,
                Components = ComponentDataRegistry.CaptureSceneAuthored(entity),
            });
        }

        return data;
    }

    private static int SerializedParentId(Entity entity)
    {
        for (var p = entity.Parent; p != null; p = p.Parent)
            if (!p.HideFromSerialization) return p.SerializedId;
        return 0;
    }

    private static int AssignSerializedIds(List<Entity> ordered, int highWater)
    {
        int next = highWater;
        foreach (var e in ordered)
            if (!e.HideFromSerialization && e.SerializedId > next) next = e.SerializedId;

        foreach (var e in ordered)
            if (!e.HideFromSerialization && e.SerializedId == 0) e.SerializedId = ++next;

        return next;
    }

    public static void FromData(Scene scene, SceneData data)
    {
        scene.Clear();

        scene.Kind = data.Kind;
        scene.NextSerializedId = data.NextEntityId;
        scene.LightingEnabled = data.LightingEnabled;
        scene.AmbientLight = new Microsoft.Xna.Framework.Color(data.AmbientR, data.AmbientG, data.AmbientB);
        scene.ResetAmbient();
        scene.BackColor = new Microsoft.Xna.Framework.Color(data.BackR, data.BackG, data.BackB);
        scene.Exterior = data.Exterior;
        scene.ViewZoom = data.ViewZoom > 0f ? data.ViewZoom : 1f;

        scene.CameraFixed = data.CameraFixed;
        scene.CameraFixedPos = new Microsoft.Xna.Framework.Vector2(data.CameraFixedX, data.CameraFixedY);
        scene.CameraBoundsEnabled = data.CameraBoundsEnabled;
        scene.CameraDampingEnabled = data.CameraDampingEnabled;
        scene.CameraDamping = data.CameraDampingEnabled
            ? new Microsoft.Xna.Framework.Vector2(data.CameraDampingX, data.CameraDampingY)
            : new Microsoft.Xna.Framework.Vector2(2.3f, 2.3f);
        scene.CameraBounds = new Microsoft.Xna.Framework.Rectangle(
            data.CameraBoundsX, data.CameraBoundsY, data.CameraBoundsW, data.CameraBoundsH);

        scene.DefaultSurfaceId = string.IsNullOrEmpty(data.DefaultSurface) ? null : data.DefaultSurface;

        if (!scene.ApplySkyPreset(data.Sky) && !string.IsNullOrEmpty(data.Sky))
            System.Console.WriteLine($"[Scene] sky asset missing: {data.Sky} - the reference is kept, no sky is drawn");

        scene.Ambients.Clear();
        if (data.Ambients != null)
            foreach (var a in data.Ambients)
                if (!string.IsNullOrEmpty(a.SoundId))
                {
                    if (!Assets.AssetRegistry.LooksLikeId(a.SoundId))
                        System.Console.Error.WriteLine(
                            $"[Ambient] ✘ the ambience is not an audio id: '{a.SoundId}' ({data.Name}) - " +
                            "it is an old path string. Pick it again in the inspector");
                    scene.Ambients.Add(new Audio.AmbientTrack(a.SoundId, a.Volume));
                }
        if (data.Post != null)
        {
            scene.Post = data.Post.ToProfile();
        }
        else
        {
            var tint = new Microsoft.Xna.Framework.Color(data.PostTintR, data.PostTintG, data.PostTintB);
            scene.Post = new Rendering.PostProfile
            {
                ColorFilter = Microsoft.Xna.Framework.Color.Lerp(
                    Microsoft.Xna.Framework.Color.White, tint, data.PostTintStrength),
                Vignette = data.PostVignette,
                VignetteSmooth = 0.35f,
            };
        }

        scene.PostPresets.Clear();
        if (data.PostPresets != null)
            foreach (var pd in data.PostPresets)
            {
                var asset = Assets.PostAssetCache.Get(pd.AssetId);
                if (!string.IsNullOrEmpty(pd.AssetId) && asset == null)
                    System.Console.WriteLine($"[Scene] post asset missing: {pd.Key} (id {pd.AssetId})");

                scene.PostPresets.Add(new Rendering.PostPreset
                {
                    Key = pd.Key,
                    AssetId = pd.AssetId,
                    Profile = asset?.Profile.ToProfile() ?? pd.Profile?.ToProfile() ?? new Rendering.PostProfile(),
                });
            }

        scene.ActivePostKey = data.ActivePost;
        if (!string.IsNullOrEmpty(data.ActivePost) && !scene.ApplyPostPreset(data.ActivePost))
        {
            System.Console.WriteLine($"[Scene] no active post preset: {data.ActivePost}");
            scene.ActivePostKey = null;
        }

        var idMap = new Dictionary<int, Entity>();

        foreach (var ed in data.Entities)
        {
            var entity = scene.CreateEntity(ed.Name);
        entity.SerializedId = ed.Id;
            entity.Active = ed.Active;
            foreach (var comp in ed.Components)
                comp.Apply(entity);
            MigrateLegacyScale(entity, ed);
            MigratePivotAnchor(entity, data.SchemaVersion);
            idMap[ed.Id] = entity;
        }

        foreach (var ed in data.Entities)
        {
            if (ed.ParentId == 0) continue;
            if (idMap.TryGetValue(ed.Id, out var child) && idMap.TryGetValue(ed.ParentId, out var parent))
                child.SetParent(parent);
        }

        RestoreTilemap(scene, data);

        scene.FlushPendingAdds();
    }

    private static void MigratePivotAnchor(Entity entity, int schemaVersion)
    {
        if (schemaVersion >= 2) return;
        var t = entity.GetComponent<Transform>();
        if (t == null) return;

        Microsoft.Xna.Framework.Vector2 delta;
        var sr = entity.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            var size = sr.GetDrawSize();
            if (size == Microsoft.Xna.Framework.Vector2.Zero) return;
            sr.PivotX = 0.5f; sr.PivotY = 1f;
            delta = new Microsoft.Xna.Framework.Vector2(0f, size.Y * 0.5f);
        }
        else if (entity.Name == Core.Scene.PlayerName)
        {
            delta = new Microsoft.Xna.Framework.Vector2(0f, 24f);
        }
        else return;

        t.Position += delta;
        foreach (var col in entity.GetComponents<Collider2D>())
            col.Offset -= delta;
    }

    private static void MigrateLegacyScale(Entity entity, EntityData ed)
    {
        TransformData? td = null;
        ColliderData? cd = null;
        foreach (var c in ed.Components)
        {
            if (c is TransformData t) td = t;
            else if (c is ColliderData col) cd = col;
        }
        if (td?.ScaleX == null || td.ScaleY == null) return;

        var legacy = new Microsoft.Xna.Framework.Vector2(td.ScaleX.Value, td.ScaleY.Value);

        if (cd != null && !cd.SizeX.HasValue)
        {
            var col = entity.GetComponent<BoxCollider2D>();
            if (col != null) col.Size = legacy;
        }

        var sr = entity.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            var native = sr.GetNativeSize();
            if (native == Microsoft.Xna.Framework.Vector2.Zero || legacy != native)
                sr.DrawSize = legacy;
        }
    }

    public static TilemapRenderer? RestoreTilemap(Scene scene, SceneData data)
    {
        if (data.TilemapLayers.Count == 0) return null;

        var entity = scene.CreateEntity("Tilemap");
        entity.Active = data.TilemapActive;
        var renderer = entity.AddComponent<TilemapRenderer>();
        renderer.TileSize = data.TilemapLayers[0].TileSize;

        foreach (var ld in data.TilemapLayers)
        {
            var layer = renderer.AddLayer(ld.Name, ld.Width, ld.Height);
            ApplyTilemapData(layer, ld);

            if (!string.IsNullOrEmpty(ld.Tileset))
            {
                var rel = Assets.AssetRegistry.Instance.GetPath(ld.Tileset);
                if (string.IsNullOrEmpty(rel))
                {
                    System.Console.Error.WriteLine(
                        $"[Tilemap] ✘ tileset asset id '{ld.Tileset}' not found in the registry " +
                        $"(layer '{ld.Name}') - the file was deleted, or assets.json has not been scanned");
                }
                else
                {
                    var tex = Assets.TextureLoader.Instance.Load(rel);
                    if (tex != null)
                    {
                        var tileset = new Tileset(tex, renderer.TileSize, renderer.TileSize) { SourcePath = rel };
                        tileset.Terrain = LoadTerrainFor(rel, tileset, ld.Name);
                        layer.Tileset = tileset;
                    }
                }
            }
        }
        return renderer;
    }

    internal static TilesetTerrain? LoadTerrainFor(string pngRelativePath, Tileset tileset, string layerName)
    {
        var path = Path.Combine(Assets.ContentPaths.Root, Path.ChangeExtension(pngRelativePath, ".tileset"));
        var terrain = TilesetTerrain.Load(path);
        terrain?.Validate(tileset.Columns, tileset.Rows, tileset.TileWidth, $"layer '{layerName}' ({path})");
        return terrain;
    }

    internal static TilemapLayerData CaptureTilemapLayer(TilemapLayer layer, int tileSize)
        => new()
        {
            Name = layer.Name,
            Width = layer.Width,
            Height = layer.Height,
            OriginX = layer.OriginX,
            OriginY = layer.OriginY,
            TileSize = tileSize,
            RenderLayer = layer.RenderLayer,
            HasCollision = layer.HasCollision,
            DrawOrder = layer.DrawOrder,
            Tileset = TilesetRefFor(layer.Tileset?.SourcePath),
            Rows = TileRows.Encode(layer),
        };

    internal static string? TilesetRefFor(string? sourcePath)
        => string.IsNullOrEmpty(sourcePath)
            ? null
            : Assets.AssetRegistry.Instance.GetOrCreateId(sourcePath);

    public static void ApplyTilemapData(TilemapLayer layer, TilemapLayerData data)
    {
        layer.SetBounds(data.OriginX, data.OriginY, data.Width, data.Height);
        layer.RenderLayer = data.RenderLayer;
        layer.HasCollision = data.HasCollision;
        layer.DrawOrder = data.DrawOrder;
        if (data.Rows != null)
            TileRows.Decode(data.Rows, layer);
        else if (data.Tiles != null)
            foreach (var tile in data.Tiles)
                layer.SetTile(tile.X, tile.Y, tile.TileId);
    }

    public static void SaveToFile(SceneData data, string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        if (string.IsNullOrEmpty(data.Id))
            data.Id = PeekId(filePath) ?? "";

        foreach (var ed in data.Entities)
            if (ed.Id > data.NextEntityId) data.NextEntityId = ed.Id;

        var json = JsonSerializer.Serialize(data, SceneTypeInfo);
        AtomicFile.WriteAllText(filePath, json);
    }

    internal static void MigrateLegacyRefs(SceneData data, string filePath)
    {
        var file = Path.GetFileName(filePath);

        if (data.Ambients != null)
            foreach (var a in data.Ambients)
            {
                a.SoundId = MigrateAudioRef(a.SoundId, a.LegacyPath, file, "ambience");
                a.LegacyPath = null;
            }

        foreach (var e in data.Entities)
            foreach (var c in e.Components)
                if (c is SoundEmitterData se)
                {
                    se.SoundId = MigrateAudioRef(se.SoundId, se.LegacyPath, file, $"emitter '{e.Name}'");
                    se.LegacyPath = null;
                }

        foreach (var ld in data.TilemapLayers)
        {
            var old = ld.LegacyTilesetPath;
            ld.LegacyTilesetPath = null;
            if (string.IsNullOrEmpty(old)) continue;

            if (!string.IsNullOrEmpty(ld.Tileset))
            {
                System.Console.Error.WriteLine(
                    $"[scene migration] ⚠ layer '{ld.Name}' in {file} has both the new and the old key " +
                    $"(tileset='{ld.Tileset}', tilesetPath='{old}') - the new key wins");
                continue;
            }

            var id = Assets.AssetRegistry.Instance.GetId(old);
            if (id != null) { ld.Tileset = id; continue; }

            System.Console.Error.WriteLine(
                $"[scene migration] ✘ layer '{ld.Name}' in {file}: tileset path '{old}' is not in the registry - " +
                "the value was left as it was (the tilemap will not draw). Add the file and rescan");
            ld.Tileset = old;
        }
    }

    private static string MigrateAudioRef(string soundId, string? old, string file, string where)
    {
        if (string.IsNullOrEmpty(old)) return soundId;

        if (!string.IsNullOrEmpty(soundId))
        {
            System.Console.Error.WriteLine(
                $"[scene migration] ⚠ {where} in {file} has both the new and the old key " +
                $"(soundId='{soundId}', path='{old}') - the new key wins");
            return soundId;
        }

        var id = Assets.AssetRegistry.Instance.GetAudioIdByLegacyPath(old);
        if (id != null) return id;

        System.Console.Error.WriteLine(
            $"[scene migration] ✘ {where} in {file}: no audio matches the path '{old}' in the registry - " +
            "the value was left as it was (there will be no sound). Add the file and rescan, or pick it again in the inspector");
        return old;
    }

    private static string? PeekId(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(filePath));
            var id = (node as System.Text.Json.Nodes.JsonObject)?["id"]?.GetValue<string>();
            return string.IsNullOrEmpty(id) ? null : id;
        }
        catch { return null; }
    }

    private static readonly HashSet<string> _nameNoted = new();

    public static SceneData? LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            var json = File.ReadAllText(filePath);
            var data = JsonSerializer.Deserialize(json, SceneJsonContext.Default.SceneData);
            PostData.WarnMovedKeys(json, filePath);
            if (data != null) MigrateLegacyRefs(data, filePath);
            if (data != null) ReconcileName(data, filePath);
            return data;
        }
        catch (System.Exception ex)
        {
            System.Console.WriteLine($"[SceneSerializer] Load failed: {filePath} — {ex.Message}");
            return null;
        }
    }

    private static void ReconcileName(SceneData data, string filePath)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrEmpty(stem) || data.Name == stem) return;

        if (_nameNoted.Add(filePath))
            System.Console.WriteLine(
                $"[SceneSerializer] in-file name '{data.Name}' is ignored; the scene name is the file stem '{stem}' " +
                $"({filePath}). It will be corrected on the next save.");
        data.Name = stem;
    }
}
