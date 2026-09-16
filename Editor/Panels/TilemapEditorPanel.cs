using System;
using System.Linq;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Editor.Commands;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Tilemap;
using NVec2 = System.Numerics.Vector2;
using NVec4 = System.Numerics.Vector4;

namespace PixelCore.Editor.Panels;

public class TilemapEditorPanel
{
    public enum Tool { Brush, Erase, Fill, Rectangle, Terrain, Pick }

    public bool IsOpen { get; set; } = false;

    public bool IsEditing { get; set; }

    private EditorState? _state;
    private Func<Texture2D, IntPtr>? _bindTexture;
    private Texture2D? _boundTexture;
    private IntPtr _textureId;

    private TilemapRenderer? _target;
    private int _layerIndex;
    private Tool _tool = Tool.Brush;
    private int[,] _stamp = { { 0 } };
    private int _terrain;
    private float _paletteZoom = 2f;

    private bool _selectingInPalette;
    private Point _paletteStart, _paletteEnd;

    private TileStroke? _stroke;
    private Point _strokeStart, _lastCell;
    private Point? _hoverCell;

    private int _newTileSize = 16, _newWidth = 40, _newHeight = 30;
    private TilemapLayer? _propertiesFor;
    private string _nameBuffer = "";
    private int _resizeWidth, _resizeHeight;

    public void Bind(EditorState state, Func<Texture2D, IntPtr> bindTexture)
    {
        _state = state;
        _bindTexture = bindTexture;
    }

    public void SetTarget(TilemapRenderer? tilemap)
    {
        _target = tilemap;
        _layerIndex = 0;
        _stroke = null;
    }

    public void Draw()
    {
        if (!IsOpen)
        {
            IsEditing = false;
            return;
        }

        ImGui.SetNextWindowSize(new NVec2(380, 640), ImGuiCond.FirstUseEver);
        var open = IsOpen;
        bool visible = ImGui.Begin(Icons.Map + "  Tilemap###Tilemap Editor", ref open);
        IsOpen = open;
        if (!visible)
        {
            ImGui.End();
            return;
        }

        var scene = _state?.CurrentScene;
        var map = Target();
        if (_state == null || scene == null)
        {
            ImGui.TextDisabled("No scene is open.");
        }
        else if (map == null)
        {
            DrawCreate(scene);
        }
        else
        {
            var editing = IsEditing;
            if (ImGui.Checkbox("Paint in the scene view", ref editing)) IsEditing = editing;
            ImGui.SameLine();
            ImGui.TextDisabled(IsEditing ? "(right-drag pans)" : "(clicks select)");

            ImGui.Separator();
            DrawLayers(scene, map);

            var layer = CurrentLayer(map);
            if (layer != null)
            {
                ImGui.Separator();
                DrawLayerProperties(scene, map, layer);
                ImGui.Separator();
                DrawTools(layer);
                if (layer.Tileset?.Terrain is { } terrain) DrawTerrains(terrain);
                DrawPalette(layer);
            }
        }

        ImGui.End();
    }

    private void DrawCreate(Scene scene)
    {
        ImGui.TextWrapped("This scene has no tilemap.");
        ImGui.InputInt("Tile size", ref _newTileSize);
        ImGui.InputInt("Width in tiles", ref _newWidth);
        ImGui.InputInt("Height in tiles", ref _newHeight);
        _newTileSize = Math.Clamp(_newTileSize, 1, 256);
        _newWidth = Math.Clamp(_newWidth, 1, 1024);
        _newHeight = Math.Clamp(_newHeight, 1, 1024);

        if (ImGui.Button(Icons.Plus + "  Create tilemap") &&
            TilemapEditing.CreateTilemap(_state!, scene, _newTileSize, _newWidth, _newHeight))
            IsEditing = true;
    }

    private void DrawLayers(Scene scene, TilemapRenderer map)
    {
        ImGui.TextDisabled($"Layers - front first - {map.TileSize}px tiles");

        var frontFirst = map.Layers
            .Select((layer, index) => (layer, index))
            .OrderByDescending(p => p.layer.RenderLayer)
            .ThenByDescending(p => p.layer.DrawOrder)
            .ThenByDescending(p => p.index);
        foreach (var (layer, index) in frontFirst)
        {
            string tags = (layer.HasCollision ? "  wall" : "")
                        + (layer.RenderLayer == RenderLayers.AboveEntities ? "  above"
                           : layer.RenderLayer == RenderLayers.BelowEntities ? "  below" : "");
            if (ImGui.Selectable($"{layer.Name}  {layer.Width}x{layer.Height}{tags}##layer{index}", index == _layerIndex))
                _layerIndex = index;
        }

        if (ImGui.Button(Icons.Plus + "##addLayer"))
            TilemapEditing.ChangeStructure(_state!, scene, map, "Add tilemap layer", m =>
            {
                var added = TilemapEditing.AddLayer(m, CurrentLayer(m));
                _layerIndex = m.Layers.IndexOf(added);
            });
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add a layer");

        var current = CurrentLayer(map);
        ImGui.SameLine();
        if (ImGui.Button("Up") && current != null)
            TilemapEditing.ChangeStructure(_state!, scene, map, "Move tilemap layer up", m => TilemapEditing.MoveLayer(m, current, +1));
        ImGui.SameLine();
        if (ImGui.Button("Down") && current != null)
            TilemapEditing.ChangeStructure(_state!, scene, map, "Move tilemap layer down", m => TilemapEditing.MoveLayer(m, current, -1));

        ImGui.SameLine();
        ImGui.BeginDisabled(map.Layers.Count <= 1);
        if (ImGui.Button(Icons.Trash + "##removeLayer"))
        {
            int index = _layerIndex;
            TilemapEditing.ChangeStructure(_state!, scene, map, "Remove tilemap layer", m => m.Layers.RemoveAt(index));
            _layerIndex = Math.Max(0, index - 1);
        }
        ImGui.EndDisabled();
    }

    private void DrawLayerProperties(Scene scene, TilemapRenderer map, TilemapLayer layer)
    {
        if (!ReferenceEquals(_propertiesFor, layer))
        {
            _propertiesFor = layer;
            _nameBuffer = layer.Name;
            _resizeWidth = layer.Width;
            _resizeHeight = layer.Height;
        }
        int index = _layerIndex;

        ImGui.InputText("Name", ref _nameBuffer, 64);
        if (ImGui.IsItemDeactivatedAfterEdit() && _nameBuffer.Trim().Length > 0 && _nameBuffer.Trim() != layer.Name)
        {
            var name = TilemapEditing.UniqueLayerName(map, _nameBuffer.Trim(), except: layer);
            TilemapEditing.ChangeStructure(_state!, scene, map, "Rename tilemap layer", m => m.Layers[index].Name = name);
            _propertiesFor = null;
        }

        bool wall = layer.HasCollision;
        if (ImGui.Checkbox("Blocks movement", ref wall))
            TilemapEditing.ChangeStructure(_state!, scene, map, wall ? "Make tilemap layer a wall" : "Make tilemap layer walkable",
                m => m.Layers[index].HasCollision = wall);
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Every painted tile on this layer blocks. Paint walls and ground on separate layers.");

        string[] bands = { "Ground", "Below entities", "Above entities" };
        int[] values = { RenderLayers.Floor, RenderLayers.BelowEntities, RenderLayers.AboveEntities };
        int band = Array.IndexOf(values, layer.RenderLayer);
        if (ImGui.BeginCombo("Draws as", band >= 0 ? bands[band] : $"Layer {layer.RenderLayer}"))
        {
            for (int i = 0; i < bands.Length; i++)
            {
                int value = values[i];
                if (ImGui.Selectable(bands[i], i == band))
                    TilemapEditing.ChangeStructure(_state!, scene, map, "Change tilemap layer band", m => m.Layers[index].RenderLayer = value);
            }
            ImGui.EndCombo();
        }

        if (ImGui.BeginCombo("Tileset", layer.Tileset?.SourcePath ?? "(none)"))
        {
            var paths = AssetRegistry.Instance.Entries.Values
                .Select(e => e.Path)
                .Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                            && p.Contains("Tileset", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal);
            foreach (var path in paths)
            {
                if (!ImGui.Selectable(path, path == layer.Tileset?.SourcePath)) continue;
                var tileset = TilemapEditing.LoadTileset(path, map.TileSize, layer.Name);
                if (tileset != null)
                    TilemapEditing.ChangeStructure(_state!, scene, map, "Change tilemap layer tileset", m => m.Layers[index].Tileset = tileset);
            }
            ImGui.EndCombo();
        }

        ImGui.SetNextItemWidth(90);
        ImGui.InputInt("##resizeWidth", ref _resizeWidth);
        ImGui.SameLine();
        ImGui.TextUnformatted("x");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90);
        ImGui.InputInt("##resizeHeight", ref _resizeHeight);
        _resizeWidth = Math.Clamp(_resizeWidth, 1, 1024);
        _resizeHeight = Math.Clamp(_resizeHeight, 1, 1024);
        ImGui.SameLine();
        ImGui.BeginDisabled(_resizeWidth == layer.Width && _resizeHeight == layer.Height);
        if (ImGui.Button("Resize"))
        {
            int width = _resizeWidth, height = _resizeHeight;
            TilemapEditing.ChangeStructure(_state!, scene, map, "Resize tilemap layer", m => TilemapEditing.ResizeLayer(m, index, width, height));
            _propertiesFor = null;
        }
        ImGui.EndDisabled();
        ImGui.TextDisabled($"Starts at cell ({layer.OriginX}, {layer.OriginY})");
    }

    private void DrawTools(TilemapLayer layer)
    {
        bool hasTerrain = layer.Tileset?.Terrain != null;
        if (_tool == Tool.Terrain && !hasTerrain) _tool = Tool.Brush;

        bool first = true;
        foreach (var tool in Enum.GetValues<Tool>())
        {
            if (tool == Tool.Terrain && !hasTerrain) continue;
            if (!first) ImGui.SameLine();
            first = false;
            if (ImGui.RadioButton(ToolLabel(tool), _tool == tool)) _tool = tool;
        }
        ImGui.TextDisabled(ToolHint(_tool));
    }

    private void DrawTerrains(TilesetTerrain terrain)
    {
        ImGui.TextDisabled("Terrain");
        for (int i = 0; i < terrain.Terrains.Count; i++)
        {
            if (i > 0) ImGui.SameLine();
            if (ImGui.RadioButton($"{terrain.Terrains[i]}##terrain{i}", _tool == Tool.Terrain && _terrain == i))
            {
                _terrain = i;
                _tool = Tool.Terrain;
            }
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("erase##terrainErase", _tool == Tool.Terrain && _terrain < 0))
        {
            _terrain = -1;
            _tool = Tool.Terrain;
        }
    }

    private void DrawPalette(TilemapLayer layer)
    {
        var tileset = layer.Tileset;
        if (tileset == null)
        {
            ImGui.TextDisabled("Pick a tileset to paint with.");
            return;
        }
        BindTexture(tileset.Texture);

        ImGui.SetNextItemWidth(120);
        ImGui.SliderFloat("Zoom", ref _paletteZoom, 1f, 4f, "%.0fx");
        _paletteZoom = MathF.Round(_paletteZoom);
        ImGui.SameLine();
        ImGui.TextDisabled($"{_stamp.GetLength(0)}x{_stamp.GetLength(1)} selected");

        float tileW = tileset.TileWidth * _paletteZoom, tileH = tileset.TileHeight * _paletteZoom;
        if (ImGui.BeginChild("##palette", new NVec2(0, 0), ImGuiChildFlags.FrameStyle, ImGuiWindowFlags.HorizontalScrollbar))
        {
            var origin = ImGui.GetCursorScreenPos();
            ImGui.Image(_textureId, new NVec2(tileset.Texture.Width * _paletteZoom, tileset.Texture.Height * _paletteZoom));
            bool hovered = ImGui.IsItemHovered();
            var dl = ImGui.GetWindowDrawList();

            if (hovered && tileset.Columns > 0 && tileset.Rows > 0)
            {
                var mouse = ImGui.GetMousePos();
                var cell = new Point(Math.Clamp((int)((mouse.X - origin.X) / tileW), 0, tileset.Columns - 1),
                                     Math.Clamp((int)((mouse.Y - origin.Y) / tileH), 0, tileset.Rows - 1));
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    _selectingInPalette = true;
                    _paletteStart = cell;
                }
                if (_selectingInPalette) _paletteEnd = cell;

                dl.AddRect(origin + new NVec2(cell.X * tileW, cell.Y * tileH),
                           origin + new NVec2((cell.X + 1) * tileW, (cell.Y + 1) * tileH),
                           ImGui.GetColorU32(new NVec4(1f, 1f, 1f, 0.5f)));
                ImGui.SetTooltip($"tile {cell.Y * tileset.Columns + cell.X}");
            }

            if (_selectingInPalette && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                _selectingInPalette = false;
                _stamp = TileOps.StampFromSheet(tileset.Columns, _paletteStart.X, _paletteStart.Y, _paletteEnd.X, _paletteEnd.Y);
                if (_tool is Tool.Erase or Tool.Terrain or Tool.Pick) _tool = Tool.Brush;
            }

            int x0 = Math.Min(_paletteStart.X, _paletteEnd.X), x1 = Math.Max(_paletteStart.X, _paletteEnd.X);
            int y0 = Math.Min(_paletteStart.Y, _paletteEnd.Y), y1 = Math.Max(_paletteStart.Y, _paletteEnd.Y);
            dl.AddRect(origin + new NVec2(x0 * tileW, y0 * tileH), origin + new NVec2((x1 + 1) * tileW, (y1 + 1) * tileH),
                       ImGui.GetColorU32(EditorTheme.Accent), 0f, ImDrawFlags.None, 2f);
        }
        ImGui.EndChild();
    }

    public void HandleInput(Camera camera, EditorApp editor)
    {
        var scene = _state?.CurrentScene;
        var map = Target();
        var layer = CurrentLayer(map);
        if (!IsEditing || scene == null || map == null || layer == null)
        {
            FinishStroke(scene, layer);
            _hoverCell = null;
            return;
        }

        bool hovered = editor.IsSceneViewHovered();
        var mouse = ImGui.GetMousePos();
        var world = camera.ScreenToWorld(editor.ScreenToSceneLocal(new Vector2(mouse.X, mouse.Y)));
        var cell = new Point((int)MathF.Floor(world.X / map.TileSize), (int)MathF.Floor(world.Y / map.TileSize));
        _hoverCell = hovered ? cell : null;

        if (_stroke == null)
        {
            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) BeginStroke(layer, cell);
        }
        else if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            ContinueStroke(layer, cell);
        }
        else
        {
            FinishStroke(scene, layer);
        }
    }

    private void BeginStroke(TilemapLayer layer, Point cell)
    {
        if (_tool == Tool.Pick)
        {
            var tile = layer.GetTile(cell.X, cell.Y);
            if (!tile.IsEmpty && layer.Tileset is { Columns: > 0 } tileset)
            {
                _stamp = new[,] { { tile.TileId } };
                _paletteStart = _paletteEnd = new Point(tile.TileId % tileset.Columns, tile.TileId / tileset.Columns);
                _tool = Tool.Brush;
            }
            return;
        }

        _stroke = new TileStroke(_layerIndex, layer);
        _strokeStart = _lastCell = cell;
        if (_tool == Tool.Fill)
            TilemapEditing.Fill(layer, cell.X, cell.Y, new TileData { TileId = _stamp[0, 0] }, _stroke);
        else if (_tool != Tool.Rectangle)
            PaintAt(layer, cell);
    }

    private void ContinueStroke(TilemapLayer layer, Point cell)
    {
        if (cell == _lastCell || _stroke == null) return;
        if (_tool is Tool.Brush or Tool.Erase or Tool.Terrain)
            foreach (var (x, y) in TileOps.Line(_lastCell.X, _lastCell.Y, cell.X, cell.Y).Skip(1))
                PaintAt(layer, new Point(x, y));
        _lastCell = cell;
    }

    private void PaintAt(TilemapLayer layer, Point cell)
    {
        switch (_tool)
        {
            case Tool.Brush:
                TilemapEditing.PaintStamp(layer, cell.X, cell.Y, _stamp, _stroke!);
                break;
            case Tool.Erase:
                TilemapEditing.Erase(layer, cell.X, cell.Y, _stroke!);
                break;
            case Tool.Terrain when layer.Tileset?.Terrain is { } terrain:
                TilemapEditing.PaintTerrain(layer, terrain, cell.X, cell.Y, _terrain, _stroke!);
                break;
        }
    }

    private void FinishStroke(Scene? scene, TilemapLayer? layer)
    {
        if (_stroke == null) return;
        if (scene != null && layer != null && _state != null)
        {
            if (_tool == Tool.Rectangle)
                TilemapEditing.FillRectangle(layer, _strokeStart.X, _strokeStart.Y, _lastCell.X, _lastCell.Y, _stamp, _stroke);
            TilemapEditing.FinishStroke(_state, scene, _stroke, layer, StrokeDescription(_tool));
        }
        _stroke = null;
    }

    public void DrawOverlay(ImDrawListPtr dl, NVec2 viewportMin, Camera camera)
    {
        if (!IsEditing) return;
        var map = Target();
        var layer = CurrentLayer(map);
        if (map == null || layer == null) return;

        float ts = map.TileSize;
        NVec2 Screen(float wx, float wy)
        {
            var s = camera.WorldToScreen(new Vector2(wx, wy));
            return new NVec2(viewportMin.X + MathF.Round(s.X), viewportMin.Y + MathF.Round(s.Y));
        }

        dl.AddRect(Screen(layer.OriginX * ts, layer.OriginY * ts),
                   Screen((layer.OriginX + layer.Width) * ts, (layer.OriginY + layer.Height) * ts),
                   ImGui.GetColorU32(new NVec4(1f, 1f, 1f, 0.25f)));

        var accent = EditorTheme.Accent;
        uint accentColor = ImGui.GetColorU32(accent);

        if (_tool == Tool.Rectangle && _stroke != null)
        {
            int x0 = Math.Min(_strokeStart.X, _lastCell.X), x1 = Math.Max(_strokeStart.X, _lastCell.X);
            int y0 = Math.Min(_strokeStart.Y, _lastCell.Y), y1 = Math.Max(_strokeStart.Y, _lastCell.Y);
            dl.AddRectFilled(Screen(x0 * ts, y0 * ts), Screen((x1 + 1) * ts, (y1 + 1) * ts),
                             ImGui.GetColorU32(new NVec4(accent.X, accent.Y, accent.Z, 0.15f)));
            dl.AddRect(Screen(x0 * ts, y0 * ts), Screen((x1 + 1) * ts, (y1 + 1) * ts), accentColor, 0f, ImDrawFlags.None, 1.5f);
            return;
        }

        if (_hoverCell is not { } cell) return;

        int width = 1, height = 1;
        if (_tool == Tool.Brush)
        {
            width = _stamp.GetLength(0);
            height = _stamp.GetLength(1);
            if (layer.Tileset is { Columns: > 0 } tileset && _textureId != IntPtr.Zero && ReferenceEquals(_boundTexture, tileset.Texture))
            {
                float texW = tileset.Texture.Width, texH = tileset.Texture.Height;
                uint ghost = ImGui.GetColorU32(new NVec4(1f, 1f, 1f, 0.7f));
                for (int sx = 0; sx < width; sx++)
                    for (int sy = 0; sy < height; sy++)
                    {
                        int id = _stamp[sx, sy];
                        if (id == TileOps.Skip) continue;
                        int col = id % tileset.Columns, row = id / tileset.Columns;
                        var uv0 = new NVec2(col * tileset.TileWidth / texW, row * tileset.TileHeight / texH);
                        var uv1 = new NVec2((col + 1) * tileset.TileWidth / texW, (row + 1) * tileset.TileHeight / texH);
                        dl.AddImage(_textureId, Screen((cell.X + sx) * ts, (cell.Y + sy) * ts),
                                    Screen((cell.X + sx + 1) * ts, (cell.Y + sy + 1) * ts), uv0, uv1, ghost);
                    }
            }
        }

        uint outline = _tool == Tool.Erase || (_tool == Tool.Terrain && _terrain < 0)
            ? ImGui.GetColorU32(EditorTheme.Danger)
            : accentColor;
        dl.AddRect(Screen(cell.X * ts, cell.Y * ts), Screen((cell.X + width) * ts, (cell.Y + height) * ts),
                   outline, 0f, ImDrawFlags.None, 1.5f);
    }

    private TilemapRenderer? Target()
    {
        var scene = _state?.CurrentScene;
        if (scene == null) return _target = null;
        if (_target == null || !scene.Entities.Contains(_target.Entity)) _target = TilemapTarget.Find(scene);
        if (_target != null) _layerIndex = Math.Clamp(_layerIndex, 0, Math.Max(0, _target.Layers.Count - 1));
        return _target;
    }

    private TilemapLayer? CurrentLayer(TilemapRenderer? map)
        => map != null && _layerIndex >= 0 && _layerIndex < map.Layers.Count ? map.Layers[_layerIndex] : null;

    private void BindTexture(Texture2D texture)
    {
        if (_bindTexture == null || ReferenceEquals(_boundTexture, texture)) return;
        _textureId = _bindTexture(texture);
        _boundTexture = texture;
    }

    private static string ToolLabel(Tool tool) => tool switch
    {
        Tool.Brush => "Brush",
        Tool.Erase => "Erase",
        Tool.Fill => "Fill",
        Tool.Rectangle => "Rect",
        Tool.Terrain => "Terrain",
        _ => "Pick",
    };

    private static string ToolHint(Tool tool) => tool switch
    {
        Tool.Brush => "Paints the selection; painting past the edge grows the layer.",
        Tool.Erase => "Clears cells.",
        Tool.Fill => "Fills the connected area of the same tile.",
        Tool.Rectangle => "Drag a rectangle; the selection repeats inside it.",
        Tool.Terrain => "Paints terrain with its edges and corners; past the edge it grows the layer.",
        _ => "Click a painted cell to pick its tile.",
    };

    private static string StrokeDescription(Tool tool) => tool switch
    {
        Tool.Erase => "Erase tiles",
        Tool.Fill => "Fill tiles",
        Tool.Rectangle => "Fill tile rectangle",
        Tool.Terrain => "Paint terrain",
        _ => "Paint tiles",
    };
}
