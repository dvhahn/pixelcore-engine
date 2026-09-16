using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Components;

public enum LightShape { Point, Cone, Rect, Texture }

public enum LightActiveWhen
{
    Always,
    NightOnly,
    Switch,
}

[ComponentCategory(ComponentCategories.Render)]
public class Light2D : Component
{
    public const float DefaultRadius = 280f;

    public const float DefaultIntensity = 0.9f;

    public LightShape Shape { get; set; } = LightShape.Point;

    public Color Color { get; set; } = new Color(255, 230, 180);

    public float Intensity { get; set; } = DefaultIntensity;

    public float Additive { get; set; }

    public bool Negative { get; set; }

    public float Radius { get; set; } = DefaultRadius;

    public float Rotation { get; set; }

    public float Length { get; set; } = 160f;

    public float SpreadAngle { get; set; } = 50f;

    public Vector2 RectSize { get; set; } = new Vector2(96f, 64f);

    public float Feather { get; set; } = 0.35f;

    public float CornerRadius { get; set; }

    public float Core { get; set; }

    public bool FollowSun { get; set; }

    public LightActiveWhen ActiveWhen { get; set; } = LightActiveWhen.Always;

    public bool SwitchOn { get; set; }

    public bool OnlyOffCamera { get; set; }

    public bool? IsOn { get; set; }

    public bool? PendingOn { get; set; }

    public Rectangle WorldBounds(Vector2 position)
    {
        float r = Shape switch
        {
            LightShape.Point => Radius,
            LightShape.Cone => Length,
            LightShape.Rect => new Vector2(RectSize.X, RectSize.Y).Length() * 0.5f,
            LightShape.Texture => Texture != null
                ? new Vector2(Texture.Width * Scale.X, Texture.Height * Scale.Y).Length() * 0.5f
                : Radius,
            _ => Radius,
        };
        r = MathF.Max(r, 1f);
        return new Rectangle((int)MathF.Floor(position.X - r), (int)MathF.Floor(position.Y - r),
                             (int)MathF.Ceiling(r * 2f), (int)MathF.Ceiling(r * 2f));
    }

    public string? TexturePath { get; set; }

    public Texture2D? Texture { get; set; }

    public Vector2 Scale { get; set; } = Vector2.One;

    public string? UnresolvedTextureRef { get; set; }

    public void SetTexture(string? contentRelativePath)
    {
        if (string.IsNullOrEmpty(contentRelativePath))
        {
            TexturePath = null;
            Texture = null;
            UnresolvedTextureRef = null;
            return;
        }

        TexturePath = contentRelativePath;
        Texture = TextureLoader.Instance.Load(contentRelativePath);
        UnresolvedTextureRef = null;
    }
}
