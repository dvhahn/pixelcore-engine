using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace PixelCore.Runtime.Rendering;

public struct FxLayer
{
    public float ChromAb;

    public float LensDistortion;

    public Color FogColor;

    public float FogOpacity;

    public static FxLayer Neutral => new() { FogColor = new Color(185, 195, 215) };

    public readonly bool IsNeutral => ChromAb <= 0f && LensDistortion == 0f && FogOpacity <= 0f;
}

public static class FxStack
{
    private static FxLayer? _weather;

    private static readonly List<(int Id, FxLayer Layer)> _pushed = new();

    private static int _nextId = 1;

    public static FxLayer Current =>
        _pushed.Count > 0 ? _pushed[^1].Layer
        : _weather ?? FxLayer.Neutral;

    public static void SetWeather(FxLayer? layer) => _weather = layer;

    public static int Push(FxLayer layer)
    {
        int id = _nextId++;
        _pushed.Add((id, layer));
        return id;
    }

    public static void Pop(int id)
    {
        for (int i = _pushed.Count - 1; i >= 0; i--)
        {
            if (_pushed[i].Id == id) { _pushed.RemoveAt(i); return; }
        }
    }

    public static int PushedCount => _pushed.Count;

    public static bool HasWeather => _weather.HasValue;

    public static void ResetAll()
    {
        _pushed.Clear();
        _weather = null;
        _nextId = 1;
    }
}
