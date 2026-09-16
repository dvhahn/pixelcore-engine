using System;
using System.Collections.Generic;
using System.IO;

namespace PixelCore.Runtime.Tilemap;

public static class TerrainSelfTest
{
    private const string FloorTerrainPath = "Content/Sprites/Tileset/NinjaAdventure/TilesetFloor.tileset";
    private const int FloorColumns = 22, FloorRows = 26;
    private const int Grass = 0, Dirt = 1;

    private static int _pass, _fail;

    public static void Run()
    {
        _pass = 0; _fail = 0;
        Console.WriteLine("=== Terrain self-test ===");

        var floor = TilesetTerrain.Load(FloorTerrainPath);
        Check($"premise: {FloorTerrainPath} exists and loads", floor != null);
        if (floor != null)
        {
            TestRealFile(floor);
            TestEveryTileRoundTrips(floor);
            TestPaintedShapesMatchExactly(floor);
            TestNeighboursArePicked(floor);
            TestVariantsAreStable(floor);
            TestMapEdgeIsSameTerrain(floor);
            TestErase(floor);
        }
        TestValidate();
        TestBrokenFile();

        Console.WriteLine($"=== Terrain: {_pass} passed, {_fail} failed ===");
    }

    private static void TestRealFile(TilesetTerrain floor)
    {
        Check("1. two terrains, grass then dirt",
            floor.Terrains.Count == 2 && floor.Terrains[0] == "grass" && floor.Terrains[1] == "dirt");
        Check("1. 56 terrain tiles", floor.Tiles.Count == 56, $"{floor.Tiles.Count}");
        Check("* 1. the file agrees with its PNG (22x26 tiles of 16px)",
            floor.Validate(FloorColumns, FloorRows, 16, "TilesetFloor") == 0);
        Check("1. control: the same file against a 10x10 tileset reports tiles outside it",
            floor.Validate(10, 10, 16, "control, expected to complain") > 0);
    }

    private static void TestEveryTileRoundTrips(TilesetTerrain floor)
    {
        int ok = 0;
        foreach (var tile in floor.Tiles)
        {
            int id = TerrainBrush.Pick(floor, tile.Terrain, tile.Peering, 0, 0);
            if (id >= 0 && SamePeering(PeeringOf(floor, id), tile.Peering)) ok++;
        }
        Check($"* 2. every tile's own neighbourhood picks a tile with that exact peering ({ok}/{floor.Tiles.Count})",
            ok == floor.Tiles.Count);

        var edge = floor.Tiles.Find(t => t.Terrain == Dirt && t.Peering[0] == Grass && t.Peering[4] == Dirt);
        Check("premise: there is a dirt tile with grass above and dirt below", edge != null);
        if (edge == null) return;
        var changed = (int[])edge.Peering.Clone();
        changed[0] = Dirt;
        int other = TerrainBrush.Pick(floor, Dirt, changed, 0, 0);
        Check("2. control: changing one side picks a tile with different peering",
            other >= 0 && !SamePeering(PeeringOf(floor, other), edge.Peering));
    }

    private static void TestPaintedShapesMatchExactly(TilesetTerrain floor)
    {
        var layer = new TilemapLayer("Ground", 24, 16) { HasCollision = false };
        FillAll(layer, floor, Grass);

        for (int x = 3; x <= 8; x++)
            for (int y = 3; y <= 7; y++)
                TerrainBrush.Paint(layer, floor, x, y, Dirt);
        for (int x = 9; x <= 16; x++)
            TerrainBrush.Paint(layer, floor, x, 5, Dirt);
        for (int i = -2; i <= 2; i++)
        {
            TerrainBrush.Paint(layer, floor, 19 + i, 10, Dirt);
            TerrainBrush.Paint(layer, floor, 19, 10 + i, Dirt);
        }
        TerrainBrush.Paint(layer, floor, 2, 13, Dirt);
        for (int i = 0; i < 4; i++)
        {
            TerrainBrush.Paint(layer, floor, 8 + i, 11 + i, Dirt);
            TerrainBrush.Paint(layer, floor, 9 + i, 11 + i, Dirt);
        }

        int dirtCells = 0, exact = 0, strayGrass = 0;
        var distinct = new HashSet<int>();
        for (int x = 0; x < layer.Width; x++)
            for (int y = 0; y < layer.Height; y++)
            {
                int terrain = TerrainBrush.TerrainAt(layer, floor, x, y);
                if (terrain != Dirt)
                {
                    if (terrain != Grass) strayGrass++;
                    continue;
                }
                dirtCells++;
                distinct.Add(layer.GetTile(x, y).TileId);
                if (SamePeering(PeeringOf(floor, layer.GetTile(x, y).TileId), Neighbourhood(layer, floor, x, y, Dirt)))
                    exact++;
            }

        Check($"premise: the shapes painted exactly their 56 dirt cells, with many different tiles ({dirtCells}, {distinct.Count})",
            dirtCells == 56 && distinct.Count >= 12);
        Check($"* 3. every dirt cell's tile matches its real neighbourhood exactly ({exact}/{dirtCells})",
            exact == dirtCells);
        Check("3. every other cell is still grass", strayGrass == 0, $"{strayGrass}");
    }

    private static void TestNeighboursArePicked(TilesetTerrain floor)
    {
        var layer = new TilemapLayer("Ground", 10, 10);
        FillAll(layer, floor, Grass);

        TerrainBrush.Paint(layer, floor, 5, 5, Dirt);
        Check("premise: a lone dirt cell has grass on its right", PeeringOf(floor, layer.GetTile(5, 5).TileId)?[2] == Grass);

        TerrainBrush.Paint(layer, floor, 6, 5, Dirt);
        Check("* 4. painting beside a cell re-picks it: its right side is now dirt",
            PeeringOf(floor, layer.GetTile(5, 5).TileId)?[2] == Dirt);
        Check("4. and the new cell's left side is dirt", PeeringOf(floor, layer.GetTile(6, 5).TileId)?[6] == Dirt);

        var before = Copy(layer);
        var changes = new List<(int x, int y, TileData tile)>();
        TerrainBrush.Paint(layer, floor, 5, 6, Dirt, (x, y, tile) => changes.Add((x, y, tile)));
        Check("4. the brush reports more than the painted cell (its neighbours changed too)", changes.Count >= 2, $"{changes.Count}");
        for (int i = changes.Count - 1; i >= 0; i--)
            layer.SetTile(changes[i].x, changes[i].y, changes[i].tile);
        Check("* 4. putting back the reported tiles restores the map exactly (what undo will do)", SameTiles(before, layer));
    }

    private static void TestVariantsAreStable(TilesetTerrain floor)
    {
        TilemapLayer Build()
        {
            var layer = new TilemapLayer("Variants", 16, 12);
            FillAll(layer, floor, Grass);
            for (int x = 4; x <= 10; x++)
                for (int y = 3; y <= 8; y++)
                    TerrainBrush.Paint(layer, floor, x, y, Dirt);
            return layer;
        }

        var a = Build();
        var b = Build();
        Check("5. painting the same map twice gives the same tiles", SameTiles(a, b));

        var grassIds = new HashSet<int>();
        for (int x = 0; x < a.Width; x++)
            for (int y = 0; y < a.Height; y++)
                if (TerrainBrush.TerrainAt(a, floor, x, y) == Grass) grassIds.Add(a.GetTile(x, y).TileId);
        Check($"* 5. plain grass uses more than one variant ({grassIds.Count})", grassIds.Count > 1);
    }

    private static void TestMapEdgeIsSameTerrain(TilesetTerrain floor)
    {
        var layer = new TilemapLayer("All dirt", 6, 6);
        FillAll(layer, floor, Dirt);

        bool allFull = true;
        for (int x = 0; x < layer.Width; x++)
            for (int y = 0; y < layer.Height; y++)
                if (!SamePeering(PeeringOf(floor, layer.GetTile(x, y).TileId), new[] { Dirt, Dirt, Dirt, Dirt, Dirt, Dirt, Dirt, Dirt }))
                    allFull = false;
        Check("* 6. a map filled with one terrain draws no border along the map edge", allFull);

        TerrainBrush.Paint(layer, floor, 3, 3, Grass);
        Check("6. control: a grass cell inside gives the dirt beside it an edge",
            PeeringOf(floor, layer.GetTile(2, 3).TileId)?[2] == Grass);
    }

    private static void TestErase(TilesetTerrain floor)
    {
        var layer = new TilemapLayer("Erase", 8, 8);
        FillAll(layer, floor, Dirt);

        TerrainBrush.Paint(layer, floor, 4, 4, -1);
        Check("7. erasing empties the cell", layer.GetTile(4, 4).IsEmpty);
        Check("* 7. the dirt beside an erased cell ends in an edge on that side",
            PeeringOf(floor, layer.GetTile(3, 4).TileId)?[2] != Dirt);
    }

    private static void TestValidate()
    {
        var bad = new TilesetTerrain
        {
            Terrains = { "a", "b" },
            Tiles =
            {
                new TerrainTile { Id = 1, Terrain = 0, Peering = new int[8] },
                new TerrainTile { Id = 1, Terrain = 0, Peering = new int[8] },
                new TerrainTile { Id = 999, Terrain = 0, Peering = new int[8] },
                new TerrainTile { Id = 2, Terrain = 5, Peering = new int[8] },
                new TerrainTile { Id = 3, Terrain = 1, Peering = new int[7] },
            },
        };
        int problems = bad.Validate(4, 4, 16, "synthetic, expected to complain");
        Check("8. four kinds of mistake are four problems", problems == 4, $"{problems}");
        Check("8. the brush skips a tile without eight peering values", bad.TilesOf(1).Count == 0);

        var clean = new TilesetTerrain
        {
            Terrains = { "a" },
            Tiles = { new TerrainTile { Id = 0, Terrain = 0, Peering = new int[8] } },
        };
        Check("* 8. control: a clean file reports nothing", clean.Validate(4, 4, 16, "control") == 0);
    }

    private static void TestBrokenFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pixelcore_terrain_{Guid.NewGuid():N}.tileset");
        try
        {
            File.WriteAllText(path, "{ \"terrains\": [\"a\"], \"tiles\": [ { \"id\": ");
            Check("9. a truncated file loads as null (and says why)", TilesetTerrain.Load(path) == null);

            File.WriteAllText(path, "{ \"terrains\": [\"a\"], \"tiles\": [] }");
            Check("* 9. control: a well-formed file loads", TilesetTerrain.Load(path) != null);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
        Check("9. no file at all is null, quietly (most tilesets have no terrains)", TilesetTerrain.Load(path) == null);
    }

    private static void FillAll(TilemapLayer layer, TilesetTerrain set, int terrain)
    {
        int seed = set.TilesOf(terrain)[0].Id;
        for (int x = 0; x < layer.Width; x++)
            for (int y = 0; y < layer.Height; y++)
                layer.SetTile(x, y, seed);
        for (int x = 0; x < layer.Width; x++)
            for (int y = 0; y < layer.Height; y++)
                TerrainBrush.Repick(layer, set, x, y);
    }

    private static int[] Neighbourhood(TilemapLayer layer, TilesetTerrain set, int x, int y, int terrain)
    {
        var want = new int[TilesetTerrain.PeeringCount];
        for (int i = 0; i < want.Length; i++)
        {
            var (dx, dy) = TerrainBrush.Offset(i);
            int nx = x + dx, ny = y + dy;
            bool inside = nx >= 0 && ny >= 0 && nx < layer.Width && ny < layer.Height;
            want[i] = inside ? TerrainBrush.TerrainAt(layer, set, nx, ny) : terrain;
        }
        TerrainBrush.Normalise(terrain, want);
        return want;
    }

    private static int[]? PeeringOf(TilesetTerrain set, int tileId) => set.Tiles.Find(t => t.Id == tileId)?.Peering;

    private static bool SamePeering(int[]? a, int[] b)
    {
        if (a == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private static TilemapLayer Copy(TilemapLayer layer)
    {
        var copy = new TilemapLayer(layer.Name, layer.Width, layer.Height);
        for (int x = 0; x < layer.Width; x++)
            for (int y = 0; y < layer.Height; y++)
                copy.SetTile(x, y, layer.GetTile(x, y));
        return copy;
    }

    private static bool SameTiles(TilemapLayer a, TilemapLayer b)
    {
        for (int x = 0; x < a.Width; x++)
            for (int y = 0; y < a.Height; y++)
                if (a.GetTile(x, y).TileId != b.GetTile(x, y).TileId) return false;
        return true;
    }

    private static void Check(string name, bool ok, string? detail = null)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name +
            (ok || detail == null ? "" : $"   [{detail}]"));
        if (ok) _pass++; else _fail++;
    }
}
