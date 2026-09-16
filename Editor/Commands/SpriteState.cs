using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Components;

namespace PixelCore.Editor.Commands;

internal readonly struct SpriteState
{
    public readonly string? TexturePath;
    public readonly string? AtlasPath;
    public readonly string? SliceName;
    public readonly Rectangle? SourceRect;
    public readonly Texture2D? Texture;
    public readonly float PivotX;
    public readonly float PivotY;

    private SpriteState(SpriteRenderer sr)
    {
        TexturePath = sr.TexturePath;
        AtlasPath = sr.AtlasPath;
        SliceName = sr.SliceName;
        SourceRect = sr.SourceRect;
        Texture = sr.Texture;
        PivotX = sr.PivotX;
        PivotY = sr.PivotY;
    }

    public static SpriteState Capture(SpriteRenderer sr) => new(sr);

    public static void Restore(SpriteRenderer sr, in SpriteState s)
    {
        if (!string.IsNullOrEmpty(s.AtlasPath) && !string.IsNullOrEmpty(s.SliceName))
        {
            sr.SetSprite(s.AtlasPath!, s.SliceName!);

            if (sr.AtlasPath != s.AtlasPath)
            {
                Console.WriteLine($"[Undo] sprite restore failed - could not read the atlas: {s.AtlasPath}");
                return;
            }
        }
        else
        {
            sr.AtlasPath = null;
            sr.SliceName = null;
            sr.Texture = s.Texture;
        }

        sr.TexturePath = s.TexturePath;
        sr.SourceRect = s.SourceRect;
        sr.PivotX = s.PivotX;
        sr.PivotY = s.PivotY;
    }

    public bool SameAs(in SpriteState o)
        => TexturePath == o.TexturePath && AtlasPath == o.AtlasPath && SliceName == o.SliceName
        && SourceRect.Equals(o.SourceRect) && ReferenceEquals(Texture, o.Texture)
        && PivotX == o.PivotX && PivotY == o.PivotY;
}
