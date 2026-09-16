using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.UI;

namespace PixelCore.Runtime.Particles;

[ComponentCategory(ComponentCategories.Render)]
public class ParticleEmitter : Component, IRenderable, IAdditive, IAtmosphere
{
    public string? PresetId { get; set; }

    public bool Emitting { get; set; } = true;

    public float RateScale { get; set; } = 1f;

    public Color? TintOverride { get; set; }

    public string? UnresolvedPresetRef { get; set; }

    private readonly ParticleSimulation _sim = new();
    private Texture2D? _texture;
    private string? _textureIdLoaded;
    private bool _prewarmed;

    public ParticleSimulation Simulation => _sim;

    public int AliveCount => _sim.AliveCount;

    private static readonly HashSet<string> _missingPresetWarned = new();
    private static readonly HashSet<string> _missingTextureWarned = new();

    public ParticlePreset? ResolvePreset()
    {
        var id = PresetId;
        if (string.IsNullOrEmpty(id)) return null;

        var asset = ParticlePresetCache.Get(id);
        if (asset != null)
        {
            UnresolvedPresetRef = null;
            return asset.Preset;
        }

        UnresolvedPresetRef = id;
        if (_missingPresetWarned.Add(id))
            Console.Error.WriteLine(
                $"[Particles] ✘ preset '{id}' not found - this emitter will emit nothing. " +
                $"Entity '{Entity?.Name}'. The .particle file was deleted or moved outside Content.");
        return null;
    }

    public override void Update(float deltaTime)
    {
        var preset = ResolvePreset();
        if (preset == null) return;

        var transform = Entity.GetComponent<Transform>();
        var origin = transform?.Position ?? Vector2.Zero;

        if (!_prewarmed)
        {
            _prewarmed = true;
            _sim.Prewarm(origin, preset, Emitting, RateScale);
        }

        _sim.Update(deltaTime, origin, preset, Emitting, RateScale);
    }

    public int Burst()
    {
        var preset = ResolvePreset();
        if (preset == null) return 0;

        var transform = Entity.GetComponent<Transform>();
        return _sim.Burst(transform?.Position ?? Vector2.Zero, preset);
    }

    public override void OnDisable() { _sim.Clear(); _prewarmed = false; }

    public override void OnDestroy() { _sim.Clear(); _prewarmed = false; }

    public int RenderLayer => ResolvePresetQuiet()?.RenderLayer ?? RenderLayers.AboveEntities;

    public float SortY => (Entity?.GetComponent<Transform>()?.Position.Y ?? 0f)
                          + (ResolvePresetQuiet()?.SortYOffset ?? 0f);

    public float SortOffset => 0f;

    public bool IsAdditive => ResolvePresetQuiet()?.Additive ?? false;

    public bool InAtmosphere => ResolvePresetQuiet()?.InAtmosphere ?? false;

    private ParticlePreset? ResolvePresetQuiet()
        => string.IsNullOrEmpty(PresetId) ? null : ParticlePresetCache.Get(PresetId)?.Preset;

    public void Render(SpriteBatch spriteBatch)
    {
        int alive = _sim.AliveCount;
        if (alive == 0) return;

        var preset = ResolvePresetQuiet();
        if (preset == null) return;

        var texture = ResolveTexture(preset, spriteBatch.GraphicsDevice);
        if (texture == null) return;

        var tint = TintOverride ?? preset.Tint;
        var half = new Vector2(texture.Width * 0.5f, texture.Height * 0.5f);
        var particles = _sim.Alive;

        for (int i = 0; i < alive; i++)
        {
            ref readonly var p = ref particles[i];
            float delta = p.Delta;

            float alpha = Math.Clamp(preset.Alpha.Evaluate(p.AlphaSeed, delta), 0f, 1f);
            if (alpha <= 0f) continue;

            float scale = preset.Scale.Evaluate(p.ScaleSeed, delta);
            if (scale <= 0f) continue;

            var pos = DrawPosition(p.RenderPosition, preset.SnapsToPixels);

            spriteBatch.Draw(
                texture,
                pos,
                null,
                tint * alpha,
                p.Rotation * (MathF.PI / 180f),
                half,
                scale,
                SpriteEffects.None,
                0f);
        }
    }

    public static Vector2 DrawPosition(Vector2 renderPosition, bool snapToPixels)
        => snapToPixels
            ? new Vector2(MathF.Floor(renderPosition.X + 0.5f), MathF.Floor(renderPosition.Y + 0.5f))
            : renderPosition;

    private Texture2D? ResolveTexture(ParticlePreset preset, GraphicsDevice device)
    {
        var id = preset.TextureId;
        if (string.IsNullOrEmpty(id)) return UIDraw.Pixel(device);

        if (id.Length > 0 && id[0] == '@') return ResolveBuiltin(id, device);

        if (_texture != null && _textureIdLoaded == id) return _texture;

        var rel = AssetRegistry.Instance.GetPath(id);
        if (string.IsNullOrEmpty(rel))
        {
            if (_missingTextureWarned.Add(id))
                Console.Error.WriteLine(
                    $"[Particles] ✘ preset texture '{id}' not found - drawing a 1px white dot. " +
                    $"The png was deleted or moved outside Content.");
            return UIDraw.Pixel(device);
        }

        _texture = TextureLoader.Instance.Load(rel);
        _textureIdLoaded = id;
        return _texture ?? UIDraw.Pixel(device);
    }

    private static readonly Dictionary<string, Texture2D> _builtins = new();

    public const string SoftDot = "@soft";

    private static Texture2D BuildSoftDot(GraphicsDevice device)
    {
        const int size = 32;
        var tex = new Texture2D(device, size, size);
        var px = new Color[size * size];
        float c = (size - 1) / 2f, r = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = MathF.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / r;
                float a = Math.Clamp(1f - d, 0f, 1f);
                a = a * a * (3f - 2f * a);
                a *= a;
                px[y * size + x] = new Color(a, a, a, a);
            }
        tex.SetData(px);
        return tex;
    }

    private static Texture2D ResolveBuiltin(string name, GraphicsDevice device)
    {
        if (_builtins.TryGetValue(name, out var cached) && !cached.IsDisposed) return cached;

        Texture2D? built = name == SoftDot ? BuildSoftDot(device) : null;
        if (built == null)
        {
            if (_missingTextureWarned.Add(name))
                Console.Error.WriteLine(
                    $"[Particles] ✘ built-in texture '{name}' does not exist - drawing a 1px white dot. " +
                    $"Available names: {SoftDot}");
            return UIDraw.Pixel(device);
        }

        _builtins[name] = built;
        return built;
    }

    public static void ClearBuiltins()
    {
        foreach (var t in _builtins.Values) t.Dispose();
        _builtins.Clear();
    }

    public static bool IsBuiltinTexture(string? id) => !string.IsNullOrEmpty(id) && id[0] == '@';

    public static bool IsKnownBuiltin(string? id) => id == SoftDot;

    public static void ResetWarnings()
    {
        _missingPresetWarned.Clear();
        _missingTextureWarned.Clear();
    }
}
