using System;
using System.IO;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Physics;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Runtime.Tilemap;

public static class TilemapSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        _pass = 0; _fail = 0;
        Console.WriteLine("=== Tilemap self-test ===");

        TestCoordRoundTrip();
        TestNegativeCoords();
        TestOutOfBounds();
        TestTileSizeChange();
        TestCollisionResolve();
        TestLayerGating();
        TestPaintedTileBlocksWhateverItIs();
        TestLayersSortAsRenderables();
        TestRowsRoundTrip();
        TestLegacyTileListStillLoads();
        TestBadRowsAreReported();
        TestOriginAndGrowth();
        TestRowsKeepOrigin();
        TestNavCoversGrownLayer();

        Console.WriteLine($"=== Tilemap: {_pass} passed, {_fail} failed ===");
    }

    private static void TestCoordRoundTrip()
    {
        var layer = new TilemapLayer("L", 10, 10);
        const int ts = 32;

        var world = layer.TileToWorld(3, 4, ts);
        Check("1. tile to world gives the centre (3,4 at 32px -> 112,144)",
            world == new Vector2(112f, 144f), $"{world}");
        Check("1. world to tile round trip lands on the same cell", layer.WorldToTile(world, ts) == new Point(3, 4));

        Check("1. the top-left corner is the same tile",
            layer.WorldToTile(new Vector2(96f, 128f), ts) == new Point(3, 4));
        Check("1. just inside the bottom-right is the same tile",
            layer.WorldToTile(new Vector2(127.9f, 159.9f), ts) == new Point(3, 4));
        Check("* 1. crossing the boundary lands on the next tile (upper bound exclusive)",
            layer.WorldToTile(new Vector2(128f, 160f), ts) == new Point(4, 5));
    }

    private static void TestNegativeCoords()
    {
        var layer = new TilemapLayer("L", 10, 10);
        Check("2. -1px is tile -1, not 0",
            layer.WorldToTile(new Vector2(-1f, -1f), 32) == new Point(-1, -1));
        Check("2. -32px is tile -1", layer.WorldToTile(new Vector2(-32f, -32f), 32) == new Point(-1, -1));
        Check("2. -33px is tile -2", layer.WorldToTile(new Vector2(-33f, -33f), 32) == new Point(-2, -2));
        Check("* 2. control: differs from truncating division",
            layer.WorldToTile(new Vector2(-1f, -1f), 32) != new Point((int)(-1f / 32), (int)(-1f / 32)));
    }

    private static void TestOutOfBounds()
    {
        var layer = new TilemapLayer("L", 4, 4);
        layer.SetTile(0, 0, 1);

        Check("3. premise: the inner tile is filled", !layer.GetTile(0, 0).IsEmpty);
        Check("3. a negative index reads empty", layer.GetTile(-1, 0).IsEmpty);
        Check("3. past the width reads empty", layer.GetTile(4, 0).IsEmpty);
        Check("3. past the height reads empty", layer.GetTile(0, 4).IsEmpty);
        Check("3. far out of range reads empty without throwing", layer.GetTile(9999, -9999).IsEmpty);
    }

    private static void TestTileSizeChange()
    {
        var layer = new TilemapLayer("L", 10, 10);
        var p = new Vector2(100f, 100f);
        Check("4. on a 32px grid, (100,100) is tile (3,3)",
            layer.WorldToTile(p, 32) == new Point(3, 3));
        Check("* 4. on a 16px grid the same point is tile (6,6)",
            layer.WorldToTile(p, 16) == new Point(6, 6));

        var tm = new TilemapRenderer();
        Check("4. TileSize defaults to 32", tm.TileSize == 32);
        tm.TileSize = 0;
        Check("* 4. TileSize 0 is clamped to 1 (division would die)", tm.TileSize == 1);
        tm.TileSize = -5;
        Check("4. negatives clamp to 1 as well", tm.TileSize == 1);
    }

    private static void TestCollisionResolve()
    {
        var layer = new TilemapLayer("Walls", 10, 10);
        layer.SetTile(2, 2, 1);

        var empty = new AABB(0f, 0f, 16f, 16f);
        Check("5. empty space returns 0", layer.ResolveAxis(empty, 32, 0) == 0f && layer.ResolveAxis(empty, 32, 1) == 0f);

        var overlap = new AABB(56f, 70f, 16f, 16f);
        float pushX = layer.ResolveAxis(overlap, 32, 0);
        Check($"5. overlapping a painted tile yields a push distance (X {pushX:0.0})", pushX != 0f);
        Check("5. biting from the left pushes left (sign)", pushX < 0f, $"{pushX}");

        var layer2 = new TilemapLayer("Empty", 10, 10);
        Check("* 5. control: the same rect is 0 on an empty tilemap",
            layer2.ResolveAxis(overlap, 32, 0) == 0f);
    }

    private static void TestLayerGating()
    {
        var tm = new TilemapRenderer { TileSize = 32 };
        var deco = tm.AddLayer("Deco", 10, 10);
        deco.HasCollision = false;
        deco.SetTile(2, 2, 1);

        var box = new AABB(56f, 70f, 16f, 16f);
        Check("6. a HasCollision=false layer is not a wall", tm.ResolveAxis(box, 0) == 0f);

        var walls = tm.AddLayer("Walls", 10, 10);
        walls.SetTile(2, 2, 1);
        Check("* 6. control: the same tile on a colliding layer does push", tm.ResolveAxis(box, 0) != 0f);

        float twoLayers = tm.ResolveAxis(box, 0);
        deco.HasCollision = true;
        Check("6. the same tile on two layers does not compound the push",
            MathF.Abs(tm.ResolveAxis(box, 0) - twoLayers) < 1e-5f);
    }

    private static void TestPaintedTileBlocksWhateverItIs()
    {
        var layer = new TilemapLayer("Walls", 10, 10);
        var box = new AABB(56f, 70f, 16f, 16f);

        Check("7. premise: an empty cell does not block", layer.ResolveAxis(box, 32, 0) == 0f);
        layer.SetTile(2, 2, 0);
        Check("* 7. tile id 0 blocks (0 is a tile, not 'nothing')", layer.ResolveAxis(box, 32, 0) != 0f);
        layer.SetTile(2, 2, 57);
        Check("7. any other id blocks the same way", layer.ResolveAxis(box, 32, 0) != 0f);
        layer.SetTile(2, 2, TileData.Empty);
        Check("7. erasing the cell removes the wall", layer.ResolveAxis(box, 32, 0) == 0f);
    }

    private static void TestLayersSortAsRenderables()
    {
        var scene = new Scene("TilemapSort");
        var map = scene.CreateEntity("Tilemap").AddComponent<TilemapRenderer>();
        map.TileSize = 16;
        var ground = map.AddLayer("Ground", 4, 4);
        var detail = map.AddLayer("Detail", 4, 4);
        var canopy = map.AddLayer("Canopy", 4, 4);
        canopy.RenderLayer = RenderLayers.AboveEntities;

        var hero = scene.CreateEntity("Hero");
        hero.GetComponent<Transform>()!.Position = new Vector2(8, 8);
        var heroSprite = hero.AddComponent<SpriteRenderer>();

        var puddle = scene.CreateEntity("Puddle");
        var puddleSprite = puddle.AddComponent<SpriteRenderer>();
        puddleSprite.RenderLayer = RenderLayers.Floor;
        puddleSprite.SortOffset = -3f;

        scene.FlushPendingAdds();

        var order = scene.BuildRenderOrder();
        int At(IRenderable r) => order.IndexOf(r);

        Check("8. three layers and two sprites, each once - the tilemap component is not an entry of its own",
            order.Count == 5, $"{order.Count}");
        Check("8. ground draws before detail (DrawOrder)", At(ground) < At(detail));
        Check("8. floor layers draw before an entity-layer sprite", At(detail) < At(heroSprite));
        Check("* 8. an AboveEntities layer draws after the entity-layer sprite", At(canopy) > At(heroSprite));
        Check("* 8. a floor decal with a negative Order still draws over the ground (a layer is the bottom of its band)",
            At(puddleSprite) > At(detail), $"puddle {At(puddleSprite)}, detail {At(detail)}");

        ground.DrawOrder = 5;
        order = scene.BuildRenderOrder();
        Check("8. control: raising Ground's DrawOrder moves it above Detail", At(ground) > At(detail));
    }

    private static void TestRowsRoundTrip()
    {
        var source = new TilemapLayer("Ground", 5, 3)
        {
            HasCollision = false,
            DrawOrder = 2,
            RenderLayer = RenderLayers.AboveEntities,
        };
        source.SetTile(0, 0, 0);
        source.SetTile(4, 0, 571);
        source.SetTile(2, 1, 13);
        source.SetTile(1, 2, 9);

        var rows = TileRows.Encode(source);
        Check("9. one string per row", rows.Count == 3, $"{rows.Count}");
        Check("9. a row reads as written, '.' for empty", rows[0] == "0,.,.,.,571", rows[0]);

        var data = SceneSerializer.CaptureTilemapLayer(source, 16);
        Check("9. capture writes rows and no per-tile list", data.Rows != null && data.Tiles == null);

        var back = new TilemapLayer("Ground", 5, 3);
        SceneSerializer.ApplyTilemapData(back, data);
        Check("* 9. every cell survives capture and apply", SameTiles(source, back));
        Check("9. render layer, collision and draw order survive too",
            back.RenderLayer == RenderLayers.AboveEntities && !back.HasCollision && back.DrawOrder == 2);

        var path = Path.Combine(Path.GetTempPath(), $"pixelcore_tilemap_{Guid.NewGuid():N}.scene");
        try
        {
            var scene = new SceneData { Name = "TilemapRows" };
            scene.TilemapLayers.Add(data);
            SceneSerializer.SaveToFile(scene, path);

            var text = File.ReadAllText(path);
            Check("9. the file carries \"rows\" and no \"tiles\"", text.Contains("\"rows\"") && !text.Contains("\"tiles\""));

            var loaded = SceneSerializer.LoadFromFile(path);
            var viaFile = new TilemapLayer("Ground", 5, 3);
            if (loaded != null && loaded.TilemapLayers.Count == 1)
                SceneSerializer.ApplyTilemapData(viaFile, loaded.TilemapLayers[0]);
            Check("9. premise: the file loads with one layer", loaded != null && loaded.TilemapLayers.Count == 1);
            Check("* 9. every cell survives a trip through the file", SameTiles(source, viaFile));
            Check("9. the render layer survives the file", viaFile.RenderLayer == RenderLayers.AboveEntities,
                $"{viaFile.RenderLayer}");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void TestLegacyTileListStillLoads()
    {
        const string legacyScene =
            "{\"schemaVersion\":2,\"name\":\"Legacy\",\"entities\":[],\"tilemapLayers\":[" +
            "{\"name\":\"Old\",\"width\":3,\"height\":2,\"tileSize\":32,\"hasCollision\":true,\"drawOrder\":0," +
            "\"tiles\":[{\"x\":1,\"y\":0,\"tileId\":5,\"type\":1},{\"x\":2,\"y\":1,\"tileId\":7,\"type\":0}]}]}";

        var path = Path.Combine(Path.GetTempPath(), $"pixelcore_tilemap_legacy_{Guid.NewGuid():N}.scene");
        try
        {
            File.WriteAllText(path, legacyScene);
            var loaded = SceneSerializer.LoadFromFile(path);
            Check("10. premise: the old file loads with one layer", loaded != null && loaded.TilemapLayers.Count == 1);
            if (loaded == null || loaded.TilemapLayers.Count != 1) return;

            var layer = new TilemapLayer("Old", 3, 2);
            SceneSerializer.ApplyTilemapData(layer, loaded.TilemapLayers[0]);
            Check("10. the listed tiles land where they were", layer.GetTile(1, 0).TileId == 5 && layer.GetTile(2, 1).TileId == 7);
            Check("10. unlisted cells stay empty", layer.GetTile(0, 0).IsEmpty && layer.GetTile(0, 1).IsEmpty);
            Check("10. no render layer in the file reads as the floor", layer.RenderLayer == RenderLayers.Floor);

            var box = new AABB(2 * 32 - 8, 1 * 32 + 6, 16f, 16f);
            Check("10. a cell whose old type was 'Empty' blocks on a colliding layer (the documented change)",
                layer.ResolveAxis(box, 32, 0) != 0f);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void TestBadRowsAreReported()
    {
        var layer = new TilemapLayer("Hand", 3, 2);
        layer.SetTile(0, 0, 4);
        layer.SetTile(2, 1, 4);

        int problems = TileRows.Decode(new[] { "1,x,3", "2,2" }, layer);
        Check("11. a bad cell and a short row are two problems", problems == 2, $"{problems}");
        Check("11. good cells in a bad row still load", layer.GetTile(0, 0).TileId == 1 && layer.GetTile(2, 0).TileId == 3);
        Check("11. the bad cell stays empty", layer.GetTile(1, 0).IsEmpty);
        Check("11. a missing cell is emptied, not left holding the old tile", layer.GetTile(2, 1).IsEmpty);
        Check("11. a negative number is not a tile id", TileRows.Decode(new[] { "-1,.,.", ".,.,." }, layer) == 1);

        Check("* 11. control: clean rows report nothing", TileRows.Decode(new[] { "1,2,3", "4,.,6" }, layer) == 0);
    }

    private static void TestOriginAndGrowth()
    {
        var layer = new TilemapLayer("Offset", 3, 2, originX: -2, originY: -1);
        layer.SetTile(-2, -1, 5);
        Check("12. a tile at the origin cell reads back", layer.GetTile(-2, -1).TileId == 5);
        Check("12. the rectangle ends where it should (x -2..0, y -1..0)",
            layer.Contains(0, 0) && !layer.Contains(1, 0) && !layer.Contains(-3, -1));
        layer.SetTile(1, 0, 7);
        Check("12. writing outside the rectangle does nothing", layer.GetTile(1, 0).IsEmpty);

        var box = new AABB(-60f, -28f, 16f, 16f);
        Check("* 12. a tile left of and above the origin blocks there", layer.ResolveAxis(box, 32, 0) != 0f);
        var atZero = new TilemapLayer("Zero", 3, 2);
        atZero.SetTile(0, 0, 5);
        Check("12. control: the same box does not touch a layer that starts at cell (0, 0)", atZero.ResolveAxis(box, 32, 0) == 0f);

        var grow = new TilemapLayer("Grow", 2, 2);
        grow.SetTile(1, 1, 9);
        bool grew = grow.Include(-3, -2, 1, 1);
        Check("12. including cells left of and above grows the rectangle to start there",
            grew && grow.OriginX == -3 && grow.OriginY == -2 && grow.Width == 5 && grow.Height == 4,
            $"origin ({grow.OriginX},{grow.OriginY}) size {grow.Width}x{grow.Height}");
        Check("* 12. growing keeps every tile in its cell", grow.GetTile(1, 1).TileId == 9 && grow.GetTile(0, 0).IsEmpty);
        Check("12. including cells already inside changes nothing", !grow.Include(-1, -1, 0, 0));

        grow.SetBounds(0, 0, 2, 2);
        Check("12. shrinking back crops to the rectangle and keeps what is left",
            grow.Width == 2 && grow.OriginX == 0 && grow.GetTile(1, 1).TileId == 9);
    }

    private static void TestRowsKeepOrigin()
    {
        var source = new TilemapLayer("Offset", 3, 2, originX: -4, originY: 3);
        source.SetTile(-4, 3, 11);
        source.SetTile(-2, 4, 12);

        var data = SceneSerializer.CaptureTilemapLayer(source, 16);
        Check("13. capture writes the origin", data.OriginX == -4 && data.OriginY == 3);
        Check("13. the first row starts at the origin cell", data.Rows?[0] == "11,.,.", data.Rows?[0]);

        var back = new TilemapLayer("Offset", 1, 1);
        SceneSerializer.ApplyTilemapData(back, data);
        Check("* 13. apply puts the rectangle back where it was", back.OriginX == -4 && back.OriginY == 3 && SameTiles(source, back));

        var legacy = new TilemapLayerData { Name = "Old", Width = 2, Height = 1, Rows = new() { "3,." } };
        var old = new TilemapLayer("Old", 2, 1);
        SceneSerializer.ApplyTilemapData(old, legacy);
        Check("13. data without an origin starts at cell (0, 0)", old.OriginX == 0 && old.OriginY == 0 && old.GetTile(0, 0).TileId == 3);
    }

    private static void TestNavCoversGrownLayer()
    {
        var scene = new Scene("Nav-Offset");
        var map = scene.CreateEntity("Tilemap").AddComponent<TilemapRenderer>();
        map.TileSize = 16;
        var walls = map.AddLayer("Walls", 4, 4);
        walls.SetBounds(-10, -10, 4, 4);
        walls.SetTile(-10, -10, 1);
        scene.FlushPendingAdds();

        Nav.Nav.Invalidate();
        var grid = Nav.Nav.For(scene);
        var cell = grid.WorldToCell(new Vector2(-9 * 16 + 8, -9 * 16 + 8));
        Check("* 14. the navigation grid covers a layer that starts left of and above the origin", grid.InBounds(cell), $"{cell}");
        Nav.Nav.Invalidate();
    }

    private static bool SameTiles(TilemapLayer a, TilemapLayer b)
    {
        if (a.OriginX != b.OriginX || a.OriginY != b.OriginY || a.Width != b.Width || a.Height != b.Height) return false;
        for (int x = a.OriginX; x < a.OriginX + a.Width; x++)
            for (int y = a.OriginY; y < a.OriginY + a.Height; y++)
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
