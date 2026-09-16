using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PixelCore.Runtime.Tilemap;

public static class TileRows
{
    public const string EmptyCell = ".";

    private const int ReportedCellsPerLayer = 8;

    public static List<string> Encode(TilemapLayer layer)
    {
        var rows = new List<string>(layer.Height);
        var sb = new StringBuilder(layer.Width * 3);
        for (int y = 0; y < layer.Height; y++)
        {
            sb.Clear();
            for (int x = 0; x < layer.Width; x++)
            {
                if (x > 0) sb.Append(',');
                var tile = layer.GetTile(layer.OriginX + x, layer.OriginY + y);
                if (tile.IsEmpty) sb.Append(EmptyCell);
                else sb.Append(tile.TileId.ToString(CultureInfo.InvariantCulture));
            }
            rows.Add(sb.ToString());
        }
        return rows;
    }

    public static int Decode(IReadOnlyList<string> rows, TilemapLayer layer)
    {
        int problems = 0;
        int badCells = 0;
        int ox = layer.OriginX, oy = layer.OriginY;

        if (rows.Count != layer.Height)
        {
            problems++;
            Console.Error.WriteLine(
                $"[Tilemap] ✘ layer '{layer.Name}' has {rows.Count} rows, expected {layer.Height} - " +
                "extra rows are dropped, missing rows stay empty");
        }

        int rowCount = Math.Min(rows.Count, layer.Height);
        for (int y = 0; y < rowCount; y++)
        {
            var cells = rows[y].Split(',');
            if (cells.Length != layer.Width)
            {
                problems++;
                Console.Error.WriteLine(
                    $"[Tilemap] ✘ layer '{layer.Name}' row {y} has {cells.Length} cells, expected {layer.Width} - " +
                    "extra cells are dropped, missing cells stay empty");
            }

            int cellCount = Math.Min(cells.Length, layer.Width);
            for (int x = 0; x < layer.Width; x++)
            {
                if (x >= cellCount) { layer.SetTile(ox + x, oy + y, TileData.Empty); continue; }

                var cell = cells[x].Trim();
                if (cell == EmptyCell) { layer.SetTile(ox + x, oy + y, TileData.Empty); continue; }

                if (int.TryParse(cell, NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                {
                    layer.SetTile(ox + x, oy + y, id);
                    continue;
                }

                problems++;
                badCells++;
                if (badCells <= ReportedCellsPerLayer)
                    Console.Error.WriteLine(
                        $"[Tilemap] ✘ layer '{layer.Name}' cell ({ox + x},{oy + y}) is '{cell}', not a tile id - it stays empty");
                layer.SetTile(ox + x, oy + y, TileData.Empty);
            }
        }

        for (int y = rowCount; y < layer.Height; y++)
            for (int x = 0; x < layer.Width; x++)
                layer.SetTile(ox + x, oy + y, TileData.Empty);

        if (badCells > ReportedCellsPerLayer)
            Console.Error.WriteLine(
                $"[Tilemap] ✘ layer '{layer.Name}': {badCells - ReportedCellsPerLayer} more cells were not tile ids");

        return problems;
    }
}
