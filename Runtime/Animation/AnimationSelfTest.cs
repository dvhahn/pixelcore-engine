using System;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Animation;

public static class AnimationSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        _pass = 0; _fail = 0;
        Console.WriteLine("=== Animation playback self-test ===");

        TestFrameAdvance();
        TestLoopWrap();
        TestNonLoopHoldsLastFrame();
        TestEmptyClip();
        TestFrameEvents();
        TestSpeed();
        TestPivotHandover();

        Console.WriteLine($"=== Anim: {_pass} passed, {_fail} failed ===");
    }

    private static void TestFrameAdvance()
    {
        var clip = Clip("Walk", loop: true, 0.1f, 0.1f, 0.1f);
        Check("1. premise: total length is 0.3s", MathF.Abs(clip.TotalDuration - 0.3f) < 1e-5f,
            $"{clip.TotalDuration}");

        Check("1. t=0.00 is frame 0", clip.GetFrameIndex(0.00f) == 0);
        Check("1. t=0.05 is frame 0", clip.GetFrameIndex(0.05f) == 0);
        Check("1. t=0.10 is frame 1 (boundary is exclusive)", clip.GetFrameIndex(0.10f) == 1,
            $"{clip.GetFrameIndex(0.10f)}");
        Check("1. t=0.25 is frame 2", clip.GetFrameIndex(0.25f) == 2);

        var uneven = Clip("Uneven", loop: true, 0.5f, 0.1f);
        Check("1. uneven lengths: t=0.4 is frame 0", uneven.GetFrameIndex(0.4f) == 0);
        Check("1. uneven lengths: t=0.55 is frame 1", uneven.GetFrameIndex(0.55f) == 1);
        Check("* 1. control: differs from an even-split assumption",
            uneven.GetFrameIndex(0.4f) != (int)(0.4f / (uneven.TotalDuration / 2f)));
    }

    private static void TestLoopWrap()
    {
        var clip = Clip("Loop", loop: true, 0.1f, 0.1f);
        Check("2. one lap later it is back on frame 0", clip.GetFrameIndex(0.20f) == 0);
        Check("2. three laps later, still frame 0", clip.GetFrameIndex(0.60f) == 0);
        Check("2. mid-lap is frame 1", clip.GetFrameIndex(0.55f) == 1,
            $"{clip.GetFrameIndex(0.55f)}");

        Check("2. multiples land where floating point puts them (0.5 is frame 0; surprising but correct)",
            clip.GetFrameIndex(0.50f) == 0, $"{clip.GetFrameIndex(0.50f)}");

        var (anim, _) = Rig(clip);
        for (int i = 0; i < 60; i++) anim.Update(1f / 60f);
        Check("2. a looping clip keeps playing", anim.IsPlaying);
        Check("2. looping keeps accumulating time; it does not rewind", anim.CurrentTime > 0.9f,
            $"{anim.CurrentTime:0.000}");
    }

    private static void TestNonLoopHoldsLastFrame()
    {
        var clip = Clip("Open", loop: false, 0.1f, 0.1f);
        Check("3. past the end it stays on the last index", clip.GetFrameIndex(9f) == 1);

        var (anim, r) = Rig(clip);
        int completed = 0;
        anim.OnAnimationComplete += _ => completed++;
        for (int i = 0; i < 60; i++) anim.Update(1f / 60f);

        Check("3. playback stops after completion", !anim.IsPlaying);
        Check("3. the completion event fires once, not every frame", completed == 1, $"{completed}x");
        Check("* 3. the output slot survives completion", r.FrameOverride != null);
        Check("3. and it holds the last frame",
            r.FrameOverride?.SourceRect == clip.Frames[1].SourceRect);

        anim.ClearFrame();
        Check("* 3. control: ClearFrame empties the slot", r.FrameOverride == null);
        anim.Update(1f / 60f);
        Check("* 3. control: an emptied slot does not refill next tick", r.FrameOverride == null);
    }

    private static void TestEmptyClip()
    {
        var clip = new AnimationClip("Empty");
        Check("4. an empty clip reports index 0", clip.GetFrameIndex(1f) == 0);
        Check("4. an empty clip has zero length", clip.TotalDuration == 0f);

        var (anim, r) = Rig(clip);
        for (int i = 0; i < 10; i++) anim.Update(1f / 60f);
        Check("4. playing an empty clip does not throw", true);
        Check("4. an empty clip leaves the slot empty; there is no frame to draw", r.FrameOverride == null);
    }

    private static void TestFrameEvents()
    {
        var clip = new AnimationClip("Walk") { Loop = true };
        clip.AddFrame(new AnimationFrame(new Rectangle(0, 0, 16, 16), 0.1f, "step_l"));
        clip.AddFrame(new AnimationFrame(new Rectangle(16, 0, 16, 16), 0.1f));
        clip.AddFrame(new AnimationFrame(new Rectangle(32, 0, 16, 16), 0.1f, "step_r"));
        clip.AddFrame(new AnimationFrame(new Rectangle(48, 0, 16, 16), 0.1f));

        var (anim, _) = Rig(clip);
        int l = 0, r = 0;
        anim.OnFrameEvent += name => { if (name == "step_l") l++; else if (name == "step_r") r++; };

        for (int i = 0; i < 20; i++) anim.Update(1f / 60f);
        Check($"5. one lap fires step_l and step_r once each (measured {l}/{r})", l == 1 && r == 1);

        for (int i = 0; i < 24; i++) anim.Update(1f / 60f);
        Check($"5. two laps fire each twice (measured {l}/{r})", l == 2 && r == 2);

        int total = 0;
        var (anim2, _) = Rig(Clip("NoEvents", loop: true, 0.1f, 0.1f, 0.1f, 0.1f));
        anim2.OnFrameEvent += _ => total++;
        for (int i = 0; i < 48; i++) anim2.Update(1f / 60f);
        Check("* 5. control: a clip with no event names never fires", total == 0, $"{total}x");
    }

    private static void TestSpeed()
    {
        var (fast, _) = Rig(Clip("Fast", loop: true, 0.1f, 0.1f));
        fast.Speed = 2f;
        for (int i = 0; i < 30; i++) fast.Update(1f / 60f);
        Check($"6. Speed 2 runs clip time twice as fast ({fast.CurrentTime:0.000}, expected 1.0)",
            MathF.Abs(fast.CurrentTime - 1f) < 0.02f);

        var (slow, _) = Rig(Clip("Slow", loop: true, 0.1f, 0.1f));
        slow.Speed = 0.5f;
        for (int i = 0; i < 30; i++) slow.Update(1f / 60f);
        Check($"6. Speed 0.5 runs half as fast ({slow.CurrentTime:0.000}, expected 0.25)",
            MathF.Abs(slow.CurrentTime - 0.25f) < 0.02f);
    }

    private static void TestPivotHandover()
    {
        var a = Clip("A", loop: true, 0.1f);
        a.PivotX = 0.5f; a.PivotY = 1.0f;
        var b = Clip("B", loop: true, 0.1f);
        b.PivotX = 0.25f; b.PivotY = 0.75f;

        var (anim, r) = Rig(a);
        anim.AddClip(b);
        anim.Update(1f / 60f);
        Check("7. while A plays, the pivot is A's",
            r.FrameOverride?.PivotX == 0.5f && r.FrameOverride?.PivotY == 1.0f);

        anim.Play("B");
        anim.Update(1f / 60f);
        Check("* 7. switching to B moves the pivot to B's on the same tick",
            r.FrameOverride?.PivotX == 0.25f && r.FrameOverride?.PivotY == 0.75f,
            $"{r.FrameOverride?.PivotX} / {r.FrameOverride?.PivotY}");
        Check("7. the picture is B's too; a pivot-only switch is half a switch",
            r.FrameOverride?.SourceRect == b.Frames[0].SourceRect);
    }

    private static AnimationClip Clip(string name, bool loop, params float[] durations)
    {
        var clip = new AnimationClip(name) { Loop = loop };
        for (int i = 0; i < durations.Length; i++)
            clip.AddFrame(new AnimationFrame(new Rectangle(i * 16, 0, 16, 16), durations[i]));
        return clip;
    }

    private static (Animator, SpriteRenderer) Rig(AnimationClip clip)
    {
        var scene = new Scene("AnimTest");
        var e = scene.CreateEntity("Actor");
        var r = e.AddComponent<SpriteRenderer>();
        var anim = e.AddComponent<Animator>();
        scene.FlushPendingAdds();
        anim.AddClip(clip);
        anim.Play(clip.Name);
        return (anim, r);
    }

    private static void Check(string name, bool ok, string? detail = null)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name +
            (ok || detail == null ? "" : $"   [{detail}]"));
        if (ok) _pass++; else _fail++;
    }
}
