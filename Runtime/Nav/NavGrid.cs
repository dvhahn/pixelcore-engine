using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Physics;

namespace PixelCore.Runtime.Nav;

public sealed class NavGrid
{
    public const int CellSize = 8;

    public static readonly Vector2 Footprint = new(7f, 6f);

    public Vector2 Origin { get; }

    public int Width { get; }
    public int Height { get; }

    private readonly bool[] _blocked;

    private readonly int[] _region;

    public int RegionCount { get; private set; }

    public int BlockedCount { get; private set; }

    public double BakeMilliseconds { get; internal set; }

    private NavGrid(Vector2 origin, int width, int height)
    {
        Origin = origin;
        Width = width;
        Height = height;
        _blocked = new bool[Math.Max(0, width * height)];
        _region = new int[Math.Max(0, width * height)];
    }

    public bool IsEmpty => Width <= 0 || Height <= 0;

    private int Index(int x, int y) => y * Width + x;

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    public bool InBounds(Point c) => InBounds(c.X, c.Y);

    public Point WorldToCell(Vector2 world) => new(
        (int)MathF.Floor((world.X - Origin.X) / CellSize),
        (int)MathF.Floor((world.Y - Origin.Y) / CellSize));

    public Vector2 CellCenter(Point c) => new(
        Origin.X + (c.X + 0.5f) * CellSize,
        Origin.Y + (c.Y + 0.5f) * CellSize);

    public bool IsBlocked(int x, int y) => !InBounds(x, y) || _blocked[Index(x, y)];
    public bool IsBlocked(Point c) => IsBlocked(c.X, c.Y);

    public int RegionOf(Point c) => InBounds(c) ? _region[Index(c.X, c.Y)] : 0;

    public bool Reachable(Point a, Point b)
    {
        int ra = RegionOf(a);
        return ra != 0 && ra == RegionOf(b);
    }

    public static NavGrid Bake(Scene scene)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var solids = CollectStaticSolids(scene);
        var tilemap = FindTilemap(scene);

        if (!TryComputeExtent(scene, solids, tilemap, out var min, out var max))
        {
            var blank = new NavGrid(Vector2.Zero, 0, 0);
            blank.BakeMilliseconds = sw.Elapsed.TotalMilliseconds;
            return blank;
        }

        var origin = new Vector2(
            MathF.Floor(min.X / CellSize) * CellSize,
            MathF.Floor(min.Y / CellSize) * CellSize);

        int w = (int)MathF.Ceiling((max.X - origin.X) / CellSize);
        int h = (int)MathF.Ceiling((max.Y - origin.Y) / CellSize);
        w = Math.Max(1, w); h = Math.Max(1, h);

        var grid = new NavGrid(origin, w, h);

        var half = Footprint * 0.5f;
        int blockedCount = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var c = grid.CellCenter(new Point(x, y));
                var probe = new AABB(c.X - half.X, c.Y - half.Y, Footprint.X, Footprint.Y);
                if (ProbeBlocked(probe, solids, tilemap))
                {
                    grid._blocked[grid.Index(x, y)] = true;
                    blockedCount++;
                }
            }
        }

        grid.BlockedCount = blockedCount;
        grid.LabelRegions();
        grid.BakeMilliseconds = sw.Elapsed.TotalMilliseconds;
        return grid;
    }

    private static bool ProbeBlocked(AABB probe, List<Collider2D> solids, TilemapRenderer? tilemap)
    {
        if (tilemap != null &&
            (tilemap.ResolveAxis(probe, 0) != 0f || tilemap.ResolveAxis(probe, 1) != 0f))
            return true;

        for (int i = 0; i < solids.Count; i++)
        {
            var other = solids[i];
            if (other is BoxCollider2D)
            {
                if (probe.Overlaps(other.GetBounds(), out _, out _)) return true;
            }
            else if (ShapeTests.OverlapsAabb(probe, other)) return true;
        }
        return false;
    }

    private static List<Collider2D> CollectStaticSolids(Scene scene)
    {
        var list = new List<Collider2D>();
        foreach (var e in scene.Entities)
        {
            if (!e.ActiveInHierarchy) continue;
            if (e.GetComponent<Rigidbody2D>() != null) continue;
            foreach (var col in e.GetComponents<Collider2D>())
            {
                if (!col.Enabled || col.IsTrigger) continue;
                list.Add(col);
            }
        }
        return list;
    }

    private static TilemapRenderer? FindTilemap(Scene scene)
    {
        foreach (var e in scene.Entities)
        {
            if (!e.ActiveInHierarchy) continue;
            var t = e.GetComponent<TilemapRenderer>();
            if (t != null && t.Enabled) return t;
        }
        return null;
    }

    private static bool TryComputeExtent(Scene scene, List<Collider2D> solids,
        TilemapRenderer? tilemap, out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.MaxValue);
        max = new Vector2(float.MinValue);
        bool any = false;

        foreach (var col in solids)
        {
            var b = col.GetBounds();
            min = Vector2.Min(min, b.Min);
            max = Vector2.Max(max, b.Max);
            any = true;
        }

        if (tilemap != null)
        {
            foreach (var layer in tilemap.Layers)
            {
                if (layer.Width <= 0 || layer.Height <= 0) continue;
                min = Vector2.Min(min, new Vector2(layer.OriginX * tilemap.TileSize, layer.OriginY * tilemap.TileSize));
                max = Vector2.Max(max, new Vector2(
                    (layer.OriginX + layer.Width) * tilemap.TileSize, (layer.OriginY + layer.Height) * tilemap.TileSize));
                any = true;
            }
        }

        if (scene.CameraBoundsEnabled)
        {
            var r = scene.CameraBounds;
            min = Vector2.Min(min, new Vector2(r.X, r.Y));
            max = Vector2.Max(max, new Vector2(r.X + r.Width, r.Y + r.Height));
            any = true;
        }

        if (!any) return false;

        min -= new Vector2(CellSize);
        max += new Vector2(CellSize);
        return true;
    }

    private void LabelRegions()
    {
        if (IsEmpty) return;

        int next = 0;
        var queue = new Queue<int>();
        Span<int> dx = stackalloc int[] { 1, -1, 0, 0 };
        Span<int> dy = stackalloc int[] { 0, 0, 1, -1 };

        for (int start = 0; start < _region.Length; start++)
        {
            if (_blocked[start] || _region[start] != 0) continue;

            next++;
            _region[start] = next;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                int cx = cur % Width, cy = cur / Width;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + dx[k], ny = cy + dy[k];
                    if (!InBounds(nx, ny)) continue;
                    int ni = Index(nx, ny);
                    if (_blocked[ni] || _region[ni] != 0) continue;
                    _region[ni] = next;
                    queue.Enqueue(ni);
                }
            }
        }

        RegionCount = next;
    }

    public bool HasLineOfSight(Point a, Point b)
    {
        if (IsBlocked(a) || IsBlocked(b)) return false;

        int x = a.X, y = a.Y;
        int dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
        int sx = b.X > a.X ? 1 : -1, sy = b.Y > a.Y ? 1 : -1;

        int error = dx - dy;
        dx *= 2; dy *= 2;

        for (int n = 1 + (dx + dy) / 2; n > 0; n--)
        {
            if (IsBlocked(x, y)) return false;
            if (x == b.X && y == b.Y) return true;

            if (error > 0) { x += sx; error -= dy; }
            else if (error < 0) { y += sy; error += dx; }
            else
            {
                if (IsBlocked(x + sx, y) || IsBlocked(x, y + sy)) return false;
                x += sx; y += sy;
                error -= dy; error += dx;
            }
        }
        return true;
    }

    public bool TrySnap(Point c, int radius, out Point snapped)
    {
        snapped = c;
        if (IsEmpty) return false;
        if (!IsBlocked(c)) return true;

        float best = float.MaxValue;
        bool found = false;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int nx = c.X + dx, ny = c.Y + dy;
                if (IsBlocked(nx, ny)) continue;
                float d = dx * dx + dy * dy;
                if (d >= best) continue;
                best = d;
                snapped = new Point(nx, ny);
                found = true;
            }
        }
        return found;
    }

    public byte[] Fingerprint()
    {
        var buf = new byte[16 + _blocked.Length + _region.Length * 4];
        int p = 0;
        void W(int v) { BitConverter.TryWriteBytes(buf.AsSpan(p), v); p += 4; }
        W(Width); W(Height);
        W(BitConverter.SingleToInt32Bits(Origin.X));
        W(BitConverter.SingleToInt32Bits(Origin.Y));
        for (int i = 0; i < _blocked.Length; i++) buf[p++] = _blocked[i] ? (byte)1 : (byte)0;
        for (int i = 0; i < _region.Length; i++) W(_region[i]);
        return buf;
    }
}
