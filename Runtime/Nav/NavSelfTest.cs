using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Runtime.Nav;

public static class NavSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        _pass = 0; _fail = 0;
        Console.WriteLine("=== Pathfinding self-test ===");

        TestCorridorDirect();
        TestUTrap();
        TestStrictCorner();
        TestUnreachableRegions();
        TestLosImpliesStraightAstar();
        TestStringPullBound();
        TestSnapIntoCollider();
        TestBakeDeterminism();
        TestMovingBodiesAreNotWalls();
        TestFootprintClearance();
        TestTilemapOnlyScene();
        TestPrefilterSkipsAstar();
        TestLastResultDropsOnRebake();
        TestRealScene();

        Console.WriteLine($"=== Nav: {_pass} passed, {_fail} failed ===");
    }

    private static void TestCorridorDirect()
    {
        var s = Room(80, 24);
        Box(s, 0, 0, 80, 8);
        Box(s, 0, 16, 80, 8);
        var grid = NavGrid.Bake(s);

        Check("1. premise: the bake actually saw the walls (blocked > 0)", grid.BlockedCount > 0,
            $"blocked={grid.BlockedCount}");

        var r = NavPathfinder.FindPath(grid, new Vector2(4, 12), new Vector2(76, 12));
        Check("1. a corridor is a straight line; A* never runs", r.Status == NavPathStatus.Direct, r.Status.ToString());
        Check("1. there are two points, start and goal", r.Points.Count == 2, $"{r.Points.Count} points");
        Check("1. endpoints keep the requested coordinates and do not snap to cell centres",
            r.Points.Count == 2 && r.Points[0] == new Vector2(4, 12) && r.Points[1] == new Vector2(76, 12));
        Check("1. no snapping occurred", !r.StartSnapped && !r.GoalSnapped);
    }

    private static Scene UTrapScene()
    {
        var s = Room(128, 96);
        Border(s, 128, 96, 8);
        Box(s, 56, 24, 8, 48);
        Box(s, 56, 24, 48, 8);
        Box(s, 56, 64, 48, 8);
        return s;
    }

    private static void TestUTrap()
    {
        var grid = NavGrid.Bake(UTrapScene());
        var from = new Vector2(24, 48);
        var to = new Vector2(84, 48);

        var a = grid.WorldToCell(from);
        var b = grid.WorldToCell(to);

        Check("2. premise: sight really is blocked (otherwise everything below is vacuous)",
            !grid.HasLineOfSight(a, b));
        Check("2. premise: it is still reachable on foot", grid.Reachable(a, b));

        var r = NavPathfinder.FindPath(grid, from, to);
        Check("2. A* produces a detour", r.Status == NavPathStatus.Path, r.Status.ToString());
        Check("2. three or more points, so not a straight line", r.Points.Count >= 3, $"{r.Points.Count} points");
        Check("2. the path passes through no blocked cell", PathClear(grid, r.Points));
        Check("2. the path goes around via the mouth of the U (x>96)",
            r.Points.Any(p => p.X > 96f), Dump(r.Points));
    }

    private static void TestStrictCorner()
    {
        var s = Room(32, 32);
        Box(s, -8, -8, 48, 8);
        Box(s, -8, -8, 8, 48);
        Box(s, 16, -8, 24, 48);
        Box(s, -8, 16, 48, 24);
        Box(s, 8, 0, 8, 8);
        Box(s, 0, 8, 8, 8);
        var grid = NavGrid.Bake(s);

        var a = grid.WorldToCell(new Vector2(4, 4));
        var b = grid.WorldToCell(new Vector2(12, 12));

        Check("3. premise: both cells are open", !grid.IsBlocked(a) && !grid.IsBlocked(b),
            $"{a} {b} / {CountOpen(grid)} open");
        Check("3. premise: they touch only diagonally",
            Math.Abs(a.X - b.X) == 1 && Math.Abs(a.Y - b.Y) == 1
            && grid.IsBlocked(new Point(a.X, b.Y)) && grid.IsBlocked(new Point(b.X, a.Y)),
            $"{a} {b}");
        Check("3. premise: each is fully isolated, with no detour",
            RegionSize(grid, a) == 1 && RegionSize(grid, b) == 1,
            $"region sizes {RegionSize(grid, a)} and {RegionSize(grid, b)}");

        Check("* 3. no diagonal pinch: the regions are separate",
            !grid.Reachable(a, b), $"region {grid.RegionOf(a)} vs {grid.RegionOf(b)}");
        Check("* 3. no diagonal pinch: A* cannot cross either",
            NavPathfinder.Astar(grid, a, b) == null);
        Check("* 3. no diagonal pinch: sight is blocked too (it must not be more permissive than A*)",
            !grid.HasLineOfSight(a, b));

        var r = NavPathfinder.FindPath(grid, grid.CellCenter(a), grid.CellCenter(b));
        Check("3. the query result is Unreachable", r.Status == NavPathStatus.Unreachable, r.Status.ToString());
    }

    private static void TestUnreachableRegions()
    {
        var s = Room(128, 64);
        Border(s, 128, 64, 8);
        Box(s, 60, 0, 8, 64);
        var grid = NavGrid.Bake(s);

        var left = grid.WorldToCell(new Vector2(32, 32));
        var right = grid.WorldToCell(new Vector2(96, 32));

        Check("4. premise: both sides are walkable", !grid.IsBlocked(left) && !grid.IsBlocked(right));
        Check("4. the two halves are different regions",
            grid.RegionOf(left) != grid.RegionOf(right),
            $"{grid.RegionOf(left)} vs {grid.RegionOf(right)}");
        Check("4. Reachable is false", !grid.Reachable(left, right));

        var r = NavPathfinder.FindPath(grid, new Vector2(32, 32), new Vector2(96, 32));
        Check("4. Unreachable, with no points",
            r.Status == NavPathStatus.Unreachable && r.Points.Count == 0);

        var r2 = NavPathfinder.FindPath(grid, new Vector2(24, 24), new Vector2(48, 40));
        Check("* 4. control: within one half it is reachable", r2.Ok, r2.Status.ToString());

        var off = NavPathfinder.FindPath(grid, new Vector2(32, 32), new Vector2(9999, 9999));
        Check("4. a goal off the grid returns OffGrid", off.Status == NavPathStatus.OffGrid, off.Status.ToString());
    }

    private static void TestLosImpliesStraightAstar()
    {
        var s = Room(96, 96);
        Border(s, 96, 96, 8);
        Box(s, 40, 40, 16, 16);
        var grid = NavGrid.Bake(s);

        int losPairs = 0, blockedPairs = 0, mismatch = 0;
        var open = new List<Point>();
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
                if (!grid.IsBlocked(x, y)) open.Add(new Point(x, y));

        foreach (var a in open)
        {
            foreach (var b in open)
            {
                if (a == b) continue;
                if (!grid.HasLineOfSight(a, b)) { blockedPairs++; continue; }
                losPairs++;

                var cells = NavPathfinder.Astar(grid, a, b);
                if (cells == null) { mismatch++; continue; }
                if (NavPathfinder.StringPull(grid, cells).Count != 2) mismatch++;
            }
        }

        Check("5. premise: some pairs have clear sight", losPairs > 0, $"{losPairs} pairs");
        Check("5. premise: some pairs are blocked, so the pillar really occludes", blockedPairs > 0,
            $"{blockedPairs} pairs");
        Check($"* 5. clear sight implies a straight A* path (all {losPairs} pairs)", mismatch == 0,
            $"{mismatch} mismatched pairs");
    }

    private static void TestStringPullBound()
    {
        var grid = NavGrid.Bake(UTrapScene());
        var a = grid.WorldToCell(new Vector2(24, 48));
        var b = grid.WorldToCell(new Vector2(84, 48));

        var raw = NavPathfinder.Astar(grid, a, b);
        Check("6. premise: A* produced a path", raw != null);
        if (raw == null) return;

        var pulled = NavPathfinder.StringPull(grid, raw);
        Check("6. premise: the raw path is long (a short one leaves pulling nothing to do)",
            raw.Count >= 12, $"{raw.Count} cells");
        Check("* 6. pulling cuts the point count sharply (to a third or less)",
            pulled.Count * 3 <= raw.Count, $"{raw.Count} cells -> {pulled.Count} points");
        Check("6. the endpoints survive pulling unchanged",
            pulled[0] == raw[0] && pulled[^1] == raw[^1]);
        Check("6. every pulled segment is connected by clear sight",
            AllSegmentsVisible(grid, pulled));
    }

    private static void TestSnapIntoCollider()
    {
        var s = Room(96, 96);
        Border(s, 96, 96, 8);
        Box(s, 40, 40, 16, 16);
        var grid = NavGrid.Bake(s);

        var inside = new Vector2(48, 48);
        Check("7. premise: the goal cell really is blocked", grid.IsBlocked(grid.WorldToCell(inside)));

        var r = NavPathfinder.FindPath(grid, new Vector2(20, 20), inside);
        Check("* 7. a goal inside furniture still produces a path", r.Ok, r.Status.ToString());
        Check("7. the snap is reported", r.GoalSnapped && !r.StartSnapped);
        Check("7. the destination lands somewhere walkable",
            r.Points.Count > 0 && !grid.IsBlocked(grid.WorldToCell(r.Points[^1])));

        var r2 = NavPathfinder.FindPath(grid, inside, new Vector2(20, 20));
        Check("7. a blocked start still produces a path", r2.Ok, r2.Status.ToString());
        Check("7. the start snap is reported", r2.StartSnapped && !r2.GoalSnapped);

        var thick = Room(160, 160);
        Border(thick, 160, 160, 8);
        Box(thick, 48, 48, 64, 64);
        var tg = NavGrid.Bake(thick);
        var deep = new Vector2(80, 80);
        Check("7. premise: the middle of the mass is blocked", tg.IsBlocked(tg.WorldToCell(deep)));
        var far = NavPathfinder.FindPath(tg, new Vector2(24, 24), deep);
        Check("* 7. control: no free cell within the radius gives OffGrid; snapping does not rescue anything",
            far.Status == NavPathStatus.OffGrid, far.Status.ToString());
    }

    private static void TestBakeDeterminism()
    {
        var s = UTrapScene();
        var g1 = NavGrid.Bake(s);
        var g2 = NavGrid.Bake(s);

        Check("8. premise: the fingerprint is not empty", g1.Fingerprint().Length > 16);
        Check("* 8. baking the same scene twice gives identical bytes",
            g1.Fingerprint().SequenceEqual(g2.Fingerprint()));

        var s3 = UTrapScene();
        Box(s3, 16, 16, 8, 8);
        Check("* 8. control: different walls give a different fingerprint",
            !g1.Fingerprint().SequenceEqual(NavGrid.Bake(s3).Fingerprint()));

        var p1 = NavPathfinder.FindPath(g1, new Vector2(24, 48), new Vector2(84, 48));
        var p2 = NavPathfinder.FindPath(g2, new Vector2(24, 48), new Vector2(84, 48));
        Check("8. the same query yields the same path", p1.Points.SequenceEqual(p2.Points));
    }

    private static void TestMovingBodiesAreNotWalls()
    {
        var s = Room(64, 64);
        Border(s, 64, 64, 8);

        var actor = s.CreateEntity("Actor");
        actor.GetComponent<Transform>()!.Position = new Vector2(32, 32);
        actor.AddComponent<Rigidbody2D>();
        actor.AddComponent<BoxCollider2D>().Size = new Vector2(16, 16);

        var trig = s.CreateEntity("Trigger");
        trig.GetComponent<Transform>()!.Position = new Vector2(16, 32);
        var tc = trig.AddComponent<BoxCollider2D>();
        tc.Size = new Vector2(16, 16);
        tc.IsTrigger = true;

        s.FlushPendingAdds();
        var grid = NavGrid.Bake(s);

        Check("9. premise: the outer walls are still blocked, so baking is not simply dead", grid.BlockedCount > 0);
        Check("* 9. a body with a rigidbody is not a wall",
            !grid.IsBlocked(grid.WorldToCell(new Vector2(32, 32))));
        Check("* 9. a trigger is not a wall",
            !grid.IsBlocked(grid.WorldToCell(new Vector2(16, 32))));
    }

    private static void TestFootprintClearance()
    {
        var s = Room(64, 64);
        Box(s, 22, 0, 42, 64);
        var grid = NavGrid.Bake(s);

        var near = grid.WorldToCell(new Vector2(20, 32));
        var far = grid.WorldToCell(new Vector2(12, 32));

        Check("11. premise: the wall-adjacent cell centre is outside the wall (a point probe would leave it open)",
            grid.CellCenter(near).X < 22f, $"centre x={grid.CellCenter(near).X}");
        Check("* 11. the footprint bites the wall and blocks that cell, so clearance is created at bake time",
            grid.IsBlocked(near), $"cell={near}");
        Check("* 11. control: one cell further out stays open, so the probe is not overreaching",
            !grid.IsBlocked(far), $"cell={far}");
    }

    private static void TestTilemapOnlyScene()
    {
        var s = new Scene("Nav-Tilemap");
        var e = s.CreateEntity("Tilemap");
        var tm = e.AddComponent<TilemapRenderer>();
        tm.TileSize = 8;
        var layer = tm.AddLayer("Walls", 12, 12);
        for (int i = 0; i < 12; i++)
        {
            layer.SetTile(i, 0, 1);
            layer.SetTile(i, 11, 1);
            layer.SetTile(0, i, 1);
            layer.SetTile(11, i, 1);
        }
        s.FlushPendingAdds();

        Check("12. premise: no colliders and no camera bounds",
            !s.CameraBoundsEnabled && !s.Entities.Any(x => x.GetComponent<Collider2D>() != null));

        var grid = NavGrid.Bake(s);
        Check("* 12. a tile-only scene still bakes a grid, not a silently empty one",
            !grid.IsEmpty, $"{grid.Width}×{grid.Height}");
        Check("* 12. tile walls bake as blocked", grid.BlockedCount > 0,
            $"blocked={grid.BlockedCount}");
        Check("12. the interior is walkable",
            !grid.IsBlocked(grid.WorldToCell(new Vector2(48, 48))));
        Check("12. a path is found inside the room",
            NavPathfinder.FindPath(grid, new Vector2(20, 20), new Vector2(72, 72)).Ok);
    }

    private static void TestPrefilterSkipsAstar()
    {
#if DEBUG
        var s = Room(128, 64);
        Border(s, 128, 64, 8);
        Box(s, 60, 0, 8, 64);
        var grid = NavGrid.Bake(s);

        NavPathfinder.DebugExpandedCount = 0;
        var ok = NavPathfinder.FindPath(grid, new Vector2(16, 16), new Vector2(48, 48));
        Check("13. premise: a query within one half succeeds", ok.Ok, ok.Status.ToString());

        var trap = NavGrid.Bake(UTrapScene());
        NavPathfinder.DebugExpandedCount = 0;
        NavPathfinder.FindPath(trap, new Vector2(24, 48), new Vector2(84, 48));
        Check("13. premise: the counter moves on a query that runs A*",
            NavPathfinder.DebugExpandedCount > 0, $"{NavPathfinder.DebugExpandedCount} nodes");

        NavPathfinder.DebugExpandedCount = 0;
        var un = NavPathfinder.FindPath(grid, new Vector2(32, 32), new Vector2(96, 32));
        Check("13. premise: that query really is Unreachable",
            un.Status == NavPathStatus.Unreachable, un.Status.ToString());
        Check("* 13. early rejection never calls A*, so the whole map is not expanded",
            NavPathfinder.DebugExpandedCount == 0, $"{NavPathfinder.DebugExpandedCount} nodes");
#endif
    }

    private static void TestLastResultDropsOnRebake()
    {
#if DEBUG
        Nav.Invalidate();

        var trap = UTrapScene();
        Nav.FindPath(trap, new Vector2(24, 48), new Vector2(84, 48));
        Check("14. premise: a query records a last path",
            Nav.LastResult is { } lr && lr.Points.Count > 0);

        var other = Room(64, 64);
        Border(other, 64, 64, 8);
        Nav.For(other);
        Check("* 14. changing scene drops the last path, so the old one is not drawn on the new room",
            Nav.LastResult == null);

        Nav.FindPath(other, new Vector2(20, 20), new Vector2(44, 44));
        Check("14. premise: it is recorded again in the new scene", Nav.LastResult != null);

        Nav.Invalidate();
        Check("* 14. Invalidate drops the last path too", Nav.LastResult == null);

        Nav.Invalidate();
#endif
    }

    private static void TestRealScene()
    {
        string path = Path.Combine(Assets.ContentPaths.Root, "Scenes", "Village.scene");
        Check("10. premise: Village.scene exists", File.Exists(path), path);
        if (!File.Exists(path)) return;

        var data = SceneSerializer.LoadFromFile(path);
        Check("10. premise: the scene loads", data != null);
        if (data == null) return;

        var scene = new Scene("Village");
        SceneSerializer.FromData(scene, data);
        scene.FlushPendingAdds();

        var grid = NavGrid.Bake(scene);
        Check("10. the real scene does not bake an empty grid", !grid.IsEmpty, $"{grid.Width}×{grid.Height}");
        Check("10. the real scene has blocked cells, so colliders were actually seen", grid.BlockedCount > 0,
            $"blocked={grid.BlockedCount}");

        var player = scene.FindPlayer();
        Check("10. premise: a player exists", player != null);
        if (player == null) return;

        var spawn = player.GetComponent<Transform>()!.Position;
        var cell = grid.WorldToCell(spawn);
        Check("* 10. the player spawn cell is walkable",
            grid.InBounds(cell) && !grid.IsBlocked(cell),
            $"spawn={spawn} cell={cell} inBounds={grid.InBounds(cell)}");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var far = grid.CellCenter(new Point(grid.Width - 2, grid.Height - 2));
        var r = NavPathfinder.FindPath(grid, spawn, far);
        sw.Stop();
        Console.WriteLine($"  [measured] Village.scene {grid.Width}x{grid.Height} cells " +
            $"(blocked {grid.BlockedCount}, regions {grid.RegionCount}) - " +
            $"bake {grid.BakeMilliseconds:0.00}ms, query {sw.Elapsed.TotalMilliseconds:0.00}ms -> {r.Status}");
    }

    private static Scene Room(int w, int h)
    {
        var s = new Scene("Nav");
        s.CameraBoundsEnabled = true;
        s.CameraBounds = new Rectangle(0, 0, w, h);
        return s;
    }

    private static void Box(Scene s, float x, float y, float w, float h)
    {
        var e = s.CreateEntity("Wall");
        e.GetComponent<Transform>()!.Position = new Vector2(x + w / 2f, y + h / 2f);
        e.AddComponent<BoxCollider2D>().Size = new Vector2(w, h);
        s.FlushPendingAdds();
    }

    private static void Border(Scene s, float w, float h, float t)
    {
        Box(s, 0, 0, w, t);
        Box(s, 0, h - t, w, t);
        Box(s, 0, 0, t, h);
        Box(s, w - t, 0, t, h);
    }

    private static int RegionSize(NavGrid g, Point c)
    {
        int id = g.RegionOf(c);
        if (id == 0) return 0;
        int n = 0;
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width; x++)
                if (g.RegionOf(new Point(x, y)) == id) n++;
        return n;
    }

    private static int CountOpen(NavGrid g)
    {
        int n = 0;
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width; x++)
                if (!g.IsBlocked(x, y)) n++;
        return n;
    }

    private static bool PathClear(NavGrid g, List<Vector2> pts)
    {
        foreach (var p in pts)
            if (g.IsBlocked(g.WorldToCell(p))) return false;
        return true;
    }

    private static bool AllSegmentsVisible(NavGrid g, List<Point> pts)
    {
        for (int i = 0; i + 1 < pts.Count; i++)
            if (!g.HasLineOfSight(pts[i], pts[i + 1])) return false;
        return true;
    }

    private static string Dump(List<Vector2> pts) =>
        string.Join(" → ", pts.Select(p => $"({p.X:0},{p.Y:0})"));

    private static void Check(string name, bool ok, string? detail = null)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name +
            (ok || detail == null ? "" : $"   [{detail}]"));
        if (ok) _pass++; else _fail++;
    }
}
