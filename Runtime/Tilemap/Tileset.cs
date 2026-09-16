using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PixelCore.Runtime.Tilemap;

public class Tileset
{
    public Texture2D Texture { get; }
    public int TileWidth { get; }
    public int TileHeight { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int TileCount => Columns * Rows;

    public string? SourcePath { get; set; }

    public TilesetTerrain? Terrain { get; set; }

    public Tileset(Texture2D texture, int tileWidth, int tileHeight)
    {
        Texture = texture;
        TileWidth = tileWidth;
        TileHeight = tileHeight;
        Columns = texture.Width / tileWidth;
        Rows = texture.Height / tileHeight;
    }

    public Rectangle GetSourceRect(int tileId)
    {
        if (tileId < 0 || tileId >= TileCount)
            return Rectangle.Empty;

        int x = (tileId % Columns) * TileWidth;
        int y = (tileId / Columns) * TileHeight;
        return new Rectangle(x, y, TileWidth, TileHeight);
    }
}
