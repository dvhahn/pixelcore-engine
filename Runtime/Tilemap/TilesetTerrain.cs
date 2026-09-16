using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixelCore.Runtime.Tilemap;

public class TilesetTerrain
{
    public const int PeeringCount = 8;

    [JsonPropertyName("tileWidth")]
    public int TileWidth { get; set; } = 16;

    [JsonPropertyName("tileHeight")]
    public int TileHeight { get; set; } = 16;

    [JsonPropertyName("terrains")]
    public List<string> Terrains { get; set; } = new();

    [JsonPropertyName("tiles")]
    public List<TerrainTile> Tiles { get; set; } = new();

    private Dictionary<int, TerrainTile>? _byId;
    private List<TerrainTile>[]? _byTerrain;

    public int TerrainOf(int tileId)
    {
        EnsureIndex();
        return _byId!.TryGetValue(tileId, out var tile) ? tile.Terrain : -1;
    }

    public IReadOnlyList<TerrainTile> TilesOf(int terrain)
    {
        EnsureIndex();
        return terrain >= 0 && terrain < _byTerrain!.Length ? _byTerrain[terrain] : Array.Empty<TerrainTile>();
    }

    public void Reindex()
    {
        _byId = null;
        _byTerrain = null;
    }

    private void EnsureIndex()
    {
        if (_byId != null) return;

        _byId = new Dictionary<int, TerrainTile>();
        _byTerrain = new List<TerrainTile>[Terrains.Count];
        for (int i = 0; i < _byTerrain.Length; i++) _byTerrain[i] = new List<TerrainTile>();

        foreach (var tile in Tiles)
        {
            if (!_byId.TryAdd(tile.Id, tile)) continue;
            if (tile.Terrain >= 0 && tile.Terrain < _byTerrain.Length && tile.Peering.Length == PeeringCount)
                _byTerrain[tile.Terrain].Add(tile);
        }
    }

    public int Validate(int columns, int rows, int tileSize, string label = "tileset")
    {
        int problems = 0;
        void Report(string message)
        {
            problems++;
            Console.Error.WriteLine($"[Tileset] ✘ {label}: {message}");
        }

        if (TileWidth != tileSize || TileHeight != tileSize)
            Report($"tiles are {TileWidth}x{TileHeight} here but the tilemap uses {tileSize}x{tileSize}");
        if (Terrains.Count == 0)
            Report("no terrains are named");

        var seen = new HashSet<int>();
        foreach (var tile in Tiles)
        {
            if (!seen.Add(tile.Id))
                Report($"tile {tile.Id} is listed twice - the first entry is used");
            if (tile.Id < 0 || tile.Id >= columns * rows)
                Report($"tile {tile.Id} is outside the tileset ({columns}x{rows} tiles)");
            if (tile.Terrain < 0 || tile.Terrain >= Terrains.Count)
                Report($"tile {tile.Id} names terrain {tile.Terrain}, but there are {Terrains.Count} terrains");
            if (tile.Peering.Length != PeeringCount)
            {
                Report($"tile {tile.Id} has {tile.Peering.Length} peering values, expected {PeeringCount} - the brush skips it");
                continue;
            }
            foreach (var p in tile.Peering)
                if (p < -1 || p >= Terrains.Count)
                {
                    Report($"tile {tile.Id} peers with terrain {p}, but there are {Terrains.Count} terrains");
                    break;
                }
        }
        return problems;
    }

    public static TilesetTerrain? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var terrain = JsonSerializer.Deserialize(File.ReadAllText(path), TilesetTerrainJsonContext.Default.TilesetTerrain);
            if (terrain == null)
                Console.Error.WriteLine($"[Tileset] ✘ {path} is empty - the layer draws, but the terrain brush has nothing to paint with");
            return terrain;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Tileset] ✘ {path} could not be read ({ex.Message}) - the layer draws, but the terrain brush has nothing to paint with");
            return null;
        }
    }
}

public class TerrainTile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("terrain")]
    public int Terrain { get; set; }

    [JsonPropertyName("peering")]
    public int[] Peering { get; set; } = Array.Empty<int>();
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TilesetTerrain))]
internal partial class TilesetTerrainJsonContext : JsonSerializerContext { }
