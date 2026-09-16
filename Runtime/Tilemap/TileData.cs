namespace PixelCore.Runtime.Tilemap;

public struct TileData
{
    public int TileId;

    public static TileData Empty => new() { TileId = -1 };

    public bool IsEmpty => TileId < 0;
}
