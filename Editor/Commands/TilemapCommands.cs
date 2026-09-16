using System;
using System.Collections.Generic;
using System.Linq;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Serialization;
using PixelCore.Runtime.Tilemap;

namespace PixelCore.Editor.Commands;

public static class TilemapTarget
{
    public static TilemapRenderer? Find(Scene? scene)
    {
        if (scene == null) return null;
        foreach (var entity in scene.Entities)
            if (entity.GetComponent<TilemapRenderer>() is { } map) return map;
        return null;
    }

    public static TilemapLayer? Layer(TilemapRenderer? map, int index, string name)
    {
        if (map == null) return null;
        if (index >= 0 && index < map.Layers.Count && map.Layers[index].Name == name) return map.Layers[index];
        return map.GetLayer(name);
    }
}

public readonly record struct LayerBounds(int OriginX, int OriginY, int Width, int Height)
{
    public static LayerBounds Of(TilemapLayer layer) => new(layer.OriginX, layer.OriginY, layer.Width, layer.Height);

    public void ApplyTo(TilemapLayer layer) => layer.SetBounds(OriginX, OriginY, Width, Height);
}

public sealed class TileStroke
{
    private readonly Dictionary<(int x, int y), TileData> _before = new();
    private readonly List<(int x, int y)> _order = new();

    public int LayerIndex { get; }
    public string LayerName { get; }
    public LayerBounds BoundsBefore { get; }
    public int Count => _before.Count;

    public TileStroke(int layerIndex, TilemapLayer layer)
    {
        LayerIndex = layerIndex;
        LayerName = layer.Name;
        BoundsBefore = LayerBounds.Of(layer);
    }

    public void Record(int x, int y, TileData before)
    {
        if (_before.TryAdd((x, y), before)) _order.Add((x, y));
    }

    public TileEditCommand? ToCommand(Scene scene, TilemapLayer layer, string description)
    {
        var boundsNow = LayerBounds.Of(layer);
        bool grew = boundsNow != BoundsBefore;

        var cells = new List<TileCellEdit>();
        foreach (var (x, y) in _order)
        {
            int before = _before[(x, y)].TileId;
            int after = layer.GetTile(x, y).TileId;
            if (before != after) cells.Add(new TileCellEdit(x, y, before, after));
        }

        if (cells.Count == 0)
        {
            if (grew) BoundsBefore.ApplyTo(layer);
            return null;
        }
        return new TileEditCommand(scene, LayerIndex, LayerName, cells, description,
                                   grew ? BoundsBefore : null, grew ? boundsNow : null);
    }
}

public sealed class TileEditCommand : ICommand
{
    private readonly Scene _scene;
    private readonly int _layerIndex;
    private readonly string _layerName;
    private readonly List<TileCellEdit> _cells;
    private readonly LayerBounds? _boundsBefore, _boundsAfter;

    public string Description { get; }
    public int CellCount => _cells.Count;

    public TileEditCommand(Scene scene, int layerIndex, string layerName, List<TileCellEdit> cells, string description,
                           LayerBounds? boundsBefore = null, LayerBounds? boundsAfter = null)
    {
        _scene = scene;
        _layerIndex = layerIndex;
        _layerName = layerName;
        _cells = cells;
        Description = description;
        _boundsBefore = boundsBefore;
        _boundsAfter = boundsAfter;
    }

    public void Execute()
    {
        var layer = Resolve();
        if (layer == null) return;
        _boundsAfter?.ApplyTo(layer);
        foreach (var cell in _cells) layer.SetTile(cell.X, cell.Y, cell.After);
    }

    public void Undo()
    {
        var layer = Resolve();
        if (layer == null) return;
        foreach (var cell in _cells) layer.SetTile(cell.X, cell.Y, cell.Before);
        _boundsBefore?.ApplyTo(layer);
    }

    private TilemapLayer? Resolve()
    {
        var layer = TilemapTarget.Layer(TilemapTarget.Find(_scene), _layerIndex, _layerName);
        if (layer == null)
            Console.Error.WriteLine($"[Undo] tilemap layer '{_layerName}' is gone - '{Description}' changed nothing");
        return layer;
    }
}

public sealed class TilemapSnapshot
{
    public int TileSize { get; init; }
    public List<TilemapLayerState> Layers { get; init; } = new();

    public static TilemapSnapshot Capture(TilemapRenderer map)
    {
        var snapshot = new TilemapSnapshot { TileSize = map.TileSize };
        foreach (var layer in map.Layers) snapshot.Layers.Add(TilemapLayerState.Capture(layer));
        return snapshot;
    }

    public void ApplyTo(TilemapRenderer map)
    {
        map.TileSize = TileSize;
        map.Layers.Clear();
        foreach (var state in Layers) map.Layers.Add(state.Build());
    }

    public bool SameAs(TilemapSnapshot other)
    {
        if (TileSize != other.TileSize || Layers.Count != other.Layers.Count) return false;
        for (int i = 0; i < Layers.Count; i++)
            if (!Layers[i].SameAs(other.Layers[i])) return false;
        return true;
    }
}

public sealed class TilemapLayerState
{
    public string Name { get; init; } = "";
    public int OriginX { get; init; }
    public int OriginY { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public Tileset? Tileset { get; init; }
    public bool HasCollision { get; init; }
    public int DrawOrder { get; init; }
    public int RenderLayer { get; init; }
    public int[] Tiles { get; init; } = Array.Empty<int>();

    public static TilemapLayerState Capture(TilemapLayer layer)
    {
        var tiles = new int[layer.Width * layer.Height];
        for (int y = 0; y < layer.Height; y++)
            for (int x = 0; x < layer.Width; x++)
                tiles[y * layer.Width + x] = layer.GetTile(layer.OriginX + x, layer.OriginY + y).TileId;
        return new TilemapLayerState
        {
            Name = layer.Name, OriginX = layer.OriginX, OriginY = layer.OriginY, Width = layer.Width, Height = layer.Height,
            Tileset = layer.Tileset, HasCollision = layer.HasCollision, DrawOrder = layer.DrawOrder,
            RenderLayer = layer.RenderLayer, Tiles = tiles,
        };
    }

    public TilemapLayer Build()
    {
        var layer = new TilemapLayer(Name, Width, Height, OriginX, OriginY)
        {
            Tileset = Tileset, HasCollision = HasCollision, DrawOrder = DrawOrder, RenderLayer = RenderLayer,
        };
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                layer.SetTile(OriginX + x, OriginY + y, Tiles[y * Width + x]);
        return layer;
    }

    public bool SameAs(TilemapLayerState other)
        => Name == other.Name && OriginX == other.OriginX && OriginY == other.OriginY
           && Width == other.Width && Height == other.Height
           && ReferenceEquals(Tileset, other.Tileset) && HasCollision == other.HasCollision
           && DrawOrder == other.DrawOrder && RenderLayer == other.RenderLayer
           && Tiles.AsSpan().SequenceEqual(other.Tiles);
}

public sealed class TilemapStructureCommand : ICommand
{
    private readonly Scene _scene;
    private readonly TilemapSnapshot _before, _after;

    public string Description { get; }

    public TilemapStructureCommand(Scene scene, TilemapSnapshot before, TilemapSnapshot after, string description)
    {
        _scene = scene;
        _before = before;
        _after = after;
        Description = description;
    }

    public void Execute() => Apply(_after);
    public void Undo() => Apply(_before);

    private void Apply(TilemapSnapshot snapshot)
    {
        var map = TilemapTarget.Find(_scene);
        if (map == null)
        {
            Console.Error.WriteLine($"[Undo] the tilemap is gone - '{Description}' changed nothing");
            return;
        }
        snapshot.ApplyTo(map);
    }
}

public sealed class CreateTilemapCommand : ICommand
{
    private readonly Scene _scene;
    private TilemapSnapshot _state;

    public string Description => "Create tilemap";

    public CreateTilemapCommand(Scene scene, TilemapSnapshot initial)
    {
        _scene = scene;
        _state = initial;
    }

    public void Execute()
    {
        if (TilemapTarget.Find(_scene) != null)
        {
            Console.Error.WriteLine("[Tilemap] ✘ this scene already has a tilemap - a scene saves one, so a second was not created");
            return;
        }
        var map = _scene.CreateEntity("Tilemap").AddComponent<TilemapRenderer>();
        _state.ApplyTo(map);
    }

    public void Undo()
    {
        var map = TilemapTarget.Find(_scene);
        if (map == null) return;
        _state = TilemapSnapshot.Capture(map);
        _scene.DestroyEntity(map.Entity);
    }
}

public static class TilemapEditing
{
    public static bool CreateTilemap(EditorState state, Scene scene, int tileSize, int width, int height)
    {
        if (TilemapTarget.Find(scene) != null)
        {
            Console.Error.WriteLine("[Tilemap] ✘ this scene already has a tilemap - a scene saves one");
            return false;
        }
        var initial = new TilemapSnapshot { TileSize = Math.Max(1, tileSize) };
        initial.Layers.Add(TilemapLayerState.Capture(
            new TilemapLayer("Ground", Math.Max(1, width), Math.Max(1, height)) { HasCollision = false }));
        state.ExecuteCommand(new CreateTilemapCommand(scene, initial));
        return true;
    }

    public static bool ChangeStructure(EditorState state, Scene scene, TilemapRenderer map, string description,
                                       Action<TilemapRenderer> change)
    {
        var before = TilemapSnapshot.Capture(map);
        change(map);
        var after = TilemapSnapshot.Capture(map);
        if (after.SameAs(before)) return false;
        state.AddExecutedCommand(new TilemapStructureCommand(scene, before, after, description));
        return true;
    }

    public static bool FinishStroke(EditorState state, Scene scene, TileStroke stroke, TilemapLayer layer, string description)
    {
        var command = stroke.ToCommand(scene, layer, description);
        if (command == null) return false;
        state.AddExecutedCommand(command);
        return true;
    }

    public static void PaintStamp(TilemapLayer layer, int x, int y, int[,] stamp, TileStroke stroke)
    {
        if (!HasTile(stamp)) return;
        layer.Include(x, y, x + stamp.GetLength(0) - 1, y + stamp.GetLength(1) - 1);
        TileOps.Stamp(layer, x, y, stamp, stroke.Record);
    }

    public static void FillRectangle(TilemapLayer layer, int x0, int y0, int x1, int y1, int[,] stamp, TileStroke stroke)
    {
        if (!HasTile(stamp)) return;
        layer.Include(x0, y0, x1, y1);
        TileOps.FillRect(layer, x0, y0, x1, y1, stamp, stroke.Record);
    }

    public static void PaintTerrain(TilemapLayer layer, TilesetTerrain terrain, int x, int y, int terrainIndex, TileStroke stroke)
    {
        if (terrainIndex >= 0) layer.Include(x - 1, y - 1, x + 1, y + 1);
        TerrainBrush.Paint(layer, terrain, x, y, terrainIndex, stroke.Record);
    }

    public static void Erase(TilemapLayer layer, int x, int y, TileStroke stroke)
        => TileOps.ClearRect(layer, x, y, x, y, stroke.Record);

    public static void Fill(TilemapLayer layer, int x, int y, TileData tile, TileStroke stroke)
        => TileOps.FloodFill(layer, x, y, tile, stroke.Record);

    public static TilemapLayer AddLayer(TilemapRenderer map, TilemapLayer? like)
    {
        var layer = new TilemapLayer(UniqueLayerName(map, "Layer"), like?.Width ?? 40, like?.Height ?? 30,
                                     like?.OriginX ?? 0, like?.OriginY ?? 0)
        {
            Tileset = like?.Tileset,
            HasCollision = false,
            RenderLayer = like?.RenderLayer ?? RenderLayers.Floor,
            DrawOrder = map.Layers.Count == 0 ? 0 : map.Layers.Max(l => l.DrawOrder) + 1,
        };
        map.Layers.Add(layer);
        return layer;
    }

    public static bool MoveLayer(TilemapRenderer map, TilemapLayer layer, int direction)
    {
        var ordered = map.Renderables.Cast<TilemapLayer>().ToList();
        for (int i = 0; i < ordered.Count; i++) ordered[i].DrawOrder = i;
        int at = ordered.IndexOf(layer), to = at + Math.Sign(direction);
        if (at < 0 || to < 0 || to >= ordered.Count) return false;
        (ordered[at].DrawOrder, ordered[to].DrawOrder) = (ordered[to].DrawOrder, ordered[at].DrawOrder);
        return true;
    }

    public static void ResizeLayer(TilemapRenderer map, int index, int width, int height)
        => map.Layers[index] = TileOps.Resized(map.Layers[index], width, height);

    public static string UniqueLayerName(TilemapRenderer map, string baseName, TilemapLayer? except = null)
    {
        bool Taken(string name) => map.Layers.Any(l => !ReferenceEquals(l, except) && l.Name == name);
        if (!Taken(baseName)) return baseName;
        for (int i = 2; ; i++)
            if (!Taken($"{baseName} {i}")) return $"{baseName} {i}";
    }

    public static Tileset? LoadTileset(string contentRelativePng, int tileSize, string layerName)
    {
        var texture = TextureLoader.Instance.Load(contentRelativePng);
        if (texture == null)
        {
            Console.Error.WriteLine($"[Tilemap] ✘ could not load tileset '{contentRelativePng}'");
            return null;
        }
        var tileset = new Tileset(texture, tileSize, tileSize) { SourcePath = contentRelativePng };
        tileset.Terrain = SceneSerializer.LoadTerrainFor(contentRelativePng, tileset, layerName);
        AssetRegistry.Instance.GetOrCreateId(contentRelativePng);
        return tileset;
    }

    private static bool HasTile(int[,] stamp)
    {
        foreach (var id in stamp)
            if (id != TileOps.Skip) return true;
        return false;
    }
}

public readonly record struct TileCellEdit(int X, int Y, int Before, int After);
