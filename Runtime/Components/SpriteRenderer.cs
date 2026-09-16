using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Assets;

namespace PixelCore.Runtime.Components;

[ComponentCategory(ComponentCategories.Render)]
public class SpriteRenderer : Component, IRenderable, Rendering.IShadowCaster
{
    public bool CastShadow { get; set; }

    public string? ShadowTexturePath { get; set; }

    public Texture2D? ShadowTexture { get; set; }

    public string? UnresolvedShadowTextureRef { get; set; }

    public void SetShadowTexture(string? contentRelativePath)
    {
        if (string.IsNullOrEmpty(contentRelativePath))
        {
            ShadowTexturePath = null;
            ShadowTexture = null;
            UnresolvedShadowTextureRef = null;
            return;
        }

        ShadowTexturePath = contentRelativePath;
        ShadowTexture = Assets.TextureLoader.Instance.Load(contentRelativePath);
        UnresolvedShadowTextureRef = null;
    }

    public float ShadowScale { get; set; } = 1f;

    public int? ShadowWidth { get; set; }

    public int? ShadowHeight { get; set; }

    public float ShadowRadius { get; set; } = 1f;

    public Point ShadowOffset { get; set; }

    public int OpaqueWidth => _cachedAtlas?.OpaqueWidth ?? 0;

    public Texture2D? Texture { get; set; }
    public Color Color { get; set; } = Color.White;

    public Color? ColorOverride { get; set; }

    public Color? OutlineOverride { get; set; }

    public FrameOutput? FrameOverride { get; set; }

    public readonly struct FrameOutput
    {
        public readonly Texture2D? Texture;
        public readonly Rectangle SourceRect;
        public readonly float PivotX;
        public readonly float PivotY;

        public FrameOutput(Texture2D? texture, Rectangle sourceRect, float pivotX, float pivotY)
        {
            Texture = texture; SourceRect = sourceRect; PivotX = pivotX; PivotY = pivotY;
        }
    }

    public Texture2D? EffectiveTexture => FrameOverride.HasValue ? FrameOverride.Value.Texture : Texture;

    public Rectangle? EffectiveSourceRect
        => FrameOverride.HasValue ? FrameOverride.Value.SourceRect : SourceRect;

    public float EffectivePivotX => FrameOverride.HasValue ? FrameOverride.Value.PivotX : PivotX;

    public float EffectivePivotY => FrameOverride.HasValue ? FrameOverride.Value.PivotY : PivotY;

    public (string? Atlas, string? Texture, string? Slice)? UnresolvedSpriteRef { get; set; }

    public Vector2 Origin { get; set; } = Vector2.Zero;
    public bool FlipX { get; set; }
    public bool FlipY { get; set; }

    public int RenderLayer { get; set; } = RenderLayers.Entities;

    public float SortOffset { get; set; } = 0f;

    public float SortY
    {
        get
        {
            if (RenderLayer != RenderLayers.Entities) return SortOffset;
            var t = Entity.GetComponent<Transform>();
            if (t == null) return SortOffset;
            return t.Position.Y + SortOffset;
        }
    }

    public Vector2? DrawSize { get; set; }

    public Vector2 VisualScale { get; set; } = Vector2.One;

    public float EmissiveIntensity { get; set; }

    public Vector2 GetNativeSize()
    {
        var src = EffectiveSourceRect;
        if (src.HasValue)
            return new Vector2(src.Value.Width, src.Value.Height);
        var tex = EffectiveTexture;
        return tex != null ? new Vector2(tex.Width, tex.Height) : Vector2.Zero;
    }

    public Vector2 GetDrawSize() => DrawSize ?? GetNativeSize();

    public Rectangle? SourceRect { get; set; }

    public string? TexturePath { get; set; }

    public string? AtlasPath { get; set; }

    public int SliceIndex { get; set; } = -1;

    public string? SliceName { get; set; }

    public float PivotX { get; set; } = 0.5f;
    public float PivotY { get; set; } = 1f;

    private SpriteAtlas? _cachedAtlas;

    public static SpriteBatch? SpriteBatch { get; set; }

    public void SetSprite(string atlasPath, string sliceName)
    {
        var fullPath = System.IO.Path.Combine(Assets.TextureLoader.Instance.BasePath, atlasPath);
        _cachedAtlas = SpriteAtlas.Load(fullPath);
        if (_cachedAtlas == null) return;

        AtlasPath = atlasPath;
        SliceName = sliceName;

        var texturePath = atlasPath.Replace(".atlas", ".png");
        Texture = TextureLoader.Instance.Load(texturePath);

        var slice = _cachedAtlas.GetSliceByName(sliceName);
        if (slice != null)
        {
            SourceRect = new Rectangle(slice.X, slice.Y, slice.Width, slice.Height);
            PivotX = slice.PivotX;
            PivotY = slice.PivotY;
        }
    }

    public void SetSlice(string sliceName)
    {
        if (_cachedAtlas == null) return;

        SliceName = sliceName;
        var slice = _cachedAtlas.GetSliceByName(sliceName);
        if (slice != null)
        {
            SourceRect = new Rectangle(slice.X, slice.Y, slice.Width, slice.Height);
            PivotX = slice.PivotX;
            PivotY = slice.PivotY;
        }
    }

    public void Render(SpriteBatch spriteBatch)
    {
        if (OutlineOverride is { } line) RenderOutline(spriteBatch, line);
        RenderTinted(spriteBatch, ColorOverride ?? Color);
    }

    private void RenderOutline(SpriteBatch spriteBatch, Color color)
    {
        RenderTintedOffset(spriteBatch, color, new Point(-1, 0));
        RenderTintedOffset(spriteBatch, color, new Point(1, 0));
        RenderTintedOffset(spriteBatch, color, new Point(0, -1));
        RenderTintedOffset(spriteBatch, color, new Point(0, 1));
    }

    public Rectangle GetDestRect(Transform transform)
    {
        var size = GetDrawSize() * VisualScale;

        return new Rectangle(
            (int)MathF.Floor(transform.Position.X - size.X * EffectivePivotX + 0.5f),
            (int)MathF.Floor(transform.Position.Y - size.Y * EffectivePivotY + 0.5f),
            (int)size.X,
            (int)size.Y
        );
    }

    public bool TryGetShadowSprite(out Texture2D texture, out Rectangle? sourceRect,
        out Rectangle destRect, out bool flipX)
    {
        var tex = EffectiveTexture;
        texture = tex!;
        sourceRect = EffectiveSourceRect;
        destRect = default;
        flipX = FlipX;
        var transform = tex != null ? Entity.GetComponent<Transform>() : null;
        if (transform == null) return false;
        destRect = GetDestRect(transform);
        return true;
    }

    public void RenderSmooth(SpriteBatch spriteBatch, Vector2 position, Vector2 camOrigin, float pixelScale,
        bool snapToScreenPixel = true)
    {
        var tex = EffectiveTexture;
        if (tex == null) return;

        var size = GetDrawSize() * VisualScale;
        var native = GetNativeSize();
        if (native.X <= 0 || native.Y <= 0) return;

        var effects = SpriteEffects.None;
        if (FlipX) effects |= SpriteEffects.FlipHorizontally;
        if (FlipY) effects |= SpriteEffects.FlipVertically;

        var topLeft = position - size * new Vector2(EffectivePivotX, EffectivePivotY);
        if (snapToScreenPixel)
            topLeft = camOrigin + new Vector2(
                MathF.Floor((topLeft.X - camOrigin.X) * pixelScale + 0.5f) / pixelScale,
                MathF.Floor((topLeft.Y - camOrigin.Y) * pixelScale + 0.5f) / pixelScale);

        var transform = Entity.GetComponent<Transform>();
        spriteBatch.Draw(
            tex,
            topLeft,
            EffectiveSourceRect,
            ColorOverride ?? Color,
            transform?.Rotation ?? 0f,
            Vector2.Zero,
            new Vector2(size.X / native.X, size.Y / native.Y),
            effects,
            0
        );
    }

    private void RenderTintedOffset(SpriteBatch spriteBatch, Color tint, Point offset)
    {
        var tex = EffectiveTexture;
        var transform = Entity.GetComponent<Transform>();
        if (tex == null || transform == null) return;

        var r = GetDestRect(transform);
        r.X += offset.X;
        r.Y += offset.Y;

        var effects = SpriteEffects.None;
        if (FlipX) effects |= SpriteEffects.FlipHorizontally;
        if (FlipY) effects |= SpriteEffects.FlipVertically;

        spriteBatch.Draw(tex, r, EffectiveSourceRect, tint,
            transform.Rotation, Vector2.Zero, effects, 0);
    }

    public void RenderTinted(SpriteBatch spriteBatch, Color tint)
    {
        var tex = EffectiveTexture;
        if (tex == null) return;

        var transform = Entity.GetComponent<Transform>();
        if (transform == null) return;

        var destRect = GetDestRect(transform);

        var effects = SpriteEffects.None;
        if (FlipX) effects |= SpriteEffects.FlipHorizontally;
        if (FlipY) effects |= SpriteEffects.FlipVertically;

        spriteBatch.Draw(
            tex,
            destRect,
            EffectiveSourceRect,
            tint,
            transform.Rotation,
            Vector2.Zero,
            effects,
            0
        );
    }
}
