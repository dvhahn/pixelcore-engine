using System;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Nav;

public static class Nav
{
    private static NavGrid? _grid;

    private static WeakReference<Scene>? _scene;

    public static NavGrid For(Scene scene)
    {
        if (_grid != null && _scene != null &&
            _scene.TryGetTarget(out var cached) && ReferenceEquals(cached, scene))
            return _grid;

        _grid = NavGrid.Bake(scene);
        _scene = new WeakReference<Scene>(scene);
#if DEBUG
        LastResult = null;
#endif
        return _grid;
    }

    public static void Invalidate()
    {
        _grid = null;
        _scene = null;
#if DEBUG
        LastResult = null;
#endif
    }

    public static NavPathResult FindPath(Scene scene, Vector2 from, Vector2 to)
    {
        var r = NavPathfinder.FindPath(For(scene), from, to);
#if DEBUG
        LastResult = r;
#endif
        return r;
    }

#if DEBUG
    public static NavPathResult? LastResult;
#endif
}
