using System;
using System.Collections.Generic;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Tilemap;
using PixelCore.Runtime.Physics;

namespace PixelCore.Runtime.Components;

[ComponentCategory(ComponentCategories.Render)]
public class TilemapRenderer : Component, IRenderableSource
{
    private int _tileSize = 32;
    public int TileSize { get => _tileSize; set => _tileSize = Math.Max(1, value); }
    public List<TilemapLayer> Layers { get; } = new();
    public int LayerCount => Layers.Count;

    private readonly List<IRenderable> _drawOrder = new();

    public IReadOnlyList<IRenderable> Renderables => BuildDrawOrder();

    public TilemapLayer AddLayer(string name, int width, int height)
    {
        var layer = new TilemapLayer(name, width, height)
        {
            DrawOrder = Layers.Count,
            Owner = this,
        };
        Layers.Add(layer);
        return layer;
    }

    public TilemapLayer? GetLayer(string name)
    {
        return Layers.Find(l => l.Name == name);
    }

    public float ResolveAxis(AABB bounds, int axis)
    {
        float push = 0f;
        foreach (var layer in Layers)
        {
            if (!layer.HasCollision) continue;
            float p = layer.ResolveAxis(bounds, TileSize, axis);
            if (MathF.Abs(p) > MathF.Abs(push)) push = p;
        }
        return push;
    }

    private List<IRenderable> BuildDrawOrder()
    {
        _drawOrder.Clear();
        for (int i = 0; i < Layers.Count; i++)
        {
            var layer = Layers[i];
            layer.Owner = this;

            int at = _drawOrder.Count;
            while (at > 0 && ((TilemapLayer)_drawOrder[at - 1]).DrawOrder > layer.DrawOrder) at--;
            _drawOrder.Insert(at, layer);
        }
        return _drawOrder;
    }
}
