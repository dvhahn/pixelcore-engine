using Microsoft.Xna.Framework.Graphics;

namespace PixelCore.Runtime.Core;

public interface IRenderable
{
    int RenderLayer { get; }

    float SortY { get; }

    float SortOffset { get; }

    void Render(SpriteBatch spriteBatch);
}

public interface IAdditive
{
    bool IsAdditive { get; }
}

public interface IAtmosphere
{
    bool InAtmosphere { get; }
}

public static class RenderLayers
{
    public const int Floor = -1000;
    public const int BelowEntities = -500;
    public const int Entities = 0;
    public const int AboveEntities = 500;
    public const int Overlay = 1000;
}
