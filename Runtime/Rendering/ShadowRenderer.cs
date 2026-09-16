using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Systems;

namespace PixelCore.Runtime.Rendering;

public enum ShadowBand
{
    None,
    Below,
    Entities,
}

public class ShadowRenderer : IDisposable
{
    private RenderTarget2D? _belowTarget, _entitiesTarget;
    private BasicEffect? _effect;

    private readonly List<(ShadowBand Band, Rectangle Quad, Texture2D Plate)> _items = new();
    private readonly VertexPositionColorTexture[] _verts = new VertexPositionColorTexture[4];
    private static readonly short[] QuadIndices = { 0, 1, 2, 2, 1, 3 };

    private readonly Dictionary<(int W, int H, float Radius, float Softness), Texture2D> _shapes = new();

    private readonly Dictionary<Texture2D, Texture2D> _opaque = new();

    private const int ShapeCacheLimit = 128;
    private const float GroundSquash = 0.62f;

    public const float ShadowSoftness = 0f;

    public bool HasBelowShadows { get; private set; }

    public bool HasEntityShadows { get; private set; }

    public bool HasShadows => HasBelowShadows || HasEntityShadows;

    public static ShadowBand ShadowBandOf(int renderLayer)
        => renderLayer <= RenderLayers.Floor ? ShadowBand.None
         : renderLayer < RenderLayers.Entities ? ShadowBand.Below
         : ShadowBand.Entities;

    public const float BaseOpacity = 0.38f;

    public void PrepareShadows(GraphicsDevice gd, Scene scene, Matrix view, int width, int height)
    {
        HasBelowShadows = HasEntityShadows = false;
        _items.Clear();
        if (width <= 0 || height <= 0) return;

        EnsureResources(gd, width, height);

        gd.BlendState = BlendState.AlphaBlend;
        gd.DepthStencilState = DepthStencilState.None;
        gd.RasterizerState = RasterizerState.CullNone;

        gd.SamplerStates[0] = SamplerState.PointClamp;

        _effect!.View = view;
        _effect.Projection = Matrix.CreateOrthographicOffCenter(0, width, height, 0, 0, 1);

        foreach (var entity in scene.Entities)
        {
            if (!entity.ActiveInHierarchy) continue;
            foreach (var component in entity.Components)
            {
                if (component is not IShadowCaster caster || !component.Enabled) continue;
                if (!caster.CastShadow) continue;
                if (!caster.TryGetShadowSprite(out var tex, out var src, out var dest, out bool flipX))
                    continue;

                var footPos = entity.GetComponent<Components.Transform>()?.Position
                              ?? new Vector2(dest.X + dest.Width / 2f, dest.Y + dest.Height);

                Point size = PlateSize(caster, dest.Width);
                Texture2D plate;
                if (caster.ShadowTexture is { } authored)
                    plate = OpaquePlate(gd, authored);
                else
                    plate = ShapePlate(gd, size, caster.ShadowRadius);

                var quad = ShadowQuad(size, footPos, caster.ShadowOffset);
                if (quad.Width <= 0 || quad.Height <= 0) continue;

                var band = ShadowBandOf(((IRenderable)component).RenderLayer);
                if (band == ShadowBand.None) continue;

                _items.Add((band, quad, plate));
            }
        }

        HasBelowShadows = FillBand(gd, ShadowBand.Below, _belowTarget!);
        HasEntityShadows = FillBand(gd, ShadowBand.Entities, _entitiesTarget!);

        gd.SetRenderTarget(null);
    }

    private bool FillBand(GraphicsDevice gd, ShadowBand band, RenderTarget2D target)
    {
        bool any = false;
        foreach (var item in _items) if (item.Band == band) { any = true; break; }
        if (!any) return false;

        gd.SetRenderTarget(target);
        gd.Clear(Color.Transparent);
        foreach (var item in _items)
            if (item.Band == band) DrawQuad(gd, item.Quad, item.Plate);
        return true;
    }

    public static Point AutoShadowSize(IShadowCaster caster, int cellWidth)
    {
        int baseWidth = caster.OpaqueWidth > 0 ? caster.OpaqueWidth : cellWidth;
        float w = baseWidth * MathF.Max(caster.ShadowScale, 0f);
        return new Point((int)MathF.Round(w), (int)MathF.Round(w * GroundSquash));
    }

    public static Point ShadowSize(IShadowCaster caster, int cellWidth)
    {
        var auto = AutoShadowSize(caster, cellWidth);
        int w = caster.ShadowWidth ?? auto.X;
        int h = caster.ShadowHeight
                ?? (caster.ShadowWidth.HasValue ? (int)MathF.Round(w * GroundSquash) : auto.Y);
        return new Point(w, h);
    }

    public static Point PlateSize(IShadowCaster caster, int cellWidth)
        => caster.ShadowTexture is { } authored
            ? new Point(authored.Width, authored.Height)
            : ShadowSize(caster, cellWidth);

    public static Rectangle ShadowQuad(Point size, Vector2 foot, Point offset)
    {
        if (size.X < 1 || size.Y < 1) return Rectangle.Empty;

        float cx = foot.X + offset.X, cy = foot.Y + offset.Y;
        return new Rectangle(
            (int)MathF.Floor(cx - size.X / 2f + 0.5f), (int)MathF.Floor(cy - size.Y / 2f + 0.5f),
            size.X, size.Y);
    }

    private void DrawQuad(GraphicsDevice gd, Rectangle q, Texture2D texture)
    {
        var black = Color.Black;
        _verts[0] = new VertexPositionColorTexture(new Vector3(q.Left, q.Top, 0), black, new Vector2(0, 0));
        _verts[1] = new VertexPositionColorTexture(new Vector3(q.Right, q.Top, 0), black, new Vector2(1, 0));
        _verts[2] = new VertexPositionColorTexture(new Vector3(q.Left, q.Bottom, 0), black, new Vector2(0, 1));
        _verts[3] = new VertexPositionColorTexture(new Vector3(q.Right, q.Bottom, 0), black, new Vector2(1, 1));

        _effect!.Texture = texture;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _verts, 0, 4, QuadIndices, 0, 2);
        }
    }

    public void Composite(SpriteBatch sb, ShadowBand band, int width, int height, float opacity)
    {
        var target = band switch
        {
            ShadowBand.Below => HasBelowShadows ? _belowTarget : null,
            ShadowBand.Entities => HasEntityShadows ? _entitiesTarget : null,
            _ => null,
        };
        if (target == null) return;

        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);
        sb.Draw(target, new Rectangle(0, 0, width, height), Color.White * opacity);
        sb.End();
    }

    private void EnsureResources(GraphicsDevice gd, int width, int height)
    {
        if (_belowTarget == null || _belowTarget.Width != width || _belowTarget.Height != height)
        {
            _belowTarget?.Dispose();
            _entitiesTarget?.Dispose();
            _belowTarget = new RenderTarget2D(gd, width, height);
            _entitiesTarget = new RenderTarget2D(gd, width, height);
        }

        if (_effect == null)
        {
            _effect = new BasicEffect(gd)
            {
                TextureEnabled = true,
                VertexColorEnabled = true,
                World = Matrix.Identity,
            };
        }
    }

    private Texture2D ShapePlate(GraphicsDevice gd, Point size, float radius)
    {
        var key = (size.X, size.Y, Radius: ClampRadius(radius), ShadowSoftness);
        if (_shapes.TryGetValue(key, out var cached)) return cached;

        if (_shapes.Count >= ShapeCacheLimit)
        {
            foreach (var t in _shapes.Values) t.Dispose();
            _shapes.Clear();
        }

        var tex = new Texture2D(gd, size.X, size.Y);
        tex.SetData(BakeRoundedRect(size.X, size.Y, key.Radius, ShadowSoftness));
        _shapes[key] = tex;
        return tex;
    }

    private Texture2D OpaquePlate(GraphicsDevice gd, Texture2D authored)
    {
        if (_opaque.TryGetValue(authored, out var cached)) return cached;

        foreach (var dead in new List<Texture2D>(_opaque.Keys))
            if (dead.IsDisposed) { _opaque[dead].Dispose(); _opaque.Remove(dead); }

        var src = new Color[authored.Width * authored.Height];
        authored.GetData(src);
        var tex = new Texture2D(gd, authored.Width, authored.Height);
        tex.SetData(ForceOpaqueAlpha(src));
        _opaque[authored] = tex;
        return tex;
    }

    public static Color[] ForceOpaqueAlpha(Color[] src)
    {
        var dst = new Color[src.Length];
        for (int i = 0; i < src.Length; i++)
            dst[i] = src[i].A > 0 ? new Color(0, 0, 0, 255) : new Color(0, 0, 0, 0);
        return dst;
    }

    public static float ClampRadius(float radius) => MathHelper.Clamp(radius, 0f, 1f);

    public static Color[] BakeRoundedRect(int width, int height, float radius, float softness)
    {
        if (width < 1 || height < 1) return Array.Empty<Color>();

        float feather = MathF.Max(MathHelper.Clamp(softness, 0f, 1f) * 0.75f, 1e-4f);
        float r = ClampRadius(radius);

        var pixels = new Color[width * height];
        float halfW = width / 2f, halfH = height / 2f;

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f - halfW) / halfW;
                float v = (y + 0.5f - halfH) / halfH;

                float qx = MathF.Abs(u) - (1f - r);
                float qy = MathF.Abs(v) - (1f - r);
                float outside = MathF.Sqrt(MathF.Max(qx, 0f) * MathF.Max(qx, 0f)
                                         + MathF.Max(qy, 0f) * MathF.Max(qy, 0f));
                float dist = outside + MathF.Min(MathF.Max(qx, qy), 0f) - r;

                float s = MathHelper.Clamp(-dist / feather, 0f, 1f);
                float f = s * s * (3f - 2f * s);
                pixels[y * width + x] = new Color(f, f, f, f);
            }

        return pixels;
    }

    public void Dispose()
    {
        _belowTarget?.Dispose();
        _entitiesTarget?.Dispose();
        _effect?.Dispose();
        foreach (var t in _shapes.Values) t.Dispose();
        _shapes.Clear();
        foreach (var t in _opaque.Values) t.Dispose();
        _opaque.Clear();
    }
}
