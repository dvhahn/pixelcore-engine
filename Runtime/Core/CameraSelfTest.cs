using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Components;

namespace PixelCore.Runtime.Core;

public static class CameraSelfTest
{
    private static int _pass, _fail;

    private const float Dt = 1f / 60f;
    private const float WalkSpeed = 24f;

    private const float Scale = 4f;

    public static void Run()
    {
        Console.WriteLine("=== Camera self-test ===");

        TestStraightWalk();
        TestTurnDiagonal();
        TestStop();
        TestReversal();
        TestFollowOffset();

        TestDeadzone();
        TestBoundsClamp();
        TestSettleFreeze();
        TestStillIntegerSnap();
        TestNoTargetStaysPut();
        TestScreenWorldRoundTrip();

        Console.WriteLine($"=== {_pass} passed, {_fail} failed ===");
    }

    private static int ScreenPos(float p)
    {
        float r = MathF.Floor(p + 0.5f);
        return (int)(-r * Scale + MathF.Round((r - p) * Scale));
    }

    private static bool IsInteger(float p) => p == MathF.Floor(p);

    private static int CountPhaseKills(Sample[] s, int from, bool axisX)
    {
        int kills = 0;
        for (int i = Math.Max(from, 1); i < s.Length; i++)
        {
            float prev = axisX ? s[i - 1].Pos.X : s[i - 1].Pos.Y;
            float cur = axisX ? s[i].Pos.X : s[i].Pos.Y;
            if (!IsInteger(prev) && IsInteger(cur)) kills++;
        }
        return kills;
    }

    private readonly struct Sample
    {
        public readonly Vector2 Pos;
        public readonly int Sx, Sy;
        public Sample(Vector2 pos) { Pos = pos; Sx = ScreenPos(pos.X); Sy = ScreenPos(pos.Y); }
    }

    private static float StdDev(IReadOnlyList<int> advances)
    {
        if (advances.Count == 0) return 0f;
        float mean = 0f;
        foreach (var a in advances) mean += a;
        mean /= advances.Count;
        float sum = 0f;
        foreach (var a in advances) sum += (a - mean) * (a - mean);
        return MathF.Sqrt(sum / advances.Count);
    }

    private static void TestStraightWalk()
    {
        var (cam, tf) = NewRig();
        var s = Drive(cam, tf, f => new Vector2(WalkSpeed * f * Dt, 0f), frames: 240);

        var (frac, adv) = Measure(s, from: 60, axisX: true);
        int kills = CountPhaseKills(s, 60, axisX: true);
        float sd = StdDev(adv);
        int maxJump = 0; foreach (var a in adv) maxJump = Math.Max(maxJump, Math.Abs(a));

        Console.WriteLine($"  [straight] phase breaks {kills}/{adv.Count} frames, fraction survival {frac:P0}"
            + $", screen advance sd {sd:0.00}px, max jump {maxJump}px");

        Check($"straight: no phase breaks ({kills})", kills == 0);
        Check($"straight: the fraction survives ({frac:P0} > 90%)", frac > 0.90f);
        Check($"straight: the screen advance is even (sd {sd:0.00} < 1.0px)", sd < 1.0f);
        Check($"straight: no block jumps in a single frame (max {maxJump}px <= 2)", maxJump <= 2);
    }

    private static void TestTurnDiagonal()
    {
        var (cam, tf) = NewRig();
        const int turnAt = 120;
        var s = Drive(cam, tf, f =>
        {
            float x = WalkSpeed * Math.Min(f, turnAt) * Dt;
            float y = WalkSpeed * Math.Max(0, f - turnAt) * Dt;
            return new Vector2(x, y);
        }, frames: 300);

        var (fracX, advX) = Measure(s, from: turnAt, axisX: true);
        var (_, advY) = Measure(s, from: turnAt, axisX: false);
        int killsX = CountPhaseKills(s, turnAt, axisX: true);
        int killsY = CountPhaseKills(s, turnAt, axisX: false);

        int jumpX = 0; foreach (var a in advX) jumpX = Math.Max(jumpX, Math.Abs(a));
        int jumpY = 0; foreach (var a in advY) jumpY = Math.Max(jumpY, Math.Abs(a));

        Console.WriteLine($"  [turn] after the turn: phase breaks X {killsX}, Y {killsY}, X fraction survival {fracX:P0}"
            + $", screen advance sd X {StdDev(advX):0.00} Y {StdDev(advY):0.00}, max jump X {jumpX} Y {jumpY}px");

        Check($"turn: phase is not shaved during the turn (X {killsX}, Y {killsY})", killsX == 0 && killsY == 0);
        Check($"turn: no stepping jumps (max jump X {jumpX} Y {jumpY} <= 2px)", jumpX <= 2 && jumpY <= 2);
        Check($"turn: the travel axis (Y) advances evenly on screen (sd {StdDev(advY):0.00} < 1.0px)", StdDev(advY) < 1.0f);
    }

    private static void TestStop()
    {
        var on = MeasureStop(settle: 3f);
        var off = MeasureStop(settle: 0f);

        Console.WriteLine($"  [stop] settle 3 (on): tail {on.tail} frames ({on.tail * Dt * 1000:0}ms), worst tick {on.worst} frames ({on.worst * Dt * 1000:0}ms)");
        Console.WriteLine($"  [stop] settle 0 (default, off): tail {off.tail} frames ({off.tail * Dt * 1000:0}ms), worst tick {off.worst} frames ({off.worst * Dt * 1000:0}ms)");

        Check($"stop, settle 3: the tail is cut ({on.tail} frames <= 60)", on.tail <= 60);
        Check($"stop, settle 3: no dragging ticks (worst {on.worst} frames <= 12, i.e. 200ms)", on.worst <= 12);
        Check($"stop, settle 3: it freezes completely (no subpixel crawl)", on.frozen);

        Check($"stop, default: settling does not fire (tail {off.tail} > {on.tail})", off.tail > on.tail);
        Check($"stop, default: it still freezes eventually (no endless crawl)", off.frozen);
        Check($"stop: the default is 0, pinning down the fact that it is off", new Camera(320, 180).SettleDeadband == 0f);
    }

    private static (int tail, int worst, bool frozen) MeasureStop(float settle)
    {
        var (cam, tf) = NewRig();
        cam.SettleDeadband = settle;
        const int stopAt = 120;
        var s = Drive(cam, tf, f => new Vector2(WalkSpeed * Math.Min(f, stopAt) * Dt, 0f), frames: 600);

        int lastScreenMove = stopAt;
        for (int i = stopAt + 1; i < s.Length; i++)
            if (s[i].Sx != s[i - 1].Sx) lastScreenMove = i;

        bool frozen = s[^1].Pos.X == s[^2].Pos.X && s[^1].Pos.X == s[^20].Pos.X;

        int worst = 0, prevTick = stopAt;
        for (int i = stopAt + 1; i <= lastScreenMove; i++)
            if (s[i].Sx != s[i - 1].Sx) { worst = Math.Max(worst, i - prevTick); prevTick = i; }

        return (lastScreenMove - stopAt, worst, frozen);
    }

    private static void TestReversal()
    {
        var off = MeasureReversal(rounding: false);
        var on = MeasureReversal(rounding: true);

        Console.WriteLine($"  [reversal] rounding off (default): phase breaks {off.kills}, fraction survival {off.frac:P0}, max jump {off.maxJump}px");
        Console.WriteLine($"  [reversal] rounding on:            phase breaks {on.kills}, fraction survival {on.frac:P0}, max jump {on.maxJump}px");

        Check($"reversal, default: phase is not shaved ({off.kills})", off.kills == 0);
        Check($"reversal, default: the fraction survives ({off.frac:P0} > 90%)", off.frac > 0.90f);

        Check($"reversal, rounding on: phase is shaved as before ({on.kills} > 0)", on.kills > 0);
        Check($"reversal: the default is rounding off", !Camera.StateRoundingInDeadzone);

        Check($"reversal: no block jumps (on {on.maxJump}, off {off.maxJump} <= 2px)",
              on.maxJump <= 2 && off.maxJump <= 2);
    }

    private static (int kills, float frac, int maxJump) MeasureReversal(bool rounding)
    {
        bool prev = Camera.StateRoundingInDeadzone;
        Camera.StateRoundingInDeadzone = rounding;
        try
        {
            var (cam, tf) = NewRig();
            var s = Drive(cam, tf, f => new Vector2(30f * MathF.Sin(MathF.Tau * 0.25f * f * Dt), 0f), frames: 600);
            var (frac, adv) = Measure(s, from: 60, axisX: true);
            int maxJump = 0; foreach (var a in adv) maxJump = Math.Max(maxJump, Math.Abs(a));
            return (CountPhaseKills(s, 60, axisX: true), frac, maxJump);
        }
        finally { Camera.StateRoundingInDeadzone = prev; }
    }

    private static void TestFollowOffset()
    {
        Console.WriteLine("--- Follow offset ---");

        var fresh = new Camera(320, 180);
        Check("the default is (0, -18): 18px above the feet, the centre of the visible body (a cell centre of -24 would cross the shoulders)",
              fresh.FollowOffset == new Vector2(0f, -18f));
        Check("the default is integral, on the same grid as the feet",
              IsInteger(fresh.FollowOffset.X) && IsInteger(fresh.FollowOffset.Y));

        var (cam, tf) = NewRig();
        tf.Position = new Vector2(100f, 100f);
        cam.Update(Dt);
        Check($"the assignment snap includes the offset (100,100 gives camera {cam.Position.X:0},{cam.Position.Y:0} = 100,82)",
              cam.Position == new Vector2(100f, 82f));

        var s = Drive(cam, tf,
            f => new Vector2(100f + MathF.Min(f, 120) * WalkSpeed * Dt, 100f), 420);
        var end = s[^1].Pos;
        var expect = tf.Position + cam.FollowOffset;
        Check($"after walking and stopping, Y = target Y - 18 (measured {end.Y:0.##})", MathF.Abs(end.Y - expect.Y) < 0.001f);
        Check($"X is inside the deadzone (|delta| {MathF.Abs(end.X - expect.X):0.##} <= {cam.Deadzone.X:0})",
              MathF.Abs(end.X - expect.X) <= cam.Deadzone.X + 0.001f);

        var (cam0, tf0) = NewRig();
        cam0.FollowOffset = Vector2.Zero;
        tf0.Position = new Vector2(100f, 100f);
        cam0.Update(Dt);
        Check("* control: with offset 0 it sits on the feet (100,100)", cam0.Position == new Vector2(100f, 100f));
        Check($"* control: the two cameras differ in Y by exactly 18 (measured {cam0.Position.Y - cam.Position.Y:0.##})",
              MathF.Abs(cam0.Position.Y - cam.Position.Y - 18f) < 0.001f);
    }

    private static void TestDeadzone()
    {
        var (cam, tf) = NewRig();
        tf.Position = Vector2.Zero;
        cam.Update(Dt);
        var parked = cam.Position;

        Check("1. right after the snap, camera = target + FollowOffset",
            parked == new Vector2(0f, -18f));

        for (int i = 0; i < 60; i++) { tf.Position = new Vector2(10f, 0f); cam.Update(Dt); }
        Check("1. no movement inside the deadzone (X 10px)", cam.Position == parked);

        for (int i = 0; i < 600; i++) { tf.Position = new Vector2(200f, 0f); cam.Update(Dt); }
        float gap = 200f - cam.Position.X;
        Check($"1. outside the deadzone it converges on the edge (gap {gap:0.00}, expected 24)",
            MathF.Abs(gap - 24f) < 0.5f);
        Check("1. the vertical axis is untouched (axis independence)", MathF.Abs(cam.Position.Y - parked.Y) < 0.01f);

        var (cam0, tf0) = NewRig();
        cam0.Deadzone = Vector2.Zero;
        tf0.Position = Vector2.Zero;
        cam0.Update(Dt);
        var parked0 = cam0.Position;
        for (int i = 0; i < 60; i++) { tf0.Position = new Vector2(10f, 0f); cam0.Update(Dt); }
        Check("* 1. control: with a deadzone of 0 it follows even 10px", cam0.Position.X > parked0.X + 5f);
    }

    private static void TestBoundsClamp()
    {
        var (cam, tf) = NewRig();
        cam.Bounds = new Rectangle(0, 0, 1000, 1000);
        tf.Position = new Vector2(-500f, -500f);
        for (int i = 0; i < 600; i++) cam.Update(Dt);
        Check($"2. clamped at the top left (X {cam.Position.X:0.0} = 160, Y {cam.Position.Y:0.0} = 90)",
            MathF.Abs(cam.Position.X - 160f) < 0.01f && MathF.Abs(cam.Position.Y - 90f) < 0.01f);

        tf.Position = new Vector2(5000f, 5000f);
        for (int i = 0; i < 600; i++) cam.Update(Dt);
        Check($"2. clamped at the bottom right (X {cam.Position.X:0.0} = 840, Y {cam.Position.Y:0.0} = 910)",
            MathF.Abs(cam.Position.X - 840f) < 0.01f && MathF.Abs(cam.Position.Y - 910f) < 0.01f);

        var (cam2, tf2) = NewRig();
        cam2.Bounds = new Rectangle(0, 0, 200, 2000);
        tf2.Position = new Vector2(5000f, 5000f);
        for (int i = 0; i < 600; i++) cam2.Update(Dt);
        Check($"2. an axis narrower than the screen is centred (X {cam2.Position.X:0.0} = 100)",
            MathF.Abs(cam2.Position.X - 100f) < 0.01f);
        Check("2. the wide axis is still clamped (both branches coexist on one camera)",
            MathF.Abs(cam2.Position.Y - 1910f) < 0.01f);

        var (cam3, tf3) = NewRig();
        tf3.Position = new Vector2(-500f, -500f);
        for (int i = 0; i < 600; i++) cam3.Update(Dt);
        Check("* 2. control: with a null Bounds it leaves the map", cam3.Position.X < 0f);
    }

    private static void TestSettleFreeze()
    {
        var (cam, tf) = NewRig();
        cam.SettleDeadband = 8f;
        tf.Position = Vector2.Zero;
        cam.Update(Dt);
        tf.Position = new Vector2(28f, 0f);
        for (int i = 0; i < 12; i++) cam.Update(Dt);
        var frozen = cam.Position;
        for (int i = 0; i < 300; i++) cam.Update(Dt);
        Check($"3. it freezes inside the settle band (X {cam.Position.X:0.000} fixed)",
            cam.Position == frozen);
        Check("3. it froze before reaching the target (reaching it would mean the freeze was not measured)",
            cam.Position.X < 4f - 0.01f);

        var (cam0, tf0) = NewRig();
        tf0.Position = Vector2.Zero;
        cam0.Update(Dt);
        tf0.Position = new Vector2(28f, 0f);
        for (int i = 0; i < 12; i++) cam0.Update(Dt);
        var mid = cam0.Position;
        for (int i = 0; i < 300; i++) cam0.Update(Dt);
        Check("* 3. control: with SettleDeadband 0 (the default) it does not freeze", cam0.Position.X > mid.X);
    }

    private static void TestStillIntegerSnap()
    {
        var (cam, tf) = NewRig();
        cam.Deadzone = Vector2.Zero;
        tf.Position = Vector2.Zero;
        cam.Update(Dt);
        tf.Position = new Vector2(10.3f, 20.7f);
        for (int i = 0; i < 900; i++) cam.Update(Dt);

        Check($"4. a still target has its goal rounded to integers (X {cam.Position.X:0.000}, Y {cam.Position.Y:0.000})",
            cam.Position.X == 10f && cam.Position.Y == 3f);

        var (cam2, tf2) = NewRig();
        cam2.Deadzone = Vector2.Zero;
        tf2.Position = Vector2.Zero;
        cam2.Update(Dt);
        bool sawFraction = false;
        for (int i = 1; i <= 300; i++)
        {
            tf2.Position = new Vector2(10.3f + i * 0.37f, 0f);
            cam2.Update(Dt);
            if (!IsInteger(cam2.Position.X)) sawFraction = true;
        }
        Check("* 4. control: the fraction survives while moving", sawFraction);
    }

    private static void TestNoTargetStaysPut()
    {
        var cam = new Camera(320, 180) { GameWidth = 320, GameHeight = 180, PixelPerfect = true };
        cam.Position = new Vector2(123.45f, -67.8f);
        for (int i = 0; i < 120; i++) cam.Update(Dt);
        Check("5. with no target the camera stays put (fixed camera)",
            cam.Position == new Vector2(123.45f, -67.8f));

        cam.Bounds = new Rectangle(0, 0, 1000, 1000);
        cam.Update(Dt);
        Check("5. bounds clamping applies even with no target",
            MathF.Abs(cam.Position.X - 160f) < 0.01f && MathF.Abs(cam.Position.Y - 90f) < 0.01f);
    }

    private static void TestScreenWorldRoundTrip()
    {
        var cam = new Camera(640, 360) { Position = new Vector2(50f, -30f), Zoom = 2f };
        var world = new Vector2(12.5f, 77.25f);
        var back = cam.ScreenToWorld(cam.WorldToScreen(world));
        Check($"6. world to screen to world round trip (error {Vector2.Distance(world, back):0.0000})",
            Vector2.Distance(world, back) < 0.01f);

        var s0 = cam.WorldToScreen(world);
        cam.Position += new Vector2(10f, 0f);
        Check("* 6. control: moving the camera changes the screen coordinate",
            MathF.Abs(cam.WorldToScreen(world).X - s0.X) > 1f);
    }

    private static (Camera, Transform) NewRig()
    {
        var scene = new Scene("CameraTest");
        var target = scene.CreateEntity("Target");
        scene.FlushPendingAdds();
        var tf = target.GetComponent<Transform>()!;

        var cam = new Camera(320, 180)
        {
            GameWidth = 320,
            GameHeight = 180,
            PixelPerfect = true,
            Deadzone = new Vector2(24, 24),
            Target = target,
        };
        return (cam, tf);
    }

    private static Sample[] Drive(Camera cam, Transform tf, Func<int, Vector2> targetAt, int frames)
    {
        var samples = new Sample[frames];
        for (int f = 0; f < frames; f++)
        {
            tf.Position = targetAt(f);
            cam.Update(Dt);
            samples[f] = new Sample(cam.Position);
        }
        return samples;
    }

    private static (float fracSurvival, List<int> advances) Measure(Sample[] s, int from, bool axisX)
    {
        int integral = 0, fractional = 0;
        var advances = new List<int>();
        for (int i = Math.Max(from, 1); i < s.Length; i++)
        {
            float p = axisX ? s[i].Pos.X : s[i].Pos.Y;
            if (IsInteger(p)) integral++; else fractional++;
            advances.Add(axisX ? s[i].Sx - s[i - 1].Sx : s[i].Sy - s[i - 1].Sy);
        }
        return (fractional / (float)(integral + fractional), advances);
    }

    private static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name);
        if (ok) _pass++; else _fail++;
    }
}
