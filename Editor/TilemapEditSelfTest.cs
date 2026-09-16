using System;
using System.Collections.Generic;
using System.Linq;
using PixelCore.Editor.Commands;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Tilemap;

namespace PixelCore.Editor;

public static class TilemapEditSelfTest
{
    private const string FloorTerrainPath = "Content/Sprites/Tileset/NinjaAdventure/TilesetFloor.tileset";

    private static int _pass, _fail;

    public static void Run()
    {
        _pass = 0; _fail = 0;
        Console.WriteLine("=== TilemapEdit self-test ===");

        TestStampFromSheet();
        TestStamp();
        TestFloodFill();
        TestFillRect();
        TestLine();
        TestResized();
        TestStrokeUndo();
        TestStrokeKeepsFirstBefore();
        TestStructureUndo();
        TestMoveLayer();
        TestCreateTilemap();
        TestTerrainStrokeUndo();
        TestGoneLayer();
        TestPaintingPastTheEdgeGrows();
        TestGrowthWithoutPaintIsRolledBack();
        TestTerrainPastTheEdgeHasAnEdge();

        Console.WriteLine($"=== TilemapEdit: {_pass} passed, {_fail} failed ===");
    }

    private static void TestStampFromSheet()
    {
        var stamp = TileOps.StampFromSheet(22, 4, 1, 6, 2);
        Check("1. a 3x2 selection is a 3x2 stamp", stamp.GetLength(0) == 3 && stamp.GetLength(1) == 2);
        Check("1. ids run row by row across a 22-column sheet (26, 28 / 48, 50)",
            stamp[0, 0] == 26 && stamp[2, 0] == 28 && stamp[0, 1] == 48 && stamp[2, 1] == 50,
            $"{stamp[0, 0]} {stamp[2, 0]} {stamp[0, 1]} {stamp[2, 1]}");
        var reversed = TileOps.StampFromSheet(22, 6, 2, 4, 1);
        Check("1. dragging the selection backwards gives the same stamp", reversed[0, 0] == 26 && reversed[2, 1] == 50);
    }

    private static void TestStamp()
    {
        var layer = new TilemapLayer("L", 5, 4);
        var stamp = new int[,] { { 10, 11 }, { TileOps.Skip, 13 }, { 14, 15 } };
        layer.SetTile(4, 2, 99);
        int changes = 0;
        TileOps.Stamp(layer, 3, 2, stamp, (_, _, _) => changes++);

        Check("2. the stamp lands with its top-left on the cell", layer.GetTile(3, 2).TileId == 10 && layer.GetTile(4, 3).TileId == 13);
        Check("2. a Skip cell leaves the map cell alone", layer.GetTile(4, 2).TileId == 99, $"{layer.GetTile(4, 2).TileId}");
        Check("* 2. cells past the layer edge are dropped, and only real changes are reported (3)", changes == 3, $"{changes}");

        changes = 0;
        TileOps.Stamp(layer, 3, 2, stamp, (_, _, _) => changes++);
        Check("2. stamping the same tiles again reports nothing", changes == 0, $"{changes}");
    }

    private static void TestFloodFill()
    {
        var big = new TilemapLayer("Big", 300, 300);
        int filled = TileOps.FloodFill(big, 150, 150, new TileData { TileId = 5 });
        Check("* 3. a 300x300 empty layer fills completely - the old recursive fill gave up at depth 10,000",
            filled == 90_000 && big.GetTile(0, 0).TileId == 5 && big.GetTile(299, 299).TileId == 5, $"{filled}");

        var ring = new TilemapLayer("Ring", 9, 9);
        for (int i = 2; i <= 6; i++)
        {
            ring.SetTile(i, 2, 1); ring.SetTile(i, 6, 1);
            ring.SetTile(2, i, 1); ring.SetTile(6, i, 1);
        }
        int inside = TileOps.FloodFill(ring, 4, 4, new TileData { TileId = 7 });
        Check("3. a fill stays inside the wall ring (3x3 = 9 cells)", inside == 9, $"{inside}");
        Check("3. the wall and the outside are untouched", ring.GetTile(2, 2).TileId == 1 && ring.GetTile(0, 0).IsEmpty);
        Check("3. filling with the tile already there changes nothing", TileOps.FloodFill(ring, 4, 4, new TileData { TileId = 7 }) == 0);
    }

    private static void TestFillRect()
    {
        var layer = new TilemapLayer("L", 6, 4);
        TileOps.FillRect(layer, 4, 3, 1, 0, new int[,] { { 20 }, { 21 } });
        Check("4. the stamp repeats from the rectangle's top-left (20, 21, 20, 21)",
            layer.GetTile(1, 0).TileId == 20 && layer.GetTile(2, 0).TileId == 21
            && layer.GetTile(3, 2).TileId == 20 && layer.GetTile(4, 3).TileId == 21);
        Check("4. nothing outside the rectangle is painted", layer.GetTile(0, 0).IsEmpty && layer.GetTile(5, 3).IsEmpty);

        TileOps.ClearRect(layer, 1, 0, 2, 3);
        Check("4. clearing a rectangle empties exactly it",
            layer.GetTile(1, 1).IsEmpty && layer.GetTile(2, 3).IsEmpty && layer.GetTile(3, 0).TileId == 20);
    }

    private static void TestLine()
    {
        var line = TileOps.Line(0, 0, 5, 2);
        bool contiguous = true;
        for (int i = 1; i < line.Count; i++)
            if (Math.Max(Math.Abs(line[i].x - line[i - 1].x), Math.Abs(line[i].y - line[i - 1].y)) != 1) contiguous = false;
        Check("5. a line includes both ends", line[0] == (0, 0) && line[^1] == (5, 2));
        Check("* 5. every step is one cell from the last - a fast drag leaves no gaps", contiguous && line.Count == 6, $"{line.Count}");
        Check("5. the reverse line has the same length", TileOps.Line(5, 2, 0, 0).Count == 6);
        Check("5. a line from a cell to itself is that cell", TileOps.Line(3, 3, 3, 3).Count == 1);
    }

    private static void TestResized()
    {
        var layer = new TilemapLayer("L", 4, 3) { HasCollision = false, DrawOrder = 3, RenderLayer = RenderLayers.AboveEntities };
        layer.SetTile(3, 2, 9);
        layer.SetTile(1, 1, 4);

        var grown = TileOps.Resized(layer, 6, 5);
        Check("6. growing keeps tiles where they were", grown.GetTile(3, 2).TileId == 9 && grown.GetTile(1, 1).TileId == 4);
        Check("6. new cells are empty", grown.GetTile(5, 4).IsEmpty);
        Check("6. properties carry over",
            grown.Name == "L" && !grown.HasCollision && grown.DrawOrder == 3 && grown.RenderLayer == RenderLayers.AboveEntities);

        var shrunk = TileOps.Resized(layer, 2, 2);
        Check("6. shrinking crops from the bottom-right", shrunk.Width == 2 && shrunk.GetTile(1, 1).TileId == 4);
    }

    private static void TestStrokeUndo()
    {
        var (state, scene, _, layer) = NewScene();
        var stroke = new TileStroke(0, layer);
        TileOps.Stamp(layer, 1, 1, new int[,] { { 5, 6 }, { 7, 8 } }, stroke.Record);
        var painted = TilemapLayerState.Capture(layer);

        state.ClearDirty();
        bool recorded = TilemapEditing.FinishStroke(state, scene, stroke, layer, "Paint tiles");
        Check("7. a stroke that changed cells is recorded", recorded && state.CommandHistory.CanUndo);
        Check("7. recording it marks the scene dirty", state.IsDirty);

        state.Undo();
        Check("* 7. undo clears the stroke's cells",
            layer.GetTile(1, 1).IsEmpty && layer.GetTile(2, 1).IsEmpty && layer.GetTile(1, 2).IsEmpty && layer.GetTile(2, 2).IsEmpty);
        state.Redo();
        Check("7. redo paints them again", TilemapLayerState.Capture(layer).SameAs(painted));

        var again = new TileStroke(0, layer);
        TileOps.Stamp(layer, 1, 1, new int[,] { { 5, 6 }, { 7, 8 } }, again.Record);
        Check("7. a stroke that changed nothing is not recorded", !TilemapEditing.FinishStroke(state, scene, again, layer, "Paint tiles"));
    }

    private static void TestStrokeKeepsFirstBefore()
    {
        var (state, scene, _, layer) = NewScene();
        layer.SetTile(2, 2, 3);

        var stroke = new TileStroke(0, layer);
        TileOps.Stamp(layer, 2, 2, new int[,] { { 4 } }, stroke.Record);
        TileOps.Stamp(layer, 2, 2, new int[,] { { 9 } }, stroke.Record);
        TilemapEditing.FinishStroke(state, scene, stroke, layer, "Paint tiles");

        state.Undo();
        Check("* 8. undoing a stroke that crossed a cell twice restores the tile from before the stroke (3, not 4)",
            layer.GetTile(2, 2).TileId == 3, $"{layer.GetTile(2, 2).TileId}");
    }

    private static void TestStructureUndo()
    {
        var (state, scene, map, layer) = NewScene();
        layer.SetTile(0, 0, 1);
        var before = TilemapSnapshot.Capture(map);

        bool added = TilemapEditing.ChangeStructure(state, scene, map, "Add tilemap layer", m => TilemapEditing.AddLayer(m, m.Layers[0]));
        Check("9. adding a layer is recorded", added && map.Layers.Count == 2);
        Check("9. the new layer takes the size, gets its own name and draws above",
            map.Layers[1].Width == layer.Width && map.Layers[1].Name != map.Layers[0].Name
            && map.Layers[1].DrawOrder > map.Layers[0].DrawOrder);

        TilemapEditing.ChangeStructure(state, scene, map, "Resize", m => TilemapEditing.ResizeLayer(m, 0, 12, 3));
        TilemapEditing.ChangeStructure(state, scene, map, "Rename", m => m.Layers[0].Name = "Floor");
        TilemapEditing.ChangeStructure(state, scene, map, "Wall", m => m.Layers[1].HasCollision = true);
        TilemapEditing.ChangeStructure(state, scene, map, "Band", m => m.Layers[1].RenderLayer = RenderLayers.AboveEntities);
        var after = TilemapSnapshot.Capture(map);

        for (int i = 0; i < 5; i++) state.Undo();
        Check("* 9. five undos return the tilemap to exactly where it started", TilemapSnapshot.Capture(map).SameAs(before));
        for (int i = 0; i < 5; i++) state.Redo();
        Check("9. five redos bring every change back", TilemapSnapshot.Capture(map).SameAs(after));

        Check("9. a change that changes nothing is not recorded",
            !TilemapEditing.ChangeStructure(state, scene, map, "Nothing", _ => { }));

        var probe = TilemapSnapshot.Capture(map);
        map.Layers[0].SetTile(0, 0, 2);
        Check("9. control: a one-tile difference is not 'the same'", !TilemapSnapshot.Capture(map).SameAs(probe));
    }

    private static void TestMoveLayer()
    {
        var map = new TilemapRenderer { TileSize = 16 };
        var a = map.AddLayer("A", 2, 2);
        var b = map.AddLayer("B", 2, 2);
        var c = map.AddLayer("C", 2, 2);

        TilemapEditing.MoveLayer(map, a, +1);
        Check("10. moving a layer up swaps it with the one drawn after it", DrawnOrder(map) == "B,A,C", DrawnOrder(map));
        Check("10. the front layer cannot move further up", !TilemapEditing.MoveLayer(map, c, +1) && DrawnOrder(map) == "B,A,C");

        foreach (var layer in map.Layers) layer.DrawOrder = 0;
        TilemapEditing.MoveLayer(map, a, +1);
        Check("* 10. with equal draw orders the move follows what is drawn (A goes above B)", DrawnOrder(map) == "B,A,C", DrawnOrder(map));
    }

    private static void TestCreateTilemap()
    {
        var state = new EditorState();
        var scene = new Scene("create-tilemap");
        state.CurrentScene = scene;

        Check("11. premise: the scene starts without a tilemap", TilemapTarget.Find(scene) == null);
        TilemapEditing.CreateTilemap(state, scene, 16, 20, 12);
        scene.Update(0f);
        var map = TilemapTarget.Find(scene);
        Check("11. creating gives the scene one 20x12 walkable ground layer",
            map != null && map.TileSize == 16 && map.Layers.Count == 1 && map.Layers[0].Width == 20 && !map.Layers[0].HasCollision);

        state.Undo();
        scene.Update(0f);
        Check("* 11. undo removes the tilemap", TilemapTarget.Find(scene) == null);
        state.Redo();
        scene.Update(0f);
        Check("11. redo brings it back at the same size", TilemapTarget.Find(scene) is { } again && again.Layers[0].Height == 12);

        TilemapEditing.ChangeStructure(state, scene, TilemapTarget.Find(scene)!, "Rename tilemap layer", m => m.Layers[0].Name = "Field");
        bool second = TilemapEditing.CreateTilemap(state, scene, 16, 5, 5);
        scene.Update(0f);
        Check("11. a second tilemap is refused and nothing is recorded (a scene saves one)",
            !second && scene.Entities.Count(e => e.GetComponent<TilemapRenderer>() != null) == 1
            && state.CommandHistory.NextUndoDescription == "Rename tilemap layer",
            state.CommandHistory.NextUndoDescription);
    }

    private static void TestTerrainStrokeUndo()
    {
        var terrain = TilesetTerrain.Load(FloorTerrainPath);
        Check($"premise: {FloorTerrainPath} loads", terrain != null);
        if (terrain == null) return;

        var (state, scene, _, layer) = NewScene();
        int grassSeed = terrain.TilesOf(0)[0].Id;
        for (int x = 0; x < layer.Width; x++)
            for (int y = 0; y < layer.Height; y++)
                layer.SetTile(x, y, grassSeed);
        for (int x = 0; x < layer.Width; x++)
            for (int y = 0; y < layer.Height; y++)
                TerrainBrush.Repick(layer, terrain, x, y);
        TerrainBrush.Paint(layer, terrain, 1, 2, 1);
        var before = TilemapLayerState.Capture(layer);

        var stroke = new TileStroke(0, layer);
        var line = TileOps.Line(2, 2, 5, 3);
        foreach (var (x, y) in line)
            TerrainBrush.Paint(layer, terrain, x, y, 1, stroke.Record);
        Check($"12. a terrain stroke beside existing dirt also changes that dirt's edge ({stroke.Count} cells for {line.Count} painted)",
            stroke.Count > line.Count);

        TilemapEditing.FinishStroke(state, scene, stroke, layer, "Paint terrain");
        state.Undo();
        Check("* 12. undoing the terrain stroke restores every cell, edges included", TilemapLayerState.Capture(layer).SameAs(before));
    }

    private static void TestGoneLayer()
    {
        var (_, scene, map, layer) = NewScene();
        var stroke = new TileStroke(0, layer);
        TileOps.Stamp(layer, 0, 0, new int[,] { { 1 } }, stroke.Record);
        var command = stroke.ToCommand(scene, layer, "Paint tiles");
        map.Layers.Clear();

        bool threw = false;
        try { command?.Undo(); }
        catch { threw = true; }
        Check("13. undoing against a removed layer does not throw (it says so and changes nothing)", command != null && !threw);
    }

    private static void TestPaintingPastTheEdgeGrows()
    {
        var (state, scene, _, layer) = NewScene();
        layer.SetTile(3, 3, 2);
        var before = TilemapLayerState.Capture(layer);

        var stroke = new TileStroke(0, layer);
        TilemapEditing.PaintStamp(layer, -2, -1, new int[,] { { 4 } }, stroke);
        TilemapEditing.PaintStamp(layer, 9, 7, new int[,] { { 5, 6 } }, stroke);
        Check("14. painting left of and above the layer grows it to start there",
            layer.OriginX == -2 && layer.OriginY == -1, $"({layer.OriginX},{layer.OriginY})");
        Check("14. painting right of and below grows it to take the whole stamp",
            layer.OriginX + layer.Width - 1 == 9 && layer.OriginY + layer.Height - 1 == 8, $"{layer.Width}x{layer.Height}");
        Check("* 14. the painted tiles and the old tile are all in their cells",
            layer.GetTile(-2, -1).TileId == 4 && layer.GetTile(9, 8).TileId == 6 && layer.GetTile(3, 3).TileId == 2);
        var grown = TilemapLayerState.Capture(layer);

        TilemapEditing.FinishStroke(state, scene, stroke, layer, "Paint tiles");
        state.Undo();
        Check("* 14. undo shrinks the layer back and restores it exactly", TilemapLayerState.Capture(layer).SameAs(before));
        state.Redo();
        Check("14. redo grows it again with the tiles", TilemapLayerState.Capture(layer).SameAs(grown));
    }

    private static void TestGrowthWithoutPaintIsRolledBack()
    {
        var (state, scene, _, layer) = NewScene();
        var before = TilemapLayerState.Capture(layer);
        var stroke = new TileStroke(0, layer);
        layer.Include(-5, -5, -5, -5);
        bool recorded = TilemapEditing.FinishStroke(state, scene, stroke, layer, "Paint tiles");
        Check("15. a stroke that only grew the layer is not recorded", !recorded);
        Check("* 15. and the growth is given back, so no change is left that undo cannot see",
            TilemapLayerState.Capture(layer).SameAs(before));
    }

    private static void TestTerrainPastTheEdgeHasAnEdge()
    {
        var terrain = TilesetTerrain.Load(FloorTerrainPath);
        Check($"premise: {FloorTerrainPath} loads", terrain != null);
        if (terrain == null) return;

        var (state, scene, _, layer) = NewScene();
        FillWithGrass(layer, terrain);
        var stroke = new TileStroke(0, layer);
        TilemapEditing.PaintTerrain(layer, terrain, 8, 2, 1, stroke);
        Check("16. painting terrain past the edge grows the layer around the cell",
            layer.Contains(9, 2) && layer.Contains(8, 1) && layer.Contains(8, 3));
        var peering = terrain.Tiles.Find(t => t.Id == layer.GetTile(8, 2).TileId)?.Peering;
        Check("* 16. the dirt ends in an edge on the new, empty side (not read as more dirt beyond the layer)",
            peering != null && peering[2] != 1, peering == null ? "no tile" : string.Join(",", peering));

        TilemapEditing.FinishStroke(state, scene, stroke, layer, "Paint terrain");
        state.Undo();
        Check("16. undo returns the layer to its 8x6 rectangle from cell (0, 0)",
            layer.OriginX == 0 && layer.OriginY == 0 && layer.Width == 8 && layer.Height == 6);
    }

    private static void FillWithGrass(TilemapLayer layer, TilesetTerrain terrain)
    {
        int seed = terrain.TilesOf(0)[0].Id;
        for (int x = layer.OriginX; x < layer.OriginX + layer.Width; x++)
            for (int y = layer.OriginY; y < layer.OriginY + layer.Height; y++)
                layer.SetTile(x, y, seed);
        for (int x = layer.OriginX; x < layer.OriginX + layer.Width; x++)
            for (int y = layer.OriginY; y < layer.OriginY + layer.Height; y++)
                TerrainBrush.Repick(layer, terrain, x, y);
    }

    private static (EditorState state, Scene scene, TilemapRenderer map, TilemapLayer layer) NewScene()
    {
        var state = new EditorState();
        var scene = new Scene("tilemap-edit");
        state.CurrentScene = scene;
        var map = scene.CreateEntity("Tilemap").AddComponent<TilemapRenderer>();
        map.TileSize = 16;
        var layer = map.AddLayer("Ground", 8, 6);
        layer.HasCollision = false;
        scene.Update(0f);
        return (state, scene, map, layer);
    }

    private static string DrawnOrder(TilemapRenderer map)
        => string.Join(",", map.Renderables.Cast<TilemapLayer>().Select(l => l.Name));

    private static void Check(string name, bool ok, string? detail = null)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name +
            (ok || detail == null ? "" : $"   [{detail}]"));
        if (ok) _pass++; else _fail++;
    }
}
