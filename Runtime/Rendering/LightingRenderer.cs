using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;

namespace PixelCore.Runtime.Rendering;

public class LightingRenderer : IDisposable
{
    private RenderTarget2D? _lightTarget;
    private RenderTarget2D? _additiveTarget;
    private Texture2D? _radialTexture;
    private Texture2D? _coneTexture;
    private readonly Dictionary<(int Feather, int Corner, int Core), Texture2D> _rectTextures = new();
    private GraphicsDevice? _gd;

    private const int RadialSize = 128;
    private const int ConeSize = 256;
    private const float ConeBakedSlope = 0.5f;
    private const int RectSize = 128;

    public const float NegativeScale = 0.4f;

    private static readonly BlendState Multiply = new()
    {
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.Zero,
        ColorBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.DestinationAlpha,
        AlphaDestinationBlend = Blend.Zero,
        AlphaBlendFunction = BlendFunction.Add,
    };

    private static readonly BlendState Subtract = new()
    {
        ColorSourceBlend = Blend.SourceAlpha,
        ColorDestinationBlend = Blend.One,
        ColorBlendFunction = BlendFunction.ReverseSubtract,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
        AlphaBlendFunction = BlendFunction.Add,
    };

    private static readonly BlendState AddKeepAlpha = new()
    {
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.One,
        ColorBlendFunction = BlendFunction.Add,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
        AlphaBlendFunction = BlendFunction.Add,
    };

    public bool HasLights { get; private set; }

    public bool HasAdditive { get; private set; }

    public Systems.TimeOfDay? Time { get; set; }

    public Color? AmbientOverride { get; set; }

    public void PrepareLights(GraphicsDevice gd, SpriteBatch sb, Scene scene,
        Matrix lightView, int width, int height)
    {
        HasLights = false;
        HasAdditive = false;
        if (!scene.LightingEnabled || width <= 0 || height <= 0) return;

        float hour = Time?.Hour ?? 12f;
        var viewBounds = ViewRectFromMatrix(lightView, width, height);
        foreach (var entity in scene.Entities)
        {
            if (!entity.ActiveInHierarchy) continue;
            var t = entity.GetComponent<Transform>();
            var l = entity.GetComponent<Light2D>();
            if (t == null || l == null || !l.Enabled) continue;
            TickSwitch(l, t.Position, hour, viewBounds, AllowDeferredSwitching);
        }

        var passes = ResolveScenePasses(scene);

        EnsureResources(gd, width, height, passes.Additive);

        var frame = ResolveFrame(scene, AmbientOverride);
        _dimming = frame.Dimming;

        gd.SetRenderTarget(_lightTarget);
        gd.Clear(frame.Clear);

        sb.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp,
            null, null, null, lightView);

        foreach (var (light, position) in LiveLights(scene))
        {
            var c = ResolveContribution(light);
            if (!c.Negative && c.Multiply > 0f)
                DrawLight(sb, light, position, c.Multiply);
        }

        foreach (var entity in scene.Entities)
        {
            if (!entity.ActiveInHierarchy) continue;

            var sr = entity.GetComponent<SpriteRenderer>();
            if (sr != null && sr.Enabled && sr.EmissiveIntensity > 0f)
            {
                float e = MathHelper.Clamp(sr.EmissiveIntensity, 0f, 4f);
                var c = sr.Color.ToVector4();
                sr.RenderTinted(sb, new Color(c.X * e, c.Y * e, c.Z * e, c.W));
            }
        }

        sb.End();

        if (passes.Negative)
        {
            sb.Begin(SpriteSortMode.Deferred, Subtract, SamplerState.LinearClamp,
                null, null, null, lightView);
            foreach (var (light, position) in LiveLights(scene))
            {
                var c = ResolveContribution(light);
                if (c.Negative) DrawLight(sb, light, position, c.Multiply);
            }
            sb.End();
        }

        if (passes.Additive && _additiveTarget != null)
        {
            gd.SetRenderTarget(_additiveTarget);
            gd.Clear(Color.Transparent);

            sb.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp,
                null, null, null, lightView);
            foreach (var (light, position) in LiveLights(scene))
            {
                var c = ResolveContribution(light);
                if (c.Additive > 0f) DrawLight(sb, light, position, c.Additive);
            }
            sb.End();

            HasAdditive = true;
        }

        gd.SetRenderTarget(null);

        HasLights = true;
    }

    private static IEnumerable<(Light2D Light, Vector2 Position)> LiveLights(Scene scene)
    {
        foreach (var entity in scene.Entities)
        {
            if (!entity.ActiveInHierarchy) continue;
            var transform = entity.GetComponent<Transform>();
            if (transform == null) continue;
            var light = entity.GetComponent<Light2D>();
            if (light == null || !light.Enabled) continue;
            if (light.IsOn == false) continue;
            yield return (light, transform.Position);
        }
    }

    public readonly record struct LightContribution(float Multiply, float Additive, bool Negative);

    public readonly record struct ScenePasses(bool Negative, bool Additive);

    public enum LightPass { Multiply, Additive }

    public static LightContribution ResolveContribution(Light2D light)
    {
        if (light.Negative) return new LightContribution(NegativeScale, 0f, true);

        float a = MathHelper.Clamp(light.Additive, 0f, 1f);
        return new LightContribution(1f - a, a, false);
    }

    public static ScenePasses ResolveScenePasses(Scene scene)
    {
        bool negative = false, additive = false;
        foreach (var (light, _) in LiveLights(scene))
        {
            var c = ResolveContribution(light);
            if (c.Negative) negative = true;
            else if (c.Additive > 0f) additive = true;
            if (negative && additive) break;
        }
        return new ScenePasses(negative, additive);
    }

    public static bool ShouldLightBeOn(Light2D light, float hour) => light.ActiveWhen switch
    {
        LightActiveWhen.NightOnly => !IsDaylight(hour),
        LightActiveWhen.Switch => light.SwitchOn,
        _ => true,
    };

    public static bool IsDaylight(float hour)
    {
        float h = ((hour % 24f) + 24f) % 24f;
        return h >= Systems.TimeOfDay.SunriseHour && h <= Systems.TimeOfDay.SunsetHour;
    }

    public static Rectangle ViewRectFromMatrix(Matrix view, int width, int height)
    {
        var inv = Matrix.Invert(view);
        if (!float.IsFinite(inv.M11) || !float.IsFinite(inv.M41))
            return new Rectangle(int.MinValue / 4, int.MinValue / 4, int.MaxValue / 2, int.MaxValue / 2);

        Span<Vector2> corners = stackalloc Vector2[4];
        corners[0] = Vector2.Transform(new Vector2(0, 0), inv);
        corners[1] = Vector2.Transform(new Vector2(width, 0), inv);
        corners[2] = Vector2.Transform(new Vector2(0, height), inv);
        corners[3] = Vector2.Transform(new Vector2(width, height), inv);

        float minX = corners[0].X, maxX = corners[0].X, minY = corners[0].Y, maxY = corners[0].Y;
        for (int i = 1; i < 4; i++)
        {
            minX = MathF.Min(minX, corners[i].X); maxX = MathF.Max(maxX, corners[i].X);
            minY = MathF.Min(minY, corners[i].Y); maxY = MathF.Max(maxY, corners[i].Y);
        }
        return new Rectangle((int)MathF.Floor(minX), (int)MathF.Floor(minY),
                             (int)MathF.Ceiling(maxX - minX), (int)MathF.Ceiling(maxY - minY));
    }

    public static bool CanCommitSwitch(Rectangle lightBounds, Rectangle viewBounds)
        => !lightBounds.Intersects(viewBounds);

    public static void TickSwitch(Light2D light, Vector2 position, float hour, Rectangle viewBounds,
        bool allowDefer)
    {
        bool target = ShouldLightBeOn(light, hour);

        if (light.IsOn == null) { light.IsOn = target; light.PendingOn = null; return; }

        if (target == light.IsOn) { light.PendingOn = null; return; }

        if (!allowDefer || !light.OnlyOffCamera || CanCommitSwitch(light.WorldBounds(position), viewBounds))
        {
            light.IsOn = target;
            light.PendingOn = null;
            return;
        }
        light.PendingOn = target;
    }

    private static readonly LightPass[] NoPasses = Array.Empty<LightPass>();
    private static readonly LightPass[] MultiplyOnly = { LightPass.Multiply };
    private static readonly LightPass[] MultiplyThenAdditive = { LightPass.Multiply, LightPass.Additive };

    public static LightPass[] ResolveComposite(bool hasLights, bool hasAdditive)
    {
        if (!hasLights) return NoPasses;
        return hasAdditive ? MultiplyThenAdditive : MultiplyOnly;
    }

    private float _dimming = 1f;

    public float IntensityScale { get; set; } = 1f;

    public bool AllowDeferredSwitching { get; set; }

    public readonly record struct FrameLighting(Color Ambient, Color Clear, float Dimming);

    public static FrameLighting ResolveFrame(Scene scene, Color? ambientOverride)
    {
        var ambient = ambientOverride ?? scene.EffectiveAmbient;
        return new FrameLighting(ambient, LightingBalance.ApplyFloor(ambient),
            LightingBalance.DimmingFactor(ambient));
    }

    private void DrawLight(SpriteBatch sb, Light2D light, Vector2 position, float weight)
    {
        var tint = light.Color *
            (MathHelper.Clamp(light.Intensity, 0f, 8f) * _dimming * weight * MathF.Max(0f, IntensityScale));

        switch (light.Shape)
        {
            case LightShape.Point:
            {
                float scale = (light.Radius * 2f) / RadialSize;
                sb.Draw(_radialTexture!, position, null, tint, 0f,
                    new Vector2(RadialSize / 2f, RadialSize / 2f),
                    scale, SpriteEffects.None, 0f);
                break;
            }
            case LightShape.Cone:
            {
                float rotation = light.Rotation;
                float length = light.Length;

                if (light.FollowSun && Time != null)
                {
                    if (Time.SunStrength <= 0.02f) return;
                    float lean = -MathF.Cos(MathHelper.Clamp(Time.SunT, 0f, 1f) * MathF.PI);
                    rotation += lean * 35f;
                    length *= 1f + 0.5f * (1f - Time.SunStrength);
                    tint *= MathF.Min(1f, Time.SunStrength * 1.6f);
                }

                float half = MathHelper.ToRadians(MathHelper.Clamp(light.SpreadAngle, 2f, 170f) / 2f);
                float sx = length / ConeSize;
                float sy = length * MathF.Tan(half) / (ConeSize * ConeBakedSlope);
                sb.Draw(_coneTexture!, position, null, tint,
                    MathHelper.ToRadians(rotation),
                    new Vector2(0f, ConeSize / 2f),
                    new Vector2(sx, sy), SpriteEffects.None, 0f);
                break;
            }
            case LightShape.Rect:
            {
                var tex = GetRectTexture(light.Feather, light.CornerRadius, light.Core);
                var size = new Vector2(MathF.Max(light.RectSize.X, 1f), MathF.Max(light.RectSize.Y, 1f));
                sb.Draw(tex, position, null, tint,
                    MathHelper.ToRadians(light.Rotation),
                    new Vector2(RectSize / 2f, RectSize / 2f),
                    size / RectSize, SpriteEffects.None, 0f);
                break;
            }
            case LightShape.Texture:
            {
                if (light.Texture == null) return;

                sb.Draw(light.Texture, position, null, tint,
                    MathHelper.ToRadians(light.Rotation),
                    new Vector2(light.Texture.Width / 2f, light.Texture.Height / 2f),
                    light.Scale, SpriteEffects.None, 0f);
                break;
            }
        }
    }

    public void Composite(SpriteBatch sb, int width, int height, RasterizerState? rasterizer = null)
    {
        if (_lightTarget == null) return;

        var dest = new Rectangle(0, 0, width, height);
        foreach (var pass in ResolveComposite(HasLights, HasAdditive))
        {
            var texture = pass == LightPass.Additive ? _additiveTarget : _lightTarget;
            if (texture == null) continue;

            sb.Begin(SpriteSortMode.Deferred,
                pass == LightPass.Additive ? AddKeepAlpha : Multiply,
                SamplerState.PointClamp, null, rasterizer);
            sb.Draw(texture, dest, Color.White);
            sb.End();
        }
    }

    private void EnsureResources(GraphicsDevice gd, int width, int height, bool wantAdditive)
    {
        _gd = gd;
        if (_lightTarget == null || _lightTarget.Width != width || _lightTarget.Height != height)
        {
            _lightTarget?.Dispose();
            _lightTarget = new RenderTarget2D(gd, width, height);
        }

        if (wantAdditive &&
            (_additiveTarget == null || _additiveTarget.Width != width || _additiveTarget.Height != height))
        {
            _additiveTarget?.Dispose();
            _additiveTarget = new RenderTarget2D(gd, width, height);
        }

        _radialTexture ??= CreateRadialTexture(gd);
        _coneTexture ??= CreateConeTexture(gd);
    }

    private static Texture2D CreateRadialTexture(GraphicsDevice gd)
    {
        var tex = new Texture2D(gd, RadialSize, RadialSize);
        tex.SetData(BakeRadial());
        return tex;
    }

    public static Color[] BakeRadial()
    {
        var pixels = new Color[RadialSize * RadialSize];
        float half = RadialSize / 2f;

        for (int y = 0; y < RadialSize; y++)
            for (int x = 0; x < RadialSize; x++)
            {
                float dx = (x + 0.5f - half) / half;
                float dy = (y + 0.5f - half) / half;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                float t = MathHelper.Clamp(1f - d, 0f, 1f);
                float f = t * t * (3f - 2f * t);
                pixels[y * RadialSize + x] = new Color(f, f, f, f);
            }

        return pixels;
    }

    private static Texture2D CreateConeTexture(GraphicsDevice gd)
    {
        var tex = new Texture2D(gd, ConeSize, ConeSize);
        var pixels = new Color[ConeSize * ConeSize];
        float cy = ConeSize / 2f;

        for (int y = 0; y < ConeSize; y++)
            for (int x = 0; x < ConeSize; x++)
            {
                float px = x + 0.5f;
                float dy = MathF.Abs(y + 0.5f - cy);

                float axial = MathF.Max(0f, 1f - px / ConeSize);
                axial *= axial;

                float slope = dy / MathF.Max(px, 0.001f);
                float s = MathHelper.Clamp((1f - slope / ConeBakedSlope) / 0.35f, 0f, 1f);
                float angular = s * s * (3f - 2f * s);

                float f = axial * angular;
                pixels[y * ConeSize + x] = new Color(f, f, f, f);
            }

        tex.SetData(pixels);
        return tex;
    }

    public static (int Feather, int Corner, int Core) RectKey(float feather, float cornerRadius, float core)
        => ((int)MathF.Round(MathHelper.Clamp(feather, 0f, 1f) * 20f),
            (int)MathF.Round(MathHelper.Clamp(cornerRadius, 0f, 1f) * 20f),
            (int)MathF.Round(MathHelper.Clamp(core, 0f, 1f) * 20f));

    private Texture2D GetRectTexture(float feather, float cornerRadius, float core)
    {
        var key = RectKey(feather, cornerRadius, core);
        if (_rectTextures.TryGetValue(key, out var cached)) return cached;

        var tex = new Texture2D(_gd!, RectSize, RectSize);
        tex.SetData(BakeRect(key.Feather / 20f, key.Corner / 20f, key.Core / 20f));
        _rectTextures[key] = tex;
        return tex;
    }

    public static Color[] BakeRect(float feather, float cornerRadius, float core)
    {
        float f = MathF.Max(MathHelper.Clamp(feather, 0f, 1f), 0.02f);
        float r = MathHelper.Clamp(cornerRadius, 0f, 1f);
        float c = MathHelper.Clamp(core, 0f, 1f);
        float inset = 1f - r;

        var pixels = new Color[RectSize * RectSize];
        float half = RectSize / 2f;

        for (int y = 0; y < RectSize; y++)
            for (int x = 0; x < RectSize; x++)
            {
                float nx = MathF.Abs(x + 0.5f - half) / half;
                float ny = MathF.Abs(y + 0.5f - half) / half;

                float qx = nx - inset, qy = ny - inset;
                float m = (qx > 0f && qy > 0f)
                    ? inset + MathF.Sqrt(qx * qx + qy * qy)
                    : MathF.Max(nx, ny);

                float s = MathHelper.Clamp((1f - m) / f, 0f, 1f);
                float v = s * s * (3f - 2f * s);

                if (c > 0f)
                {
                    float g = MathHelper.Clamp(1f - m, 0f, 1f);
                    g = g * g * (3f - 2f * g);
                    v = v + (g - v) * c;
                }

                pixels[y * RectSize + x] = new Color(v, v, v, v);
            }

        return pixels;
    }

    public void Dispose()
    {
        _lightTarget?.Dispose();
        _additiveTarget?.Dispose();
        _radialTexture?.Dispose();
        _coneTexture?.Dispose();
        foreach (var tex in _rectTextures.Values) tex.Dispose();
        _rectTextures.Clear();
    }
}
