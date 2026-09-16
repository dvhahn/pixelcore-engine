using System;
using System.Collections.Generic;

namespace PixelCore.Runtime.Tilemap;

public static class TileOps
{
    public const int Skip = -1;

    public static int[,] StampFromSheet(int columns, int x0, int y0, int x1, int y1)
    {
        int minX = Math.Min(x0, x1), minY = Math.Min(y0, y1);
        int width = Math.Abs(x1 - x0) + 1, height = Math.Abs(y1 - y0) + 1;
        var stamp = new int[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                stamp[x, y] = (minY + y) * columns + (minX + x);
        return stamp;
    }

    public static void Stamp(TilemapLayer layer, int x, int y, int[,] stamp, Action<int, int, TileData>? onChange = null)
    {
        for (int sx = 0; sx < stamp.GetLength(0); sx++)
            for (int sy = 0; sy < stamp.GetLength(1); sy++)
                if (stamp[sx, sy] != Skip)
                    Set(layer, x + sx, y + sy, new TileData { TileId = stamp[sx, sy] }, onChange);
    }

    public static void FillRect(TilemapLayer layer, int x0, int y0, int x1, int y1, int[,] stamp,
                                Action<int, int, TileData>? onChange = null)
    {
        int minX = Math.Min(x0, x1), maxX = Math.Max(x0, x1);
        int minY = Math.Min(y0, y1), maxY = Math.Max(y0, y1);
        int width = stamp.GetLength(0), height = stamp.GetLength(1);
        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                int id = stamp[(x - minX) % width, (y - minY) % height];
                if (id != Skip) Set(layer, x, y, new TileData { TileId = id }, onChange);
            }
    }

    public static void ClearRect(TilemapLayer layer, int x0, int y0, int x1, int y1, Action<int, int, TileData>? onChange = null)
    {
        for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
            for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
                Set(layer, x, y, TileData.Empty, onChange);
    }

    public static int FloodFill(TilemapLayer layer, int x, int y, TileData replacement, Action<int, int, TileData>? onChange = null)
    {
        if (!layer.Contains(x, y)) return 0;
        if (replacement.TileId < 0) replacement = TileData.Empty;

        int target = layer.GetTile(x, y).TileId;
        if (target == replacement.TileId) return 0;

        int changed = 0;
        var pending = new Stack<(int x, int y)>();
        pending.Push((x, y));
        while (pending.Count > 0)
        {
            var (cx, cy) = pending.Pop();
            if (!layer.Contains(cx, cy)) continue;
            if (layer.GetTile(cx, cy).TileId != target) continue;

            Set(layer, cx, cy, replacement, onChange);
            changed++;
            pending.Push((cx + 1, cy));
            pending.Push((cx - 1, cy));
            pending.Push((cx, cy + 1));
            pending.Push((cx, cy - 1));
        }
        return changed;
    }

    public static List<(int x, int y)> Line(int x0, int y0, int x1, int y1)
    {
        var cells = new List<(int x, int y)>();
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            cells.Add((x0, y0));
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
        return cells;
    }

    public static TilemapLayer Resized(TilemapLayer layer, int width, int height)
    {
        var copy = new TilemapLayer(layer.Name, layer.Width, layer.Height, layer.OriginX, layer.OriginY)
        {
            Tileset = layer.Tileset,
            HasCollision = layer.HasCollision,
            DrawOrder = layer.DrawOrder,
            RenderLayer = layer.RenderLayer,
        };
        for (int x = layer.OriginX; x < layer.OriginX + layer.Width; x++)
            for (int y = layer.OriginY; y < layer.OriginY + layer.Height; y++)
                copy.SetTile(x, y, layer.GetTile(x, y));
        copy.SetBounds(layer.OriginX, layer.OriginY, Math.Max(1, width), Math.Max(1, height));
        return copy;
    }

    private static void Set(TilemapLayer layer, int x, int y, TileData tile, Action<int, int, TileData>? onChange)
    {
        if (!layer.Contains(x, y)) return;
        if (tile.TileId < 0) tile = TileData.Empty;
        var before = layer.GetTile(x, y);
        if (before.TileId == tile.TileId) return;
        layer.SetTile(x, y, tile);
        onChange?.Invoke(x, y, before);
    }
}
