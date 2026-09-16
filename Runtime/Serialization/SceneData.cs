using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Audio;
using PixelCore.Runtime.Rendering;
using PixelCore.Runtime.Tilemap;

namespace PixelCore.Runtime.Serialization;

public class SceneData
{
    public const int CurrentSchemaVersion = 2;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("nextEntityId")]
    public int NextEntityId { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Untitled";

    [JsonPropertyName("kind")]
    public SceneKind Kind { get; set; } = SceneKind.Level;

    [JsonPropertyName("backR")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int BackR { get; set; }
    [JsonPropertyName("backG")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int BackG { get; set; }
    [JsonPropertyName("backB")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int BackB { get; set; }

    [JsonPropertyName("lightingEnabled")]
    public bool LightingEnabled { get; set; }
    [JsonPropertyName("ambientR")]
    public int AmbientR { get; set; } = 110;
    [JsonPropertyName("ambientG")]
    public int AmbientG { get; set; } = 110;
    [JsonPropertyName("ambientB")]
    public int AmbientB { get; set; } = 130;
    [JsonPropertyName("exterior")]
    public bool Exterior { get; set; }

    [JsonPropertyName("viewZoom")]
    public float ViewZoom { get; set; } = 1f;

    [JsonPropertyName("cameraFixed")]
    public bool CameraFixed { get; set; }
    [JsonPropertyName("cameraFixedX")]
    public float CameraFixedX { get; set; }
    [JsonPropertyName("cameraFixedY")]
    public float CameraFixedY { get; set; }
    [JsonPropertyName("cameraBoundsEnabled")]
    public bool CameraBoundsEnabled { get; set; }
    [JsonPropertyName("cameraDampingEnabled")]
    public bool CameraDampingEnabled { get; set; }
    [JsonPropertyName("cameraDampingX")]
    public float CameraDampingX { get; set; } = 2.3f;
    [JsonPropertyName("cameraDampingY")]
    public float CameraDampingY { get; set; } = 2.3f;
    [JsonPropertyName("cameraBoundsX")]
    public int CameraBoundsX { get; set; }
    [JsonPropertyName("cameraBoundsY")]
    public int CameraBoundsY { get; set; }
    [JsonPropertyName("cameraBoundsW")]
    public int CameraBoundsW { get; set; }
    [JsonPropertyName("cameraBoundsH")]
    public int CameraBoundsH { get; set; }

    [JsonPropertyName("postTintR")]
    public int PostTintR { get; set; } = 255;
    [JsonPropertyName("postTintG")]
    public int PostTintG { get; set; } = 255;
    [JsonPropertyName("postTintB")]
    public int PostTintB { get; set; } = 255;
    [JsonPropertyName("postTintStrength")]
    public float PostTintStrength { get; set; }
    [JsonPropertyName("postFogR")]
    public int PostFogR { get; set; } = 185;
    [JsonPropertyName("postFogG")]
    public int PostFogG { get; set; } = 195;
    [JsonPropertyName("postFogB")]
    public int PostFogB { get; set; } = 215;
    [JsonPropertyName("postFogOpacity")]
    public float PostFogOpacity { get; set; }
    [JsonPropertyName("postVignette")]
    public float PostVignette { get; set; }

    [JsonPropertyName("post")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PostData? Post { get; set; }

    [JsonPropertyName("postPresets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PostPresetData>? PostPresets { get; set; }

    [JsonPropertyName("ambients")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AmbientTrackData>? Ambients { get; set; }

    [JsonPropertyName("activePost")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActivePost { get; set; }

    [JsonPropertyName("defaultSurface")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultSurface { get; set; }

    [JsonPropertyName("sky")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sky { get; set; }

    [JsonPropertyName("entities")]
    public List<EntityData> Entities { get; set; } = new();
    [JsonPropertyName("tilemapLayers")]
    public List<TilemapLayerData> TilemapLayers { get; set; } = new();

    [JsonPropertyName("tilemapActive")]
    public bool TilemapActive { get; set; } = true;
}

public class PostData
{
    [JsonPropertyName("exposure")]
    public float Exposure { get; set; }
    [JsonPropertyName("contrast")]
    public float Contrast { get; set; }
    [JsonPropertyName("filterR")]
    public int FilterR { get; set; } = 255;
    [JsonPropertyName("filterG")]
    public int FilterG { get; set; } = 255;
    [JsonPropertyName("filterB")]
    public int FilterB { get; set; } = 255;
    [JsonPropertyName("hueShift")]
    public float HueShift { get; set; }
    [JsonPropertyName("saturation")]
    public float Saturation { get; set; }
    [JsonPropertyName("temperature")]
    public float Temperature { get; set; }
    [JsonPropertyName("tempTint")]
    public float TempTint { get; set; }
    [JsonPropertyName("liftX")]
    public float LiftX { get; set; } = 1f;
    [JsonPropertyName("liftY")]
    public float LiftY { get; set; } = 1f;
    [JsonPropertyName("liftZ")]
    public float LiftZ { get; set; } = 1f;
    [JsonPropertyName("liftW")]
    public float LiftW { get; set; }
    [JsonPropertyName("gammaX")]
    public float GammaX { get; set; } = 1f;
    [JsonPropertyName("gammaY")]
    public float GammaY { get; set; } = 1f;
    [JsonPropertyName("gammaZ")]
    public float GammaZ { get; set; } = 1f;
    [JsonPropertyName("gammaW")]
    public float GammaW { get; set; }
    [JsonPropertyName("gainX")]
    public float GainX { get; set; } = 1f;
    [JsonPropertyName("gainY")]
    public float GainY { get; set; } = 1f;
    [JsonPropertyName("gainZ")]
    public float GainZ { get; set; } = 1f;
    [JsonPropertyName("gainW")]
    public float GainW { get; set; }
    [JsonPropertyName("shadowsX")]
    public float ShadowsX { get; set; } = 1f;
    [JsonPropertyName("shadowsY")]
    public float ShadowsY { get; set; } = 1f;
    [JsonPropertyName("shadowsZ")]
    public float ShadowsZ { get; set; } = 1f;
    [JsonPropertyName("shadowsW")]
    public float ShadowsW { get; set; }
    [JsonPropertyName("midtonesX")]
    public float MidtonesX { get; set; } = 1f;
    [JsonPropertyName("midtonesY")]
    public float MidtonesY { get; set; } = 1f;
    [JsonPropertyName("midtonesZ")]
    public float MidtonesZ { get; set; } = 1f;
    [JsonPropertyName("midtonesW")]
    public float MidtonesW { get; set; }
    [JsonPropertyName("highlightsX")]
    public float HighlightsX { get; set; } = 1f;
    [JsonPropertyName("highlightsY")]
    public float HighlightsY { get; set; } = 1f;
    [JsonPropertyName("highlightsZ")]
    public float HighlightsZ { get; set; } = 1f;
    [JsonPropertyName("highlightsW")]
    public float HighlightsW { get; set; }
    [JsonPropertyName("tonemap")]
    public ToneMode Tonemap { get; set; }

    [JsonPropertyName("lutTexture")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LutTexture { get; set; }
    [JsonPropertyName("vigR")]
    public int VigR { get; set; }
    [JsonPropertyName("vigG")]
    public int VigG { get; set; }
    [JsonPropertyName("vigB")]
    public int VigB { get; set; }
    [JsonPropertyName("vignette")]
    public float Vignette { get; set; }
    [JsonPropertyName("vigSmooth")]
    public float VigSmooth { get; set; } = 0.2f;
    [JsonPropertyName("vigRounded")]
    public bool VigRounded { get; set; }
    [JsonPropertyName("grainIntensity")]
    public float GrainIntensity { get; set; }
    [JsonPropertyName("grainCells")]
    public float GrainCells { get; set; } = 720f;
    [JsonPropertyName("bloomIntensity")]
    public float BloomIntensity { get; set; }
    [JsonPropertyName("bloomThreshold")]
    public float BloomThreshold { get; set; } = 0.9f;
    [JsonPropertyName("bloomScatter")]
    public float BloomScatter { get; set; } = 0.7f;
    [JsonPropertyName("bloomTintR")]
    public int BloomTintR { get; set; } = 255;
    [JsonPropertyName("bloomTintG")]
    public int BloomTintG { get; set; } = 255;
    [JsonPropertyName("bloomTintB")]
    public int BloomTintB { get; set; } = 255;

    public static PostData From(PostProfile p) => new()
    {
        Exposure = p.Exposure, Contrast = p.Contrast,
        FilterR = p.ColorFilter.R, FilterG = p.ColorFilter.G, FilterB = p.ColorFilter.B,
        HueShift = p.HueShift, Saturation = p.Saturation,
        Temperature = p.Temperature, TempTint = p.TempTint,
        LiftX = p.Lift.X, LiftY = p.Lift.Y, LiftZ = p.Lift.Z, LiftW = p.Lift.W,
        GammaX = p.Gamma.X, GammaY = p.Gamma.Y, GammaZ = p.Gamma.Z, GammaW = p.Gamma.W,
        GainX = p.Gain.X, GainY = p.Gain.Y, GainZ = p.Gain.Z, GainW = p.Gain.W,
        ShadowsX = p.SmhShadows.X, ShadowsY = p.SmhShadows.Y, ShadowsZ = p.SmhShadows.Z, ShadowsW = p.SmhShadows.W,
        MidtonesX = p.SmhMidtones.X, MidtonesY = p.SmhMidtones.Y, MidtonesZ = p.SmhMidtones.Z, MidtonesW = p.SmhMidtones.W,
        HighlightsX = p.SmhHighlights.X, HighlightsY = p.SmhHighlights.Y, HighlightsZ = p.SmhHighlights.Z, HighlightsW = p.SmhHighlights.W,
        Tonemap = p.Tonemap,
        LutTexture = string.IsNullOrEmpty(p.LutTextureId) ? null : p.LutTextureId,
        VigR = p.VignetteColor.R, VigG = p.VignetteColor.G, VigB = p.VignetteColor.B,
        Vignette = p.Vignette, VigSmooth = p.VignetteSmooth, VigRounded = p.VignetteRounded,
        GrainIntensity = p.GrainIntensity, GrainCells = p.GrainCells,
        BloomIntensity = p.BloomIntensity, BloomThreshold = p.BloomThreshold, BloomScatter = p.BloomScatter,
        BloomTintR = p.BloomTint.R, BloomTintG = p.BloomTint.G, BloomTintB = p.BloomTint.B,
    };

    public PostProfile ToProfile() => new()
    {
        Exposure = Exposure, Contrast = Contrast,
        ColorFilter = new Color(FilterR, FilterG, FilterB),
        HueShift = HueShift, Saturation = Saturation,
        Temperature = Temperature, TempTint = TempTint,
        Lift = new Vector4(LiftX, LiftY, LiftZ, LiftW),
        Gamma = new Vector4(GammaX, GammaY, GammaZ, GammaW),
        Gain = new Vector4(GainX, GainY, GainZ, GainW),
        SmhShadows = new Vector4(ShadowsX, ShadowsY, ShadowsZ, ShadowsW),
        SmhMidtones = new Vector4(MidtonesX, MidtonesY, MidtonesZ, MidtonesW),
        SmhHighlights = new Vector4(HighlightsX, HighlightsY, HighlightsZ, HighlightsW),
        Tonemap = Tonemap,
        LutTextureId = LutTexture,
        VignetteColor = new Color(VigR, VigG, VigB),
        Vignette = Vignette, VignetteSmooth = VigSmooth, VignetteRounded = VigRounded,
        GrainIntensity = GrainIntensity, GrainCells = GrainCells,
        BloomIntensity = BloomIntensity, BloomThreshold = BloomThreshold, BloomScatter = BloomScatter,
        BloomTint = new Color(BloomTintR, BloomTintG, BloomTintB),
    };

    private static readonly (string Key, string Where)[] MovedKeys =
    {
        ("chromAb",        "moved to FxStack (cutscenes and weather) - cutscenes use c.Fx(...), persistent state uses FxStack.SetWeather(...)"),
        ("lensDistortion", "moved to FxStack (cutscenes and weather) - cutscenes use c.Fx(...), persistent state uses FxStack.SetWeather(...)"),
        ("fogOpacity",     "moved to FxStack (cutscenes and weather) - cutscenes use c.Fx(...), persistent state uses FxStack.SetWeather(...)"),
        ("grainResponse",  "removed - it was a weight that grew stronger in the dark, while the dodge is strongest in bright areas (the opposite direction)"),
        ("grainSize",      "removed - a knob that enlarged the cells, it was what produced the crawling look. Cells are set by grainCells (fixed to the screen)"),
    };

    private static readonly HashSet<string> _movedNoted = new();

    public static void WarnMovedKeys(string rawJson, string where)
    {
        if (string.IsNullOrEmpty(rawJson)) return;

        List<string>? found = null;
        List<string>? wheres = null;
        foreach (var (key, where_) in MovedKeys)
        {
            float worst = 0f;
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(
                         rawJson, "\"" + key + "\"\\s*:\\s*(-?[0-9]*\\.?[0-9]+(?:[eE][-+]?[0-9]+)?)"))
            {
                if (!float.TryParse(m.Groups[1].Value,
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var v)) continue;
                if (MathF.Abs(v) > MathF.Abs(worst)) worst = v;
            }
            if (worst == 0f) continue;
            (found ??= new()).Add($"{key} {worst:0.###}");
            if (!(wheres ??= new()).Contains(where_)) wheres.Add(where_);
        }
        if (found == null || !_movedNoted.Add(where)) return;

        Console.Error.WriteLine(
            $"[Post] {where} has keys that are no longer read ({string.Join(" · ", found)}) - ignored. " +
            string.Join(" / ", wheres!) + ". They do not go back into the room profile");
    }

    public static void ResetMovedWarnings() => _movedNoted.Clear();
}

public class PostPresetData
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("assetId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AssetId { get; set; }

    [JsonPropertyName("profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PostData? Profile { get; set; }
}

public class AmbientTrackData
{
    [JsonPropertyName("soundId")]
    public string SoundId { get; set; } = "";

    [JsonPropertyName("volume")]
    public float Volume { get; set; } = 1f;

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyPath { get; set; }
}

public class EntityData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("parentId")]
    public int ParentId { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;
    [JsonPropertyName("components")]
    public List<ComponentData> Components { get; set; } = new();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TransformData), "transform")]
[JsonDerivedType(typeof(SpriteData), "sprite")]
[JsonDerivedType(typeof(AnimatorData), "animator")]
[JsonDerivedType(typeof(PlayerControllerData), "playerController")]
[JsonDerivedType(typeof(InteractorData), "interactor")]
[JsonDerivedType(typeof(FootstepEmitterData), "footstepEmitter")]
[JsonDerivedType(typeof(RigidbodyData), "rigidbody")]
[JsonDerivedType(typeof(ColliderData), "collider")]
[JsonDerivedType(typeof(SceneInstanceData), "sceneInstance")]
[JsonDerivedType(typeof(LightData), "light")]
[JsonDerivedType(typeof(InteractableData), "interactable")]
[JsonDerivedType(typeof(CircleColliderData), "circleCollider")]
[JsonDerivedType(typeof(CapsuleColliderData), "capsuleCollider")]
[JsonDerivedType(typeof(SoundEmitterData), "soundEmitter")]
[JsonDerivedType(typeof(SortingGroupData), "sortingGroup")]
[JsonDerivedType(typeof(SurfaceAreaData), "surfaceArea")]
[JsonDerivedType(typeof(CutsceneTriggerData), "cutsceneTrigger")]
[JsonDerivedType(typeof(TalkerData), "talker")]
[JsonDerivedType(typeof(ParticleEmitterData), "particleEmitter")]
[JsonDerivedType(typeof(GenericComponentData), "component")]
public abstract class ComponentData
{
    public abstract void Capture(Entity entity);
    public abstract void Apply(Entity entity);

    public abstract bool WriteTo(Component c);

    public virtual void CaptureInstance(Component c) => Capture(c.Entity);

    [JsonIgnore]
    public virtual IEnumerable<string>? LegacyKeys => null;
}

public static class ComponentDataRegistry
{
    private static readonly List<(System.Type Type, System.Func<Entity, ComponentData?> Capture, System.Func<ComponentData> Fresh)> _capturers = new()
    {
        Entry<Transform, TransformData>(),
        Entry<SpriteRenderer, SpriteData>(),
        Entry<Animator, AnimatorData>(),
        Entry<Gameplay.Player.PlayerController, PlayerControllerData>(),
        Entry<Interactor, InteractorData>(),
        Entry<FootstepEmitter, FootstepEmitterData>(),
        Entry<Rigidbody2D, RigidbodyData>(),
        Entry<SceneInstance, SceneInstanceData>(),
        Entry<Light2D, LightData>(),
        Entry<Interactable, InteractableData>(),
        Entry<SoundEmitter, SoundEmitterData>(),
        Entry<SortingGroup, SortingGroupData>(),
        Entry<SurfaceArea, SurfaceAreaData>(),
        Entry<CutsceneTrigger, CutsceneTriggerData>(),
        Entry<Talker, TalkerData>(),
        Entry<Particles.ParticleEmitter, ParticleEmitterData>(),
    };

    public static List<ComponentData> CaptureSceneAuthored(Entity e)
    {
        var list = CaptureAll(e, forPersist: true);
        var inst = e.GetComponent<SceneInstance>();
        if (inst is not { RootMerged: true }) return list;
        return list.FindAll(d => !inst.IsFromBase(d));
    }

    private static (System.Type, System.Func<Entity, ComponentData?>, System.Func<ComponentData>) Entry<TComp, TData>()
        where TComp : Component where TData : ComponentData, new()
        => (typeof(TComp), e => Cap<TComp, TData>(e), () => new TData());

    public static bool HasBespokeData(System.Type componentType)
        => typeof(Collider2D).IsAssignableFrom(componentType)
           || _capturers.Exists(c => c.Type.IsAssignableFrom(componentType));

    private static readonly HashSet<System.Type> _unregisteredWarned = new();

    private static ComponentData? Cap<TComp, TData>(Entity e)
        where TComp : Component where TData : ComponentData, new()
    {
        if (e.GetComponent<TComp>() == null) return null;
        var d = new TData();
        d.Capture(e);
        return d;
    }

    public static List<ComponentData> CaptureAll(Entity e, bool forPersist = false)
    {
        var skipBody = forPersist && CodeOwnedBody.Owns(e);

        var list = new List<ComponentData>();
        foreach (var cap in _capturers)
        {
            var d = cap.Capture(e);
            if (d == null) continue;
            if (skipBody && CodeOwnedBody.IsPart(d)) continue;
            list.Add(d);
        }

        if (!skipBody)
        {
            foreach (var c in e.GetComponents<BoxCollider2D>())
            { var d = new ColliderData(); d.CaptureFrom(c); list.Add(d); }
            foreach (var c in e.GetComponents<CapsuleCollider2D>())
            { var d = new CapsuleColliderData(); d.CaptureFrom(c); list.Add(d); }
        }
        foreach (var c in e.GetComponents<CircleCollider2D>())
        { var d = new CircleColliderData(); d.CaptureFrom(c); list.Add(d); }

        foreach (var c in e.Components)
        {
            var t = c.GetType();
            if (HasBespokeData(t)) continue;
            if (!ComponentTypes.IsRegistered(t))
            {
                if (_unregisteredWarned.Add(t))
                    System.Console.WriteLine($"[Scene] ⚠ '{t.Name}' is not in the creation table, so saving is skipped - check the source generator eligibility " +
                                             "(public, non-nested, non-generic, a public default constructor, outside PixelCore.Editor). The table is generated at build time, so it is always current");
                continue;
            }
            var d = new GenericComponentData();
            d.CaptureInstance(c);
            list.Add(d);
        }
        return list;
    }

    public static ComponentData? CreateFor(Component c)
    {
        switch (c)
        {
            case SceneInstance: return null;
            case BoxCollider2D: return new ColliderData();
            case CircleCollider2D: return new CircleColliderData();
            case CapsuleCollider2D: return new CapsuleColliderData();
        }
        foreach (var cap in _capturers)
            if (cap.Type.IsInstanceOfType(c)) return cap.Fresh();
        return ComponentTypes.IsRegistered(c.GetType())
            ? new GenericComponentData { Name = c.GetType().FullName ?? c.GetType().Name }
            : null;
    }
}

public class TransformData : ComponentData
{
    [JsonPropertyName("x")]
    public float X { get; set; }
    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("scaleX")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? ScaleX { get; set; }
    [JsonPropertyName("scaleY")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? ScaleY { get; set; }

    [JsonPropertyName("rotation")]
    public float Rotation { get; set; }

    public override void Capture(Entity e)
    {
        var t = e.GetComponent<Transform>()!;
        X = t.Position.X; Y = t.Position.Y;
        Rotation = t.Rotation;
    }
    public override void Apply(Entity e)
        => WriteTo(e.GetComponent<Transform>() ?? e.AddComponent<Transform>());

    public override bool WriteTo(Component c)
    {
        if (c is not Transform t) return false;
        t.Position = new Vector2(X, Y);
        t.Rotation = Rotation;
        return true;
    }
}

public class SpriteData : ComponentData
{
    [JsonPropertyName("texture")]
    public string? Texture { get; set; }
    [JsonPropertyName("atlas")]
    public string? Atlas { get; set; }
    [JsonPropertyName("slice")]
    public string? Slice { get; set; }
    [JsonPropertyName("srcX")]
    public int? SrcX { get; set; }
    [JsonPropertyName("srcY")]
    public int? SrcY { get; set; }
    [JsonPropertyName("srcW")]
    public int? SrcW { get; set; }
    [JsonPropertyName("srcH")]
    public int? SrcH { get; set; }
    [JsonPropertyName("colorR")]
    public int ColorR { get; set; } = 255;
    [JsonPropertyName("colorG")]
    public int ColorG { get; set; } = 255;
    [JsonPropertyName("colorB")]
    public int ColorB { get; set; } = 255;
    [JsonPropertyName("colorA")]
    public int ColorA { get; set; } = 255;
    [JsonPropertyName("renderLayer")]
    public int RenderLayer { get; set; } = 0;
    [JsonPropertyName("ySort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? YSort { get; set; }
    [JsonPropertyName("sortOffset")]
    public float SortOffset { get; set; }
    [JsonPropertyName("flipX")]
    public bool FlipX { get; set; }
    [JsonPropertyName("flipY")]
    public bool FlipY { get; set; }
    [JsonPropertyName("pivotX")]
    public float PivotX { get; set; } = 0.5f;

    [JsonPropertyName("pivotY")]
    public float PivotY { get; set; } = 1f;
    [JsonPropertyName("emissive")]
    public float Emissive { get; set; }
    [JsonPropertyName("castShadow")]
    public bool CastShadow { get; set; }

    [JsonPropertyName("shadowScale")]
    public float ShadowScale { get; set; } = 1f;

    [JsonPropertyName("shadowW")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ShadowW { get; set; }

    [JsonPropertyName("shadowH")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ShadowH { get; set; }

    [JsonPropertyName("shadowRadius")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? ShadowRadius { get; set; }

    [JsonPropertyName("shadowOffX")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ShadowOffX { get; set; }

    [JsonPropertyName("shadowOffY")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ShadowOffY { get; set; }

    [JsonPropertyName("shadowTexture")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShadowTexture { get; set; }

    [JsonPropertyName("drawW")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? DrawW { get; set; }
    [JsonPropertyName("drawH")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? DrawH { get; set; }

    public override void Capture(Entity e) => CaptureFrom(e.GetComponent<SpriteRenderer>()!);

    protected void CaptureFrom(SpriteRenderer s)
    {
        DrawW = s.DrawSize?.X; DrawH = s.DrawSize?.Y;
        if (!string.IsNullOrEmpty(s.AtlasPath))
        {
            Atlas = AssetRegistry.Instance.GetOrCreateId(s.AtlasPath);
            Slice = s.SliceName;
        }
        else if (!string.IsNullOrEmpty(s.TexturePath))
        {
            Texture = AssetRegistry.Instance.GetOrCreateId(s.TexturePath);
        }
        else if (s.UnresolvedSpriteRef is { } keep)
        {
            Atlas = keep.Atlas; Texture = keep.Texture; Slice = keep.Slice;
        }
        if (s.SourceRect.HasValue)
        {
            var r = s.SourceRect.Value;
            SrcX = r.X; SrcY = r.Y; SrcW = r.Width; SrcH = r.Height;
        }
        ColorR = s.Color.R; ColorG = s.Color.G; ColorB = s.Color.B; ColorA = s.Color.A;
        RenderLayer = s.RenderLayer; SortOffset = s.SortOffset;
        FlipX = s.FlipX; FlipY = s.FlipY; PivotX = s.PivotX; PivotY = s.PivotY;
        Emissive = s.EmissiveIntensity;
        CastShadow = s.CastShadow;
        ShadowScale = s.ShadowScale;
        ShadowW = s.ShadowWidth;
        ShadowH = s.ShadowHeight;
        ShadowRadius = MathF.Abs(s.ShadowRadius - 1f) < 1e-6f ? null : s.ShadowRadius;
        ShadowOffX = s.ShadowOffset.X != 0 ? s.ShadowOffset.X : null;
        ShadowOffY = s.ShadowOffset.Y != 0 ? s.ShadowOffset.Y : null;
        ShadowTexture = !string.IsNullOrEmpty(s.ShadowTexturePath)
            ? AssetRegistry.Instance.GetOrCreateId(s.ShadowTexturePath)
            : s.UnresolvedShadowTextureRef;
    }
    public override void Apply(Entity e) => WriteTo(e.AddComponent<SpriteRenderer>());

    public override bool WriteTo(Component comp)
    {
        if (comp is not SpriteRenderer s) return false;
        WriteFieldsTo(s);
        return true;
    }

    protected void WriteFieldsTo(SpriteRenderer s)
    {
        s.Color = new Color(ColorR, ColorG, ColorB, ColorA);
        s.RenderLayer = RenderLayer; s.SortOffset = SortOffset;
        if (YSort == false && RenderLayer == RenderLayers.Entities) s.RenderLayer = RenderLayers.BelowEntities;
        s.FlipX = FlipX; s.FlipY = FlipY; s.PivotX = PivotX; s.PivotY = PivotY;
        s.EmissiveIntensity = Emissive;
        s.CastShadow = CastShadow;
        s.ShadowScale = ShadowScale;
        s.ShadowWidth = ShadowW;
        s.ShadowHeight = ShadowH;
        s.ShadowRadius = ShadowRadius ?? 1f;
        s.ShadowOffset = new Point(ShadowOffX ?? 0, ShadowOffY ?? 0);

        if (string.IsNullOrEmpty(ShadowTexture))
        {
            s.SetShadowTexture(null);
        }
        else
        {
            var shadowPath = AssetRegistry.Instance.GetPath(ShadowTexture);
            if (!string.IsNullOrEmpty(shadowPath)) s.SetShadowTexture(shadowPath);
            else
            {
                s.ShadowTexturePath = null; s.ShadowTexture = null;
                s.UnresolvedShadowTextureRef = ShadowTexture;
                Warn($"{ShadowTexture}/shadow",
                    $"[Scene] ✘ shadow texture id '{ShadowTexture}' not found in the registry " +
                    $"({s.Entity?.Name ?? "?"}) - drawing the procedural ellipse. Pick it again in the inspector");
            }
        }
        s.DrawSize = DrawW.HasValue && DrawH.HasValue ? new Vector2(DrawW.Value, DrawH.Value) : null;

        var atlasPath = string.IsNullOrEmpty(Atlas) ? null : AssetRegistry.Instance.GetPath(Atlas);
        var texPath = string.IsNullOrEmpty(Texture) ? null : AssetRegistry.Instance.GetPath(Texture);
        if (!string.IsNullOrEmpty(atlasPath) && !string.IsNullOrEmpty(Slice))
        {
            s.TexturePath = null;
            s.UnresolvedSpriteRef = null;
            s.SetSprite(atlasPath, Slice);
        }
        else if (!string.IsNullOrEmpty(texPath))
        {
            s.AtlasPath = null; s.SliceName = null;
            s.UnresolvedSpriteRef = null;
            s.TexturePath = texPath;
            s.Texture = TextureLoader.Instance.Load(texPath);
            s.SourceRect = SrcX.HasValue && SrcY.HasValue && SrcW.HasValue && SrcH.HasValue
                ? new Rectangle(SrcX.Value, SrcY.Value, SrcW.Value, SrcH.Value)
                : null;
        }
        else if (string.IsNullOrEmpty(Atlas) && string.IsNullOrEmpty(Texture))
        {
            s.AtlasPath = null; s.SliceName = null; s.TexturePath = null;
            s.Texture = null; s.SourceRect = null;
            s.UnresolvedSpriteRef = null;
        }
        else
        {
            s.UnresolvedSpriteRef = (Atlas, Texture, Slice);
            WarnUnresolved(s);
        }
    }

    private void WarnUnresolved(SpriteRenderer s)
    {
        var who = s.Entity?.Name ?? "?";

        if (!string.IsNullOrEmpty(Atlas) && string.IsNullOrEmpty(AssetRegistry.Instance.GetPath(Atlas)))
            Warn($"{Atlas}/unregistered",
                $"[Scene] ✘ atlas asset id '{Atlas}' not found in the registry ({who}) - " +
                "the file was deleted, or assets.json has not been scanned. Pick the sprite again in the inspector");
        else if (!string.IsNullOrEmpty(Atlas) && string.IsNullOrEmpty(Slice))
            Warn($"{Atlas}/slice",
                $"[Scene] ✘ atlas '{Atlas}' was found but the slice name is empty ({who}) - " +
                "nothing will be drawn. Pick a slice in the inspector");

        if (!string.IsNullOrEmpty(Texture) && string.IsNullOrEmpty(AssetRegistry.Instance.GetPath(Texture)))
            Warn($"{Texture}/unregistered",
                $"[Scene] ✘ texture asset id '{Texture}' not found in the registry ({who}) - " +
                "the file was deleted, or assets.json has not been scanned. Pick the sprite again in the inspector");
    }

    private static readonly HashSet<string> _warnedRefs = new();

    private static void Warn(string key, string message)
    {
        if (_warnedRefs.Add(key)) System.Console.Error.WriteLine(message);
    }
}

public class AnimatorData : ComponentData
{
    [JsonPropertyName("clips")]
    public List<string> Clips { get; set; } = new();

    [JsonPropertyName("defaultClip")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultClip { get; set; }

    [JsonPropertyName("legacy")]
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Legacy { get; set; }

    [JsonIgnore]
    public override IEnumerable<string>? LegacyKeys => Legacy?.Keys;

    private void WarnLegacyFields(Entity e)
    {
        if (Legacy == null || Legacy.Count == 0) return;
        Console.WriteLine(
            $"[AnimatorData] ⚠ ignoring stale animator fields: {string.Join(", ", Legacy.Keys)} ({e.Name}) - "
            + "render fields now belong to the sprite block. Saving the scene again removes them.");
    }

    public override void Capture(Entity e)
    {
        var a = e.GetComponent<Animator>();
        if (a == null) return;

        Clips.Clear();
        foreach (var clip in a.Clips)
        {
            if (string.IsNullOrEmpty(clip.SourceId))
            {
                Console.WriteLine($"[AnimatorData] ⚠ a clip with no source cannot be saved: '{clip.Name}' ({e.Name})");
                continue;
            }
            Clips.Add(clip.SourceId);
        }
        foreach (var lost in a.UnresolvedClipIds)
            if (!Clips.Contains(lost)) Clips.Add(lost);
        DefaultClip = a.DefaultClip;
    }

    public override void Apply(Entity e)
    {
        WarnLegacyFields(e);
        WriteTo(e.AddComponent<Animator>());
    }

    public override bool WriteTo(Component comp)
    {
        if (comp is not Animator a) return false;

        a.ClearClips();

        foreach (var id in Clips)
        {
            var clip = AnimClipCache.Get(id);
            if (clip != null) a.AddClip(clip);
            else a.UnresolvedClipIds.Add(id);
        }

        a.DefaultClip = DefaultClip;
        if (!string.IsNullOrEmpty(DefaultClip) && a.HasClip(DefaultClip)) a.Play(DefaultClip);
        return true;
    }
}

public class PlayerControllerData : ComponentData
{
    public override void Capture(Entity e) { }
    public override void Apply(Entity e) => WriteTo(e.AddComponent<Gameplay.Player.PlayerController>());
    public override bool WriteTo(Component c) => c is Gameplay.Player.PlayerController;
}

public class InteractorData : ComponentData
{
    public override void Capture(Entity e) { }
    public override void Apply(Entity e) => WriteTo(e.AddComponent<Interactor>());
    public override bool WriteTo(Component c) => c is Interactor;
}

public class FootstepEmitterData : ComponentData
{
    public override void Capture(Entity e) { }
    public override void Apply(Entity e) => WriteTo(e.AddComponent<FootstepEmitter>());
    public override bool WriteTo(Component c) => c is FootstepEmitter;
}

public class GenericComponentData : ComponentData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    public override void Capture(Entity e) { }
    public override void CaptureInstance(Component c) => Name = c.GetType().FullName ?? c.GetType().Name;

    public override void Apply(Entity e)
    {
        var c = ComponentTypes.Create(Name);
        if (c == null)
        {
            System.Console.WriteLine($"[Scene] ✘ unknown component '{Name}' - it is not in the creation table. The table is generated at build time, so " +
                                     "the class with this name was deleted or renamed (delete this entry from the scene, or restore the class name)");
            return;
        }
        e.AddComponent(c);
    }

    public override bool WriteTo(Component c) => c.GetType().FullName == Name;
}

public class RigidbodyData : ComponentData
{
    [JsonPropertyName("useGravity")]
    public bool UseGravity { get; set; }
    [JsonPropertyName("gravityScale")]
    public float GravityScale { get; set; } = 1f;
    [JsonPropertyName("isKinematic")]
    public bool IsKinematic { get; set; }
    [JsonPropertyName("maxFallSpeed")]
    public float MaxFallSpeed { get; set; } = 800f;

    public override void Capture(Entity e)
    {
        var r = e.GetComponent<Rigidbody2D>()!;
        UseGravity = r.UseGravity; GravityScale = r.GravityScale;
        IsKinematic = r.IsKinematic; MaxFallSpeed = r.MaxFallSpeed;
    }
    public override void Apply(Entity e) => WriteTo(e.AddComponent<Rigidbody2D>());

    public override bool WriteTo(Component c)
    {
        if (c is not Rigidbody2D r) return false;
        r.UseGravity = UseGravity; r.GravityScale = GravityScale;
        r.IsKinematic = IsKinematic; r.MaxFallSpeed = MaxFallSpeed;
        return true;
    }
}

public interface IColliderData
{
    bool IsTrigger { get; }
}

public class ColliderData : ComponentData, IColliderData
{
    [JsonPropertyName("offsetX")]
    public float OffsetX { get; set; }
    [JsonPropertyName("offsetY")]
    public float OffsetY { get; set; }
    [JsonPropertyName("sizeX")]
    public float? SizeX { get; set; }
    [JsonPropertyName("sizeY")]
    public float? SizeY { get; set; }
    [JsonPropertyName("isTrigger")]
    public bool IsTrigger { get; set; }
    [JsonPropertyName("isOneWay")]
    public bool IsOneWay { get; set; }

    public override void Capture(Entity e) => CaptureFrom(e.GetComponent<BoxCollider2D>()!);
    public override void CaptureInstance(Component c) => CaptureFrom((BoxCollider2D)c);

    public void CaptureFrom(BoxCollider2D c)
    {
        OffsetX = c.Offset.X; OffsetY = c.Offset.Y;
        SizeX = c.Size.X; SizeY = c.Size.Y;
        IsTrigger = c.IsTrigger; IsOneWay = c.IsOneWay;
    }
    public override void Apply(Entity e) => WriteTo(e.AddComponent<BoxCollider2D>());

    public override bool WriteTo(Component comp)
    {
        if (comp is not BoxCollider2D c) return false;
        c.Offset = new Vector2(OffsetX, OffsetY);
        if (SizeX.HasValue && SizeY.HasValue) c.Size = new Vector2(SizeX.Value, SizeY.Value);
        c.IsTrigger = IsTrigger; c.IsOneWay = IsOneWay;
        return true;
    }
}

public class InteractableData : ComponentData
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "verb.examine";
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "Read";

    [JsonPropertyName("block")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Block { get; set; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }
    [JsonPropertyName("event")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Event { get; set; }
    [JsonPropertyName("target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetSceneId { get; set; }

    [JsonPropertyName("spawn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Spawn { get; set; }

    [JsonPropertyName("facing")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Facing { get; set; }

    [JsonPropertyName("sound")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SoundId { get; set; }

    [JsonPropertyName("range")]
    public float Range { get; set; } = 8f;

    public override void Capture(Entity e)
    {
        var it = e.GetComponent<Interactable>()!;
        Prompt = it.Prompt;
        Kind = it.Kind.ToString();
        Block = string.IsNullOrEmpty(it.Block) ? null : it.Block;
        Text = string.IsNullOrEmpty(it.Text) ? null : it.Text;
        Event = string.IsNullOrEmpty(it.Event) ? null : it.Event;
        TargetSceneId = string.IsNullOrEmpty(it.TargetSceneId) ? null : it.TargetSceneId;
        Spawn = string.IsNullOrEmpty(it.Spawn) ? null : it.Spawn;
        Facing = string.IsNullOrEmpty(it.Facing) ? null : it.Facing;
        SoundId = string.IsNullOrEmpty(it.SoundId) ? null : it.SoundId;
        Range = it.Range;
    }

    private void WarnBadRefs(Entity e)
    {
        if (!string.IsNullOrEmpty(TargetSceneId) && !AssetRegistry.LooksLikeId(TargetSceneId))
            Console.Error.WriteLine(
                $"[Interactable] ✘ the door destination is not a scene id: '{TargetSceneId}' ({e.Name}) - " +
                "it is an old room name. Pick the room again under Target Room in the inspector");

        if (!string.IsNullOrEmpty(SoundId) && !AssetRegistry.LooksLikeId(SoundId))
            Console.Error.WriteLine(
                $"[Interactable] ✘ the sound effect is not an audio id: '{SoundId}' ({e.Name}) - " +
                "it is an old path string. Pick it again under Sound in the inspector");
    }

    public override void Apply(Entity e)
    {
        WarnBadRefs(e);
        WriteTo(e.AddComponent<Interactable>());
    }

    public override bool WriteTo(Component c)
    {
        if (c is not Interactable it) return false;
        it.Prompt = Prompt;
        if (System.Enum.TryParse<InteractKind>(Kind, out var k)) it.Kind = k;
        it.Block = Block ?? "";
        it.Text = Text ?? "";
        it.Event = Event ?? "";
        it.TargetSceneId = TargetSceneId ?? "";
        it.Spawn = Spawn ?? "";
        it.Facing = Facing ?? "";
        it.SoundId = SoundId ?? "";
        it.Range = Range;
        return true;
    }
}

public class SortingGroupData : ComponentData
{
    [JsonPropertyName("renderLayer")]
    public int RenderLayer { get; set; } = RenderLayers.Entities;
    [JsonPropertyName("sortOffset")]
    public float SortOffset { get; set; }

    public override void Capture(Entity e)
    {
        var g = e.GetComponent<SortingGroup>()!;
        RenderLayer = g.RenderLayer; SortOffset = g.SortOffset;
    }
    public override void Apply(Entity e) => WriteTo(e.AddComponent<SortingGroup>());

    public override bool WriteTo(Component c)
    {
        if (c is not SortingGroup g) return false;
        g.RenderLayer = RenderLayer; g.SortOffset = SortOffset;
        return true;
    }
}

public class CircleColliderData : ComponentData, IColliderData
{
    [JsonPropertyName("offsetX")]
    public float OffsetX { get; set; }
    [JsonPropertyName("offsetY")]
    public float OffsetY { get; set; }
    [JsonPropertyName("radius")]
    public float Radius { get; set; } = 8f;
    [JsonPropertyName("isTrigger")]
    public bool IsTrigger { get; set; }

    public override void Capture(Entity e) => CaptureFrom(e.GetComponent<CircleCollider2D>()!);
    public override void CaptureInstance(Component c) => CaptureFrom((CircleCollider2D)c);

    public void CaptureFrom(CircleCollider2D c)
    {
        OffsetX = c.Offset.X; OffsetY = c.Offset.Y;
        Radius = c.Radius; IsTrigger = c.IsTrigger;
    }
    public override void Apply(Entity e) => WriteTo(e.AddComponent<CircleCollider2D>());

    public override bool WriteTo(Component comp)
    {
        if (comp is not CircleCollider2D c) return false;
        c.Offset = new Vector2(OffsetX, OffsetY);
        c.Radius = Radius; c.IsTrigger = IsTrigger;
        return true;
    }
}

public class CapsuleColliderData : ComponentData, IColliderData
{
    [JsonPropertyName("offsetX")]
    public float OffsetX { get; set; }
    [JsonPropertyName("offsetY")]
    public float OffsetY { get; set; }
    [JsonPropertyName("radius")]
    public float Radius { get; set; } = 6f;
    [JsonPropertyName("length")]
    public float Length { get; set; } = 16f;
    [JsonPropertyName("horizontal")]
    public bool Horizontal { get; set; }
    [JsonPropertyName("isTrigger")]
    public bool IsTrigger { get; set; }

    public override void Capture(Entity e) => CaptureFrom(e.GetComponent<CapsuleCollider2D>()!);
    public override void CaptureInstance(Component c) => CaptureFrom((CapsuleCollider2D)c);

    public void CaptureFrom(CapsuleCollider2D c)
    {
        OffsetX = c.Offset.X; OffsetY = c.Offset.Y;
        Radius = c.Radius; Length = c.Length; Horizontal = c.Horizontal; IsTrigger = c.IsTrigger;
    }
    public override void Apply(Entity e) => WriteTo(e.AddComponent<CapsuleCollider2D>());

    public override bool WriteTo(Component comp)
    {
        if (comp is not CapsuleCollider2D c) return false;
        c.Offset = new Vector2(OffsetX, OffsetY);
        c.Radius = Radius; c.Length = Length; c.Horizontal = Horizontal; c.IsTrigger = IsTrigger;
        return true;
    }
}

public class SceneInstanceData : ComponentData
{
    [JsonPropertyName("sceneId")]
    public string? SceneId { get; set; }

    [JsonPropertyName("overrides")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<InstanceOverrideData>? Overrides { get; set; }

    public override void Capture(Entity e)
    {
        var inst = e.GetComponent<SceneInstance>()!;
        SceneId = inst.SceneId;
        Overrides = inst.IsLoaded ? InstanceOverrides.Compute(inst) : inst.Overrides;
    }
    public override void Apply(Entity e)
    {
        if (string.IsNullOrEmpty(SceneId)) return;
        WriteTo(e.AddComponent<SceneInstance>());
    }

    public override bool WriteTo(Component c)
    {
        if (c is not SceneInstance s) return false;
        s.SceneId = SceneId;
        s.Overrides = Overrides;
        s.Load();
        return true;
    }
}

public class InstanceOverrideData
{
    [JsonPropertyName("entityId")]
    public int EntityId { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("active")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Active { get; set; }

    [JsonPropertyName("deleted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Deleted { get; set; }

    [JsonPropertyName("components")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<System.Text.Json.Nodes.JsonObject>? Components { get; set; }

    [JsonPropertyName("removed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Removed { get; set; }

    [JsonIgnore]
    public bool IsEmpty =>
        Name == null && Active == null && Deleted == null
        && (Components == null || Components.Count == 0)
        && (Removed == null || Removed.Count == 0);
}

public class LightData : ComponentData
{
    [JsonPropertyName("r")]
    public int R { get; set; } = 255;
    [JsonPropertyName("g")]
    public int G { get; set; } = 230;
    [JsonPropertyName("b")]
    public int B { get; set; } = 180;
    [JsonPropertyName("radius")]
    public float Radius { get; set; } = Light2D.DefaultRadius;
    [JsonPropertyName("intensity")]
    public float Intensity { get; set; } = Light2D.DefaultIntensity;

    [JsonPropertyName("additive")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public float Additive { get; set; }

    [JsonPropertyName("negative")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Negative { get; set; }

    [JsonPropertyName("shape")]
    public LightShape Shape { get; set; } = LightShape.Point;
    [JsonPropertyName("rotation")]
    public float Rotation { get; set; }
    [JsonPropertyName("length")]
    public float Length { get; set; } = 160f;
    [JsonPropertyName("spreadAngle")]
    public float SpreadAngle { get; set; } = 50f;
    [JsonPropertyName("rectW")]
    public float RectW { get; set; } = 96f;
    [JsonPropertyName("rectH")]
    public float RectH { get; set; } = 64f;
    [JsonPropertyName("feather")]
    public float Feather { get; set; } = 0.35f;

    [JsonPropertyName("cornerRadius")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public float CornerRadius { get; set; }

    [JsonPropertyName("core")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public float Core { get; set; }
    [JsonPropertyName("followSun")]
    public bool FollowSun { get; set; }

    [JsonPropertyName("activeWhen")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public LightActiveWhen ActiveWhen { get; set; } = LightActiveWhen.Always;

    [JsonPropertyName("switchOn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SwitchOn { get; set; }

    [JsonPropertyName("onlyOffCamera")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool OnlyOffCamera { get; set; }

    [JsonPropertyName("texture")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Texture { get; set; }

    [JsonPropertyName("texScaleX")]
    public float ScaleX { get; set; } = 1f;
    [JsonPropertyName("texScaleY")]
    public float ScaleY { get; set; } = 1f;

    public override void Capture(Entity e)
    {
        var l = e.GetComponent<Light2D>()!;
        R = l.Color.R; G = l.Color.G; B = l.Color.B;
        Radius = l.Radius; Intensity = l.Intensity;
        Additive = l.Additive; Negative = l.Negative;
        Shape = l.Shape; Rotation = l.Rotation;
        Length = l.Length; SpreadAngle = l.SpreadAngle;
        RectW = l.RectSize.X; RectH = l.RectSize.Y; Feather = l.Feather;
        CornerRadius = l.CornerRadius; Core = l.Core;
        FollowSun = l.FollowSun;
        ActiveWhen = l.ActiveWhen; SwitchOn = l.SwitchOn; OnlyOffCamera = l.OnlyOffCamera;
        ScaleX = l.Scale.X; ScaleY = l.Scale.Y;
        Texture = !string.IsNullOrEmpty(l.TexturePath)
            ? AssetRegistry.Instance.GetOrCreateId(l.TexturePath)
            : l.UnresolvedTextureRef;
    }
    public override void Apply(Entity e) => WriteTo(e.AddComponent<Light2D>());

    public override bool WriteTo(Component c)
    {
        if (c is not Light2D l) return false;
        l.Color = new Color(R, G, B);
        l.Radius = Radius; l.Intensity = Intensity;
        l.Additive = Additive; l.Negative = Negative;
        l.Shape = Shape; l.Rotation = Rotation;
        l.Length = Length; l.SpreadAngle = SpreadAngle;
        l.RectSize = new Vector2(RectW, RectH); l.Feather = Feather;
        l.CornerRadius = CornerRadius; l.Core = Core;
        l.FollowSun = FollowSun;
        l.ActiveWhen = ActiveWhen; l.SwitchOn = SwitchOn; l.OnlyOffCamera = OnlyOffCamera;
        l.Scale = new Vector2(ScaleX, ScaleY);

        if (string.IsNullOrEmpty(Texture))
        {
            l.SetTexture(null);
        }
        else
        {
            var path = AssetRegistry.Instance.GetPath(Texture);
            if (!string.IsNullOrEmpty(path)) l.SetTexture(path);
            else
            {
                l.TexturePath = null; l.Texture = null;
                l.UnresolvedTextureRef = Texture;
                WarnUnresolvedTexture(Texture, l.Entity?.Name ?? "?");
            }
        }
        return true;
    }

    private static readonly HashSet<string> _warnedTextureRefs = new();
    private static void WarnUnresolvedTexture(string id, string who)
    {
        if (!_warnedTextureRefs.Add(id)) return;
        System.Console.Error.WriteLine(
            $"[Scene] ✘ light decal asset id '{id}' is not in the registry ({who}) - " +
            "the file may have been deleted, or assets.json may not have been rescanned. Pick the texture again in the inspector.");
    }
}

public class ParticleEmitterData : ComponentData
{
    [JsonPropertyName("preset")]
    public string Preset { get; set; } = "";

    [JsonPropertyName("emitting")]
    public bool Emitting { get; set; } = true;

    [JsonPropertyName("rateScale")]
    public float RateScale { get; set; } = 1f;

    [JsonPropertyName("tintR")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TintR { get; set; }

    [JsonPropertyName("tintG")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TintG { get; set; }

    [JsonPropertyName("tintB")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TintB { get; set; }

    public override void Capture(Entity e)
    {
        var p = e.GetComponent<Particles.ParticleEmitter>()!;
        Preset = !string.IsNullOrEmpty(p.PresetId) ? p.PresetId : (p.UnresolvedPresetRef ?? "");
        Emitting = p.Emitting;
        RateScale = p.RateScale;
        TintR = p.TintOverride?.R; TintG = p.TintOverride?.G; TintB = p.TintOverride?.B;
    }

    public override void Apply(Entity e)
    {
        if (!string.IsNullOrEmpty(Preset) && !AssetRegistry.LooksLikeId(Preset))
            Console.Error.WriteLine(
                $"[ParticleEmitter] ✘ the preset is not an asset id: '{Preset}' ({e.Name}) - " +
                "a reference is always a registry id. Pick it again in the inspector");

        WriteTo(e.AddComponent<Particles.ParticleEmitter>());
    }

    public override bool WriteTo(Component c)
    {
        if (c is not Particles.ParticleEmitter p) return false;
        p.PresetId = Preset;
        p.Emitting = Emitting;
        p.RateScale = RateScale;
        p.TintOverride = (TintR.HasValue && TintG.HasValue && TintB.HasValue)
            ? new Microsoft.Xna.Framework.Color(TintR.Value, TintG.Value, TintB.Value)
            : null;
        return true;
    }
}

public class SoundEmitterData : ComponentData
{
    [JsonPropertyName("soundId")]
    public string SoundId { get; set; } = "";

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyPath { get; set; }

    [JsonPropertyName("radius")]
    public float Radius { get; set; } = 160f;
    [JsonPropertyName("volume")]
    public float Volume { get; set; } = 1f;
    [JsonPropertyName("bus")]
    public AudioBus Bus { get; set; } = AudioBus.Ambient;
    [JsonPropertyName("panStrength")]
    public float PanStrength { get; set; } = 1f;
    [JsonPropertyName("fadeIn")]
    public float FadeIn { get; set; } = 0.35f;
    [JsonPropertyName("fadeOut")]
    public float FadeOut { get; set; } = 0.35f;

    public override void Capture(Entity e)
    {
        var s = e.GetComponent<SoundEmitter>()!;
        SoundId = s.SoundId; Radius = s.Radius; Volume = s.Volume; Bus = s.Bus;
        PanStrength = s.PanStrength; FadeIn = s.FadeIn; FadeOut = s.FadeOut;
    }

    public override void Apply(Entity e)
    {
        if (!string.IsNullOrEmpty(SoundId) && !AssetRegistry.LooksLikeId(SoundId))
            Console.Error.WriteLine(
                $"[SoundEmitter] ✘ the source is not an audio id: '{SoundId}' ({e.Name}) - " +
                "it is an old path string. Pick it again under Sound in the inspector");

        WriteTo(e.AddComponent<SoundEmitter>());
    }

    public override bool WriteTo(Component c)
    {
        if (c is not SoundEmitter s) return false;
        s.SoundId = SoundId; s.Radius = Radius; s.Volume = Volume; s.Bus = Bus;
        s.PanStrength = PanStrength; s.FadeIn = FadeIn; s.FadeOut = FadeOut;
        return true;
    }
}

public class SurfaceAreaData : ComponentData
{
    [JsonPropertyName("surfaceId")]
    public string SurfaceId { get; set; } = "";
    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    public override void Capture(Entity e)
    {
        var a = e.GetComponent<SurfaceArea>()!;
        SurfaceId = a.SurfaceId; Priority = a.Priority;
    }

    public override void Apply(Entity e) => WriteTo(e.AddComponent<SurfaceArea>());

    public override bool WriteTo(Component c)
    {
        if (c is not SurfaceArea a) return false;
        a.SurfaceId = SurfaceId; a.Priority = Priority;
        return true;
    }
}

public class CutsceneTriggerData : ComponentData
{
    [JsonPropertyName("cutsceneId")]
    public string CutsceneId { get; set; } = "";
    [JsonPropertyName("once")]
    public bool Once { get; set; } = true;

    public override void Capture(Entity e)
    {
        var t = e.GetComponent<CutsceneTrigger>()!;
        CutsceneId = t.CutsceneId; Once = t.Once;
    }

    public override void Apply(Entity e) => WriteTo(e.AddComponent<CutsceneTrigger>());

    public override bool WriteTo(Component c)
    {
        if (c is not CutsceneTrigger t) return false;
        t.CutsceneId = CutsceneId; t.Once = Once;
        return true;
    }
}

public class TalkerData : ComponentData
{
    [JsonPropertyName("anchorX")]
    public float AnchorX { get; set; }
    [JsonPropertyName("anchorY")]
    public float AnchorY { get; set; }

    public override void Capture(Entity e)
    {
        var t = e.GetComponent<Talker>()!;
        AnchorX = t.BubbleAnchor.X; AnchorY = t.BubbleAnchor.Y;
    }

    public override void Apply(Entity e) => WriteTo(e.AddComponent<Talker>());

    public override bool WriteTo(Component c)
    {
        if (c is not Talker t) return false;
        t.BubbleAnchor = new Microsoft.Xna.Framework.Vector2(AnchorX, AnchorY);
        return true;
    }
}

public class TilemapLayerData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("width")]
    public int Width { get; set; }
    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("originX")]
    public int OriginX { get; set; }
    [JsonPropertyName("originY")]
    public int OriginY { get; set; }

    [JsonPropertyName("tileSize")]
    public int TileSize { get; set; } = 32;

    [JsonPropertyName("tileset")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tileset { get; set; }

    [JsonPropertyName("tilesetPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyTilesetPath { get; set; }

    [JsonPropertyName("renderLayer")]
    public int RenderLayer { get; set; } = PixelCore.Runtime.Core.RenderLayers.Floor;
    [JsonPropertyName("hasCollision")]
    public bool HasCollision { get; set; } = true;
    [JsonPropertyName("drawOrder")]
    public int DrawOrder { get; set; }

    [JsonPropertyName("rows")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Rows { get; set; }

    [JsonPropertyName("tiles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TileEntry>? Tiles { get; set; }
}

public class TileEntry
{
    [JsonPropertyName("x")]
    public int X { get; set; }
    [JsonPropertyName("y")]
    public int Y { get; set; }
    [JsonPropertyName("tileId")]
    public int TileId { get; set; }
}
