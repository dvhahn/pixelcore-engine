using System;

namespace PixelCore.Runtime.Tilemap;

public static class TerrainBrush
{
    public static (int dx, int dy) Offset(int peering) => peering switch
    {
        0 => (0, -1),
        1 => (1, -1),
        2 => (1, 0),
        3 => (1, 1),
        4 => (0, 1),
        5 => (-1, 1),
        6 => (-1, 0),
        7 => (-1, -1),
        _ => throw new ArgumentOutOfRangeException(nameof(peering)),
    };

    public static int TerrainAt(TilemapLayer layer, TilesetTerrain set, int x, int y)
    {
        var tile = layer.GetTile(x, y);
        return tile.IsEmpty ? -1 : set.TerrainOf(tile.TileId);
    }

    public static void Paint(TilemapLayer layer, TilesetTerrain set, int x, int y, int terrain,
                             Action<int, int, TileData>? onChange = null)
    {
        if (!layer.Contains(x, y)) return;

        if (terrain < 0)
        {
            Set(layer, x, y, TileData.Empty, onChange);
        }
        else
        {
            var tiles = set.TilesOf(terrain);
            if (tiles.Count == 0)
            {
                Console.Error.WriteLine(
                    $"[Terrain] ✘ terrain {terrain} has no tiles in this tileset - nothing was painted on layer '{layer.Name}'");
                return;
            }
            if (TerrainAt(layer, set, x, y) != terrain)
                Set(layer, x, y, new TileData { TileId = tiles[0].Id }, onChange);
        }

        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                Repick(layer, set, x + dx, y + dy, onChange);
    }

    public static void Repick(TilemapLayer layer, TilesetTerrain set, int x, int y,
                              Action<int, int, TileData>? onChange = null)
    {
        if (!layer.Contains(x, y)) return;
        int terrain = TerrainAt(layer, set, x, y);
        if (terrain < 0) return;

        Span<int> want = stackalloc int[TilesetTerrain.PeeringCount];
        for (int i = 0; i < TilesetTerrain.PeeringCount; i++)
        {
            var (dx, dy) = Offset(i);
            int nx = x + dx, ny = y + dy;
            want[i] = layer.Contains(nx, ny) ? TerrainAt(layer, set, nx, ny) : terrain;
        }

        int id = Pick(set, terrain, want, x, y);
        if (id >= 0 && layer.GetTile(x, y).TileId != id)
            Set(layer, x, y, new TileData { TileId = id }, onChange);
    }

    public static int Pick(TilesetTerrain set, int terrain, ReadOnlySpan<int> want, int x, int y)
    {
        var tiles = set.TilesOf(terrain);
        if (tiles.Count == 0 || want.Length != TilesetTerrain.PeeringCount) return -1;

        Span<int> w = stackalloc int[TilesetTerrain.PeeringCount];
        want.CopyTo(w);
        Normalise(terrain, w);

        int bestScore = -1, bestFull = -1, ties = 0;
        for (int i = 0; i < tiles.Count; i++)
        {
            var (score, full) = Measure(tiles[i], terrain, w);
            if (score > bestScore || (score == bestScore && full > bestFull))
            {
                bestScore = score; bestFull = full; ties = 1;
            }
            else if (score == bestScore && full == bestFull) ties++;
        }

        int pickIndex = Variant(x, y, ties);
        for (int i = 0; i < tiles.Count; i++)
        {
            var (score, full) = Measure(tiles[i], terrain, w);
            if (score != bestScore || full != bestFull) continue;
            if (pickIndex-- == 0) return tiles[i].Id;
        }
        return -1;
    }

    internal static void Normalise(int terrain, Span<int> w)
    {
        for (int corner = 1; corner < TilesetTerrain.PeeringCount; corner += 2)
        {
            int a = corner - 1, b = (corner + 1) % TilesetTerrain.PeeringCount;
            if (w[a] == terrain && w[b] == terrain) continue;
            w[corner] = w[a] != terrain ? w[a] : w[b];
        }
    }

    internal static int Variant(int x, int y, int count)
        => count <= 1 ? 0 : (int)((uint)((x * 73856093) ^ (y * 19349663)) % (uint)count);

    private static (int score, int full) Measure(TerrainTile tile, int terrain, ReadOnlySpan<int> want)
    {
        int score = 0, full = 0;
        for (int i = 0; i < TilesetTerrain.PeeringCount; i++)
        {
            if (want[i] < 0 ? tile.Peering[i] != terrain : tile.Peering[i] == want[i]) score++;
            if (tile.Peering[i] == terrain) full++;
        }
        return (score, full);
    }

    private static void Set(TilemapLayer layer, int x, int y, TileData tile, Action<int, int, TileData>? onChange)
    {
        var before = layer.GetTile(x, y);
        if (before.TileId == tile.TileId) return;
        layer.SetTile(x, y, tile);
        onChange?.Invoke(x, y, before);
    }
}
