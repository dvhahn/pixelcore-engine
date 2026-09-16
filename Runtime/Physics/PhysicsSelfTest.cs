using System;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Systems;

namespace PixelCore.Runtime.Physics;

public static class PhysicsSelfTest
{
    private static int _pass, _fail;

    private sealed class TickCounter : Component
    {
        public int Fixed, Frame;
        public float LastFixedDt;
        public override void Update(float dt) => Frame++;
        public override void FixedTick(float dt) { Fixed++; LastFixedDt = dt; }
    }

    private static void TestFixedTickHook()
    {
        var scene = new Core.Scene("FixedTickHook");
        var e = scene.CreateEntity("Ticker");
        var t = e.AddComponent<TickCounter>();
        scene.FlushPendingAdds();

        scene.Update(1f / 60f);
        Check("Update runs once per frame", t.Frame == 1 && t.Fixed == 0);

        for (int i = 0; i < 3; i++) scene.FixedTick(1f / 60f);
        Check("FixedTick runs once per step (3 steps)", t.Fixed == 3);
        Check("FixedTick leaves the frame clock alone", t.Frame == 1);
        Check("the fixed dt is passed through unchanged (1/60)", Math.Abs(t.LastFixedDt - 1f / 60f) < 1e-6f);

        t.Enabled = false;
        scene.FixedTick(1f / 60f);
        Check("Enabled=false stops FixedTick too", t.Fixed == 3);

        t.Enabled = true;
        e.Active = false;
        scene.FixedTick(1f / 60f);
        Check("an inactive entity receives no FixedTick", t.Fixed == 3);

        e.Active = true;
        scene.FixedTick(1f / 60f);
        Check("reactivating resumes it", t.Fixed == 4);
    }

    public static void Run()
    {
        Console.WriteLine("=== Physics self-test ===");

        TestFixedTickHook();

        {
            var (scene, physics) = NewScene();
            MakeCapsule(scene, "Bin", new Vector2(0, 0), isStatic: true);
            var (mover, rb) = MakeCapsuleMover(scene, "Player", new Vector2(-24, 4));
            rb.Velocity = new Vector2(60, 0);
            scene.FlushPendingAdds();

            for (int i = 0; i < 120; i++) physics.Update(1f / 60f);

            var t = mover.GetComponent<Transform>()!;
            Check("round slide: gets past the capsule and keeps going (no sticky stop)", t.Position.X > 10f);
            Check("round slide: pushed tangentially, taking a detour", MathF.Abs(t.Position.Y) > 4.5f);
        }

        {
            var (scene, physics) = NewScene();
            MakeCapsule(scene, "Bin", new Vector2(0, 0), isStatic: true);
            var (mover, rb) = MakeCapsuleMover(scene, "Player", new Vector2(-24, 0));
            scene.FlushPendingAdds();

            for (int i = 0; i < 120; i++)
            {
                rb.Velocity = new Vector2(60, 0);
                physics.Update(1f / 60f);
            }

            var t = mover.GetComponent<Transform>()!;
            Check("head-on charge does not tunnel through", t.Position.X < -6f);
        }

        {
            var (scene, physics) = NewScene();
            MakeBox(scene, "Wall", new Vector2(0, 0), new Vector2(8, 600), isStatic: true);
            var (mover, rb) = MakeBoxMover(scene, "Mover", new Vector2(-20, 0), new Vector2(8, 8));
            scene.FlushPendingAdds();

            for (int i = 0; i < 60; i++)
            {
                rb.Velocity = new Vector2(60, 40);
                physics.Update(1f / 60f);
            }

            var t = mover.GetComponent<Transform>()!;
            Check("box wall: X stops at the wall face", t.Position.X <= -7.9f);
            Check("box wall: Y keeps sliding", t.Position.Y > 20f);
        }

        {
            var (scene, physics) = NewScene();
            MakeBox(scene, "Wall", new Vector2(0, 0), new Vector2(8, 600), isStatic: true);
            var (mover, rb) = MakeCapsuleMover(scene, "Player", new Vector2(-20, 0));
            scene.FlushPendingAdds();

            for (int i = 0; i < 60; i++)
            {
                rb.Velocity = new Vector2(60, 40);
                physics.Update(1f / 60f);
            }

            var t = mover.GetComponent<Transform>()!;
            Check("capsule against box wall: X stops, Y keeps sliding", t.Position.X < -5f && t.Position.Y > 20f);
        }

        {
            var approaches = new (Vector2 start, Vector2 vel, string name)[]
            {
                (new Vector2(40, 62), new Vector2(-60, -30), "from the right, up-left diagonal"),
                (new Vector2(15, 75), new Vector2(-30, -60), "from below, up-left diagonal"),
                (new Vector2(7, 68), new Vector2(20, -60), "hugging the left wall, up-right diagonal"),
            };
            foreach (var (start, vel, name) in approaches)
            {
                var (scene, physics) = NewScene();
                MakeBox(scene, "WallTop", new Vector2(65.63613f, 40.339428f), new Vector2(158, 16), isStatic: true);
                MakeBox(scene, "WallLeft", new Vector2(-2.7924194f, 70.05372f), new Vector2(10, 168), isStatic: true);
                MakeCapsuleObstacle(scene, "Lamp", new Vector2(15, 54), radius: 5f, length: 3f);
                var (mover, rb) = MakeCapsuleMover(scene, "Player", start);
                scene.FlushPendingAdds();

                float maxJump = 0f;
                var t = mover.GetComponent<Transform>()!;
                for (int i = 0; i < 300; i++)
                {
                    var before = t.Position;
                    rb.Velocity = vel;
                    physics.Update(1f / 60f);
                    maxJump = MathF.Max(maxJump, Vector2.Distance(before, t.Position));
                }

                Check($"narrow gap ({name}): no tunnelling through the top wall (y>48)", t.Position.Y > 48f - 8f);
                Check($"narrow gap ({name}): no tunnelling through the left wall (x>2)", t.Position.X > 2.2f - 8f);
                Check($"narrow gap ({name}): no per-frame teleport (max {maxJump:0.0}px)", maxJump < 6f);
            }
        }

        TestCornerNudge();

        TestRoundLateralSlide();

        TestRoundFrontalSteer();

        Console.WriteLine($"=== {_pass} passed, {_fail} failed ===");
    }

    private static void TestCornerNudge()
    {
        {
            var (scene, physics) = NewScene();
            MakeBox(scene, "Corner", new Vector2(0, 6), new Vector2(8, 8), isStatic: true);
            var (mover, rb) = MakeCapsuleMover(scene, "P", new Vector2(-12, 0));
            scene.FlushPendingAdds();

            float startY = mover.GetComponent<Transform>()!.Position.Y;
            for (int i = 0; i < 240; i++) { rb.Velocity = new Vector2(24, 0); physics.Update(1f / 60f); }

            var p = mover.GetComponent<Transform>()!.Position;
            Check($"a corner is passable via the nudge (x {p.X:0.#} > 0, y shifted {p.Y - startY:+0.#;-0.#})", p.X > 0f);
        }

        {
            var (scene, physics) = NewScene();
            MakeBox(scene, "Wall", new Vector2(0, 0), new Vector2(8, 64), isStatic: true);
            var (mover, rb) = MakeCapsuleMover(scene, "P", new Vector2(-12, 0));
            scene.FlushPendingAdds();

            for (int i = 0; i < 240; i++) { rb.Velocity = new Vector2(24, 0); physics.Update(1f / 60f); }

            var p = mover.GetComponent<Transform>()!.Position;
            Check($"a real wall cannot be nudged through (x {p.X:0.#} < 0)", p.X < 0f);
        }

        {
            var (scene, physics) = NewScene();
            physics.CornerNudge = false;
            MakeBox(scene, "Corner", new Vector2(0, 6), new Vector2(8, 8), isStatic: true);
            var (mover, rb) = MakeCapsuleMover(scene, "P", new Vector2(-12, 0));
            scene.FlushPendingAdds();

            for (int i = 0; i < 240; i++) { rb.Velocity = new Vector2(24, 0); physics.Update(1f / 60f); }

            var p = mover.GetComponent<Transform>()!.Position;
            Check($"with the nudge off, it stops at the corner (x {p.X:0.#} < 0)", p.X < 0f);
        }

        {
            var (scene, physics) = NewScene();
            MakeBox(scene, "DoorL", new Vector2(-20, 0), new Vector2(24, 8), isStatic: true);
            MakeBox(scene, "DoorR", new Vector2(20, 0), new Vector2(24, 8), isStatic: true);
            var (mover, rb) = MakeCapsuleMover(scene, "P", new Vector2(6, 20));
            scene.FlushPendingAdds();

            for (int i = 0; i < 240; i++) { rb.Velocity = new Vector2(0, -24); physics.Update(1f / 60f); }

            var p = mover.GetComponent<Transform>()!.Position;
            Check($"misaligned at the doorway, still passes (y {p.Y:0.#} < -8, x corrected to {p.X:0.#})", p.Y < -8f);
        }

        {
            var (scene, physics) = NewScene();
            MakeBox(scene, "Wall", new Vector2(0, 0), new Vector2(8, 200), isStatic: true);
            var (mover, rb) = MakeCapsuleMover(scene, "P", new Vector2(-12, -40));
            scene.FlushPendingAdds();

            float y0 = mover.GetComponent<Transform>()!.Position.Y;
            for (int i = 0; i < 60; i++) { rb.Velocity = new Vector2(24, 24); physics.Update(1f / 60f); }
            float moved = mover.GetComponent<Transform>()!.Position.Y - y0;

            Check($"tangential speed survives wall contact (Y {moved:0.#}px in 1s, expected 24)",
                  moved > 20f && moved < 28f);
        }
    }

    private static void TestRoundLateralSlide()
    {
        {
            var (scene, physics) = NewScene();
            MakeCapsule(scene, "Lamp", new Vector2(0, 0), isStatic: true);
            var (mover, rb) = MakeCapsuleMover(scene, "P", new Vector2(-24, 2));
            scene.FlushPendingAdds();

            for (int i = 0; i < 240; i++) { rb.Velocity = new Vector2(24, 0); physics.Update(1f / 60f); }
            var p = mover.GetComponent<Transform>()!.Position;
            Check($"grazing a round edge slips past it (x {p.X:0.#} > 8, y {p.Y:0.#})", p.X > 8f);
        }

        {
            var (scene, physics) = NewScene();
            physics.CornerNudge = false;
            MakeCapsule(scene, "Lamp", new Vector2(0, 0), isStatic: true);
            var (mover, rb) = MakeCapsuleMover(scene, "P", new Vector2(-24, 2));
            scene.FlushPendingAdds();
            for (int i = 0; i < 240; i++) { rb.Velocity = new Vector2(24, 0); physics.Update(1f / 60f); }
            var p = mover.GetComponent<Transform>()!.Position;
            Check($"round sliding works with the nudge off (x {p.X:0.#} > 8)", p.X > 8f);
        }

        {
            var (scene, physics) = NewScene();
            MakeBox(scene, "Wall", new Vector2(0, -12), new Vector2(200, 8), isStatic: true);
            MakeCapsule(scene, "Lamp", new Vector2(0, -5), isStatic: true);
            var (mover, rb) = MakeCapsuleMover(scene, "P", new Vector2(-24, -3));
            scene.FlushPendingAdds();

            for (int i = 0; i < 480; i++) { rb.Velocity = new Vector2(24, 0); physics.Update(1f / 60f); }
            var p = mover.GetComponent<Transform>()!.Position;
            Check($"a capsule against a wall is passed by going under it (x {p.X:0.#} > 8, y {p.Y:0.#})", p.X > 8f);
        }

        {
            var (scene, physics) = NewScene();
            MakeBox(scene, "WallUp", new Vector2(0, -8), new Vector2(200, 8), isStatic: true);
            MakeBox(scene, "WallDn", new Vector2(0, 8), new Vector2(200, 8), isStatic: true);
            MakeCapsule(scene, "Plug", new Vector2(0, 0), isStatic: true);
            var (mover, rb) = MakeCapsuleMover(scene, "P", new Vector2(-24, 1));
            scene.FlushPendingAdds();

            for (int i = 0; i < 480; i++) { rb.Velocity = new Vector2(24, 0); physics.Update(1f / 60f); }
            var p = mover.GetComponent<Transform>()!.Position;
            Check($"a blocked corridor cannot be slipped through (x {p.X:0.#} < 0)", p.X < 0f);
        }
    }

    private static void TestRoundFrontalSteer()
    {
        {
            var (scene, physics) = NewScene();
            MakeCapsuleObstacle(scene, "Lamp", new Vector2(0, 0), radius: 5f, length: 3f);
            var (mover, rb) = MakeCapsuleMover(scene, "P", new Vector2(1, -30), length: 1f);
            scene.FlushPendingAdds();

            for (int i = 0; i < 300; i++) { rb.Velocity = new Vector2(0, 24); physics.Update(1f / 60f); }
            var p = mover.GetComponent<Transform>()!.Position;
            Check($"a 1px-offset vertical approach rolls off and passes (y {p.Y:0.#} > 8, x {p.X:0.#})", p.Y > 8f);
            Check($"it rolls off away from the centre (x {p.X:0.#} > 1)", p.X > 1f);
        }

        {
            var (scene, physics) = NewScene();
            MakeCapsuleObstacle(scene, "Lamp", new Vector2(0, 0), radius: 5f, length: 3f);
            var (mover, rb) = MakeCapsuleMover(scene, "P", new Vector2(2, -30), length: 1f);
            scene.FlushPendingAdds();

            for (int i = 0; i < 300; i++) { rb.Velocity = new Vector2(0, 24); physics.Update(1f / 60f); }
            var p = mover.GetComponent<Transform>()!.Position;
            Check($"a 2px offset passes too (y {p.Y:0.#} > 8)", p.Y > 8f);
        }

        {
            var (scene, physics) = NewScene();
            MakeCapsuleObstacle(scene, "Lamp", new Vector2(0, 0), radius: 5f, length: 3f);
            var (mover, rb) = MakeCapsuleMover(scene, "P", new Vector2(0, -30), length: 1f);
            scene.FlushPendingAdds();

            for (int i = 0; i < 240; i++) { rb.Velocity = new Vector2(0, 24); physics.Update(1f / 60f); }
            var p = mover.GetComponent<Transform>()!.Position;
            Check($"dead centre keeps stopping (y {p.Y:0.#} < -6)", p.Y < -6f);
            Check($"no sideways jitter at dead centre (|x| {MathF.Abs(p.X):0.##} < 0.5)", MathF.Abs(p.X) < 0.5f);
        }

        {
            var (scene, physics) = NewScene();
            MakeCircleObstacle(scene, "Bin", new Vector2(0, 0), radius: 4f);
            var (mover, rb) = MakeCapsuleMover(scene, "P", new Vector2(1, -30), length: 1f);
            scene.FlushPendingAdds();

            for (int i = 0; i < 300; i++) { rb.Velocity = new Vector2(0, 24); physics.Update(1f / 60f); }
            var p = mover.GetComponent<Transform>()!.Position;
            Check($"a near-head-on approach to a circle rolls off and passes (y {p.Y:0.#} > 8)", p.Y > 8f);
        }

        {
            var (scene, physics) = NewScene();
            MakeCapsuleObstacle(scene, "Lamp", new Vector2(0, 0), radius: 5f, length: 3f);
            MakeBox(scene, "Wall", new Vector2(14.5f, 0), new Vector2(16, 40), isStatic: true);
            var (mover, rb) = MakeCapsuleMover(scene, "P", new Vector2(1, -30), length: 1f);
            scene.FlushPendingAdds();

            for (int i = 0; i < 480; i++) { rb.Velocity = new Vector2(0, 24); physics.Update(1f / 60f); }
            var p = mover.GetComponent<Transform>()!.Position;
            Check($"a wall-backed prop is passed on the other side (y {p.Y:0.#} > 8, x {p.X:0.#} < 0)", p.Y > 8f && p.X < 0f);
        }

        {
            var (scene, physics) = NewScene();
            physics.RoundSteer = false;
            MakeCapsuleObstacle(scene, "Lamp", new Vector2(0, 0), radius: 5f, length: 3f);
            var (mover, rb) = MakeCapsuleMover(scene, "P", new Vector2(1, -30), length: 1f);
            scene.FlushPendingAdds();

            for (int i = 0; i < 240; i++) { rb.Velocity = new Vector2(0, 24); physics.Update(1f / 60f); }
            var p = mover.GetComponent<Transform>()!.Position;
            Check($"with steering off, the flat band stops it forever (y {p.Y:0.#} < -6)", p.Y < -6f);
        }
    }

    private static (Scene, PhysicsSystem) NewScene()
    {
        var scene = new Scene("PhysicsTest");
        return (scene, new PhysicsSystem(scene));
    }

    private static void MakeCapsuleObstacle(Scene scene, string name, Vector2 pos, float radius, float length)
    {
        var e = scene.CreateEntity(name);
        e.GetComponent<Transform>()!.Position = pos;
        var cap = e.AddComponent<CapsuleCollider2D>();
        cap.Radius = radius; cap.Length = length; cap.Horizontal = true;
    }

    private static void MakeCapsule(Scene scene, string name, Vector2 pos, bool isStatic)
    {
        var e = scene.CreateEntity(name);
        e.GetComponent<Transform>()!.Position = pos;
        var cap = e.AddComponent<CapsuleCollider2D>();
        cap.Radius = 3f; cap.Length = 2f; cap.Horizontal = true;
        if (!isStatic)
        {
            var rb = e.AddComponent<Rigidbody2D>();
            rb.UseGravity = false;
        }
    }

    private static (Entity, Rigidbody2D) MakeCapsuleMover(Scene scene, string name, Vector2 pos, float length = 2f)
    {
        var e = scene.CreateEntity(name);
        e.GetComponent<Transform>()!.Position = pos;
        var cap = e.AddComponent<CapsuleCollider2D>();
        cap.Radius = 3f; cap.Length = length; cap.Horizontal = true;
        var rb = e.AddComponent<Rigidbody2D>();
        rb.UseGravity = false;
        return (e, rb);
    }

    private static void MakeCircleObstacle(Scene scene, string name, Vector2 pos, float radius)
    {
        var e = scene.CreateEntity(name);
        e.GetComponent<Transform>()!.Position = pos;
        var c = e.AddComponent<CircleCollider2D>();
        c.Radius = radius;
    }

    private static void MakeBox(Scene scene, string name, Vector2 pos, Vector2 size, bool isStatic)
    {
        var e = scene.CreateEntity(name);
        e.GetComponent<Transform>()!.Position = pos;
        var box = e.AddComponent<BoxCollider2D>();
        box.Size = size;
    }

    private static (Entity, Rigidbody2D) MakeBoxMover(Scene scene, string name, Vector2 pos, Vector2 size)
    {
        var e = scene.CreateEntity(name);
        e.GetComponent<Transform>()!.Position = pos;
        var box = e.AddComponent<BoxCollider2D>();
        box.Size = size;
        var rb = e.AddComponent<Rigidbody2D>();
        rb.UseGravity = false;
        return (e, rb);
    }

    private static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name);
        if (ok) _pass++; else _fail++;
    }
}
