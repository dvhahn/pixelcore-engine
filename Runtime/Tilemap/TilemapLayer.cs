using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Physics;

namespace PixelCore.Runtime.Tilemap;

public class TilemapLayer : IRenderable
{
    public string Name { get; set; }

    public int OriginX { get; private set; }

    public int OriginY { get; private set; }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public Tileset? Tileset { get; set; }

    private TileData[,] _tiles;

    public bool HasCollision { get; set; } = true;

    public int DrawOrder { get; set; }

    public int RenderLayer { get; set; } = RenderLayers.Floor;

    public float SortY => float.MinValue;

    public float SortOffset => DrawOrder;

    internal TilemapRenderer? Owner { get; set; }

    public TilemapLayer(string name, int width, int height, int originX = 0, int originY = 0)
    {
        Name = name;
        OriginX = originX;
        OriginY = originY;
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        _tiles = NewTiles(Width, Height);
    }

    public bool Contains(int x, int y)
        => x >= OriginX && y >= OriginY && x < OriginX + Width && y < OriginY + Height;

    public TileData GetTile(int x, int y)
        => Contains(x, y) ? _tiles[x - OriginX, y - OriginY] : TileData.Empty;

    public void SetTile(int x, int y, TileData tile)
    {
        if (!Contains(x, y)) return;
        _tiles[x - OriginX, y - OriginY] = tile;
    }

    public void SetTile(int x, int y, int tileId) => SetTile(x, y, new TileData { TileId = tileId });

    public void SetBounds(int originX, int originY, int width, int height)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        if (originX == OriginX && originY == OriginY && width == Width && height == Height) return;

        var tiles = NewTiles(width, height);
        int fromX = Math.Max(originX, OriginX), toX = Math.Min(originX + width, OriginX + Width);
        int fromY = Math.Max(originY, OriginY), toY = Math.Min(originY + height, OriginY + Height);
        for (int x = fromX; x < toX; x++)
            for (int y = fromY; y < toY; y++)
                tiles[x - originX, y - originY] = _tiles[x - OriginX, y - OriginY];

        _tiles = tiles;
        OriginX = originX;
        OriginY = originY;
        Width = width;
        Height = height;
    }

    public bool Include(int minX, int minY, int maxX, int maxY)
    {
        if (minX > maxX) (minX, maxX) = (maxX, minX);
        if (minY > maxY) (minY, maxY) = (maxY, minY);

        if (Width == 0 || Height == 0)
        {
            SetBounds(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return true;
        }

        int left = Math.Min(OriginX, minX), top = Math.Min(OriginY, minY);
        int right = Math.Max(OriginX + Width - 1, maxX), bottom = Math.Max(OriginY + Height - 1, maxY);
        int width = right - left + 1, height = bottom - top + 1;
        if (left == OriginX && top == OriginY && width == Width && height == Height) return false;

        SetBounds(left, top, width, height);
        return true;
    }

    public Point WorldToTile(Vector2 worldPos, int tileSize)
    {
        return new Point(
            (int)MathF.Floor(worldPos.X / tileSize),
            (int)MathF.Floor(worldPos.Y / tileSize)
        );
    }

    public Vector2 TileToWorld(int x, int y, int tileSize)
    {
        return new Vector2(
            x * tileSize + tileSize / 2f,
            y * tileSize + tileSize / 2f
        );
    }

    public float ResolveAxis(AABB bounds, int tileSize, int axis)
    {
        if (!HasCollision) return 0f;

        int startX = (int)MathF.Floor(bounds.Min.X / tileSize);
        int startY = (int)MathF.Floor(bounds.Min.Y / tileSize);
        int endX = (int)MathF.Ceiling(bounds.Max.X / tileSize);
        int endY = (int)MathF.Ceiling(bounds.Max.Y / tileSize);

        float push = 0f;

        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {
                if (GetTile(x, y).IsEmpty) continue;

                var tileBounds = new AABB(x * tileSize, y * tileSize, tileSize, tileSize);
                if (!bounds.Overlaps(tileBounds, out float ox, out float oy)) continue;

                float p = axis == 0
                    ? (bounds.Center.X < tileBounds.Center.X ? -ox : ox)
                    : (bounds.Center.Y < tileBounds.Center.Y ? -oy : oy);

                if (MathF.Abs(p) > MathF.Abs(push)) push = p;
            }
        }

        return push;
    }

    public void Draw(SpriteBatch spriteBatch, int tileSize, Rectangle? cameraBounds = null)
    {
        if (Tileset == null) return;

        int startX = 0, startY = 0;
        int endX = Width, endY = Height;

        if (cameraBounds.HasValue)
        {
            var cam = cameraBounds.Value;
            startX = Math.Max(0, (int)MathF.Floor(cam.X / (float)tileSize) - 1 - OriginX);
            startY = Math.Max(0, (int)MathF.Floor(cam.Y / (float)tileSize) - 1 - OriginY);
            endX = Math.Min(Width, (int)MathF.Floor((cam.X + cam.Width) / (float)tileSize) + 2 - OriginX);
            endY = Math.Min(Height, (int)MathF.Floor((cam.Y + cam.Height) / (float)tileSize) + 2 - OriginY);
        }

        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {
                var tile = _tiles[x, y];
                if (tile.IsEmpty) continue;

                var sourceRect = Tileset.GetSourceRect(tile.TileId);
                var destRect = new Rectangle((OriginX + x) * tileSize, (OriginY + y) * tileSize, tileSize, tileSize);

                spriteBatch.Draw(Tileset.Texture, destRect, sourceRect, Color.White);
            }
        }
    }

    public void Render(SpriteBatch spriteBatch)
    {
        if (Owner != null) Draw(spriteBatch, Owner.TileSize);
    }

    private static TileData[,] NewTiles(int width, int height)
    {
        var tiles = new TileData[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                tiles[x, y] = TileData.Empty;
        return tiles;
    }
}
