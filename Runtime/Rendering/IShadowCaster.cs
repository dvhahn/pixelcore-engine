using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PixelCore.Runtime.Rendering;

public interface IShadowCaster
{
    bool CastShadow { get; }

    float ShadowScale { get; }

    int? ShadowWidth { get; }

    int? ShadowHeight { get; }

    float ShadowRadius { get; }

    Point ShadowOffset { get; }

    int OpaqueWidth { get; }

    Texture2D? ShadowTexture { get; }

    bool TryGetShadowSprite(out Texture2D texture, out Rectangle? sourceRect,
        out Rectangle destRect, out bool flipX);
}
