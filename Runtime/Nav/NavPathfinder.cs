using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace PixelCore.Runtime.Nav;

public enum NavPathStatus
{
    Direct,

    Path,

    Unreachable,

    OffGrid,
}

public readonly struct NavPathResult
{
    public readonly NavPathStatus Status;
    public readonly List<Vector2> Points;

    public readonly bool StartSnapped;

    public readonly bool GoalSnapped;

    public NavPathResult(NavPathStatus status, List<Vector2> points,
        bool startSnapped = false, bool goalSnapped = false)
    {
        Status = status;
        Points = points;
        StartSnapped = startSnapped;
        GoalSnapped = goalSnapped;
    }

    public bool Ok => Status is NavPathStatus.Direct or NavPathStatus.Path;

    internal static NavPathResult Fail(NavPathStatus status) => new(status, new List<Vector2>());
}

public static class NavPathfinder
{
    public const int SnapRadius = 3;

    public static NavPathResult FindPath(NavGrid grid, Vector2 from, Vector2 to)
    {
        if (grid.IsEmpty) return NavPathResult.Fail(NavPathStatus.OffGrid);

        var startCell = grid.WorldToCell(from);
        var goalCell = grid.WorldToCell(to);

        if (!grid.InBounds(startCell) || !grid.InBounds(goalCell))
            return NavPathResult.Fail(NavPathStatus.OffGrid);

        bool startSnapped = grid.IsBlocked(startCell);
        bool goalSnapped = grid.IsBlocked(goalCell);
        if (startSnapped && !grid.TrySnap(startCell, SnapRadius, out startCell))
            return NavPathResult.Fail(NavPathStatus.OffGrid);
        if (goalSnapped && !grid.TrySnap(goalCell, SnapRadius, out goalCell))
            return NavPathResult.Fail(NavPathStatus.OffGrid);

        var startWorld = startSnapped ? grid.CellCenter(startCell) : from;
        var goalWorld = goalSnapped ? grid.CellCenter(goalCell) : to;

        if (!grid.Reachable(startCell, goalCell))
            return NavPathResult.Fail(NavPathStatus.Unreachable);

        if (grid.HasLineOfSight(startCell, goalCell))
            return new NavPathResult(NavPathStatus.Direct,
                new List<Vector2> { startWorld, goalWorld }, startSnapped, goalSnapped);

        var cells = Astar(grid, startCell, goalCell);
        if (cells == null)
            return NavPathResult.Fail(NavPathStatus.Unreachable);

        var pulled = StringPull(grid, cells);
        var points = new List<Vector2>(pulled.Count);
        for (int i = 0; i < pulled.Count; i++) points.Add(grid.CellCenter(pulled[i]));

        points[0] = startWorld;
        points[^1] = goalWorld;

        return new NavPathResult(NavPathStatus.Path, points, startSnapped, goalSnapped);
    }

    private static readonly Point[] Neighbours =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    internal static List<Point>? Astar(NavGrid grid, Point start, Point goal)
    {
        if (start == goal) return new List<Point> { start };

#if DEBUG
        DebugExpandedCount = 0;
#endif
        var open = new PriorityQueue<Point, double>();
        var cost = new Dictionary<Point, double> { [start] = 0 };
        var cameFrom = new Dictionary<Point, Point>();

        open.Enqueue(start, Heuristic(start, goal));

        while (open.TryDequeue(out var current, out _))
        {
#if DEBUG
            DebugExpandedCount++;
#endif
            if (current == goal) break;

            double baseCost = cost[current];
            for (int i = 0; i < Neighbours.Length; i++)
            {
                var n = new Point(current.X + Neighbours[i].X, current.Y + Neighbours[i].Y);
                if (grid.IsBlocked(n)) continue;

                bool diagonal = Neighbours[i].X != 0 && Neighbours[i].Y != 0;
                if (diagonal)
                {
                    if (grid.IsBlocked(n.X, current.Y) || grid.IsBlocked(current.X, n.Y)) continue;
                }

                double next = baseCost + 1.0 + (diagonal ? 0.414 : 0.0);
                if (cost.TryGetValue(n, out double known) && next >= known) continue;

                cost[n] = next;
                cameFrom[n] = current;
                open.Enqueue(n, next + Heuristic(n, goal));
            }
        }

        if (!cameFrom.ContainsKey(goal)) return null;

        var path = new List<Point>();
        for (var at = goal; at != start; at = cameFrom[at]) path.Add(at);
        path.Add(start);
        path.Reverse();
        return path;
    }

#if DEBUG
    internal static int DebugExpandedCount;
#endif

    private static double Heuristic(Point a, Point b)
    {
        const double D = 1.0, D2 = 1.414;
        int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
        return D * (dx + dy) + (D2 - 2 * D) * Math.Min(dx, dy);
    }

    internal static List<Point> StringPull(NavGrid grid, List<Point> cells)
    {
        var result = new List<Point> { cells[0] };
        int i = 0;
        while (i < cells.Count - 1)
        {
            int j = cells.Count - 1;
            while (j > i + 1 && !grid.HasLineOfSight(cells[i], cells[j])) j--;
            result.Add(cells[j]);
            i = j;
        }
        return result;
    }
}
