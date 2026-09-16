using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Assets;

public class SpriteAtlas
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("TexturePath")]
    public string TexturePath { get; set; } = "";

    [JsonIgnore]
    public string? SourcePath { get; private set; }

    [JsonPropertyName("Mode")]
    public SliceMode Mode { get; set; } = SliceMode.Single;

    [JsonPropertyName("CellWidth")]
    public int CellWidth { get; set; } = 16;
    [JsonPropertyName("CellHeight")]
    public int CellHeight { get; set; } = 16;

    [JsonPropertyName("OffsetX")]
    public int OffsetX { get; set; }
    [JsonPropertyName("OffsetY")]
    public int OffsetY { get; set; }

    [JsonPropertyName("SpacingX")]
    public int SpacingX { get; set; }
    [JsonPropertyName("SpacingY")]
    public int SpacingY { get; set; }

    [JsonPropertyName("OpaqueWidth")]
    public int OpaqueWidth { get; set; }

    [JsonPropertyName("Slices")]
    public List<SpriteSlice> Slices { get; set; } = new();

    [JsonIgnore]
    public Texture2D? Texture { get; internal set; }

    public void ApplyGridSlicing()
    {
        if (Texture == null) return;

        Slices.Clear();
        Mode = SliceMode.Grid;

        int stepX = CellWidth + SpacingX;
        int stepY = CellHeight + SpacingY;
        if (CellWidth <= 0 || CellHeight <= 0 || stepX <= 0 || stepY <= 0) return;

        int index = 0;
        for (int y = OffsetY; y + CellHeight <= Texture.Height; y += stepY)
        {
            for (int x = OffsetX; x + CellWidth <= Texture.Width; x += stepX)
            {
                Slices.Add(new SpriteSlice
                {
                    Name = $"sprite_{index}",
                    X = x,
                    Y = y,
                    Width = CellWidth,
                    Height = CellHeight
                });
                index++;
            }
        }
    }

    public void LoadTexture(GraphicsDevice graphicsDevice, string basePath)
    {
        var fullPath = Path.Combine(basePath, TexturePath);

        if (!File.Exists(fullPath) && SourcePath != null)
        {
            var sibling = Path.ChangeExtension(SourcePath, ".png");
            if (File.Exists(sibling))
            {
                fullPath = sibling;
                TexturePath = Path.GetFileName(sibling);
            }
        }

        if (!File.Exists(fullPath))
        {
            Console.WriteLine($"[SpriteAtlas] ⚠ texture missing: {fullPath} (atlas {SourcePath ?? TexturePath})");
            return;
        }

        using var stream = File.OpenRead(fullPath);
        Texture = Texture2D.FromStream(graphicsDevice, stream);

        if (Mode == SliceMode.Single && Slices.Count == 0 && Texture != null)
        {
            Slices.Add(new SpriteSlice
            {
                Name = Path.GetFileNameWithoutExtension(TexturePath),
                X = 0,
                Y = 0,
                Width = Texture.Width,
                Height = Texture.Height
            });
        }
    }

    public Rectangle GetSourceRect(int index)
    {
        if (index < 0 || index >= Slices.Count)
            return Rectangle.Empty;

        var slice = Slices[index];
        return new Rectangle(slice.X, slice.Y, slice.Width, slice.Height);
    }

    public SpriteSlice? GetSliceByName(string name)
    {
        return Slices.Find(s => s.Name == name);
    }

    public void Save(string filePath)
    {
        var json = JsonSerializer.Serialize(this, AssetJsonContext.Default.SpriteAtlas);
        AtomicFile.WriteAllText(filePath, json);
    }

    public static SpriteAtlas? Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[SpriteAtlas] ⚠ atlas missing: {filePath}");
            return null;
        }
        try
        {
            var json = File.ReadAllText(filePath);
            var atlas = JsonSerializer.Deserialize(json, AssetJsonContext.Default.SpriteAtlas);
            if (atlas != null) atlas.SourcePath = Path.GetFullPath(filePath);
            return atlas;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SpriteAtlas] Load failed: {filePath} — {ex.Message}");
            return null;
        }
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SpriteAtlas))]
[JsonSerializable(typeof(System.Collections.Generic.Dictionary<string, string>))]
[JsonSerializable(typeof(AssetEntry))]
[JsonSerializable(typeof(System.Collections.Generic.Dictionary<string, AssetEntry>))]
[JsonSerializable(typeof(System.Collections.Generic.SortedDictionary<string, AssetEntry>))]
internal partial class AssetJsonContext : JsonSerializerContext { }

public enum SliceMode
{
    Single,

    Grid,

    Manual
}

public class SpriteSlice
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("X")]
    public int X { get; set; }
    [JsonPropertyName("Y")]
    public int Y { get; set; }
    [JsonPropertyName("Width")]
    public int Width { get; set; }
    [JsonPropertyName("Height")]
    public int Height { get; set; }

    [JsonPropertyName("PivotX")]
    public float PivotX { get; set; } = 0.5f;
    [JsonPropertyName("PivotY")]
    public float PivotY { get; set; } = 1f;
}
