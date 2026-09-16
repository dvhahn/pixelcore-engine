using System;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Components;

namespace PixelCore.Runtime.Systems;

#if DEBUG

public static class InteractionHighlightSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== Aim highlight self-test (slot transitions) ===");
        _pass = _fail = 0;

        var saveMode = InteractionHighlight.Mode;
        try
        {
            TestOutline();
            TestTint();
            TestNone();
            TestForeignColorOverride();
        }
        finally
        {
            InteractionHighlight.Clear();
            InteractionHighlight.Mode = saveMode;
        }

        Console.WriteLine($"=== Highlight: {_pass} passed, {_fail} failed ===");
    }

    private static SpriteRenderer NewRenderer()
    {
        var e = new Core.Entity("Prop");
        e.AddComponent<Transform>();
        return e.AddComponent<SpriteRenderer>();
    }

    private static void TestOutline()
    {
        InteractionHighlight.Mode = InteractableHighlight.Outline;
        InteractionHighlight.Clear();

        var a = NewRenderer();
        var b = NewRenderer();

        InteractionHighlight.Apply(a);
        Check("* the aimed-at object gets an outline", a.OutlineOverride == InteractionHighlight.OutlineColor);
        Check("   the tint slot is left alone (both shapes must not be on at once)", a.ColorOverride == null);

        InteractionHighlight.Apply(b);
        Check("* when the target changes, the previous object switches off", a.OutlineOverride == null);
        Check("   the new object switches on", b.OutlineOverride == InteractionHighlight.OutlineColor);

        InteractionHighlight.Apply(null);
        Check("* releasing the aim switches it off", b.OutlineOverride == null);

        InteractionHighlight.Apply(a);
        InteractionHighlight.Clear();
        Check("* Clear restores it (the scene swap path)", a.OutlineOverride == null);
    }

    private static void TestTint()
    {
        InteractionHighlight.Mode = InteractableHighlight.Tint;
        InteractionHighlight.Clear();

        var a = NewRenderer();
        InteractionHighlight.Apply(a);
        Check("* tint mode uses ColorOverride", a.ColorOverride == InteractionHighlight.TintColor);
        Check("   the outline slot is empty", a.OutlineOverride == null);

        InteractionHighlight.Apply(null);
        Check("* the tint also reverts when released", a.ColorOverride == null);
    }

    private static void TestNone()
    {
        InteractionHighlight.Mode = InteractableHighlight.None;
        InteractionHighlight.Clear();

        var a = NewRenderer();
        InteractionHighlight.Apply(a);
        Check("* control: with the mode set to none, nothing is painted",
              a.OutlineOverride == null && a.ColorOverride == null);

        InteractionHighlight.Mode = InteractableHighlight.Outline;
        InteractionHighlight.Apply(a);
        InteractionHighlight.Mode = InteractableHighlight.None;
        InteractionHighlight.Apply(a);
        Check("* switching the mode off also restores what was already painted", a.OutlineOverride == null);
    }

    private static void TestForeignColorOverride()
    {
        InteractionHighlight.Mode = InteractableHighlight.Tint;
        InteractionHighlight.Clear();

        var a = NewRenderer();
        InteractionHighlight.Apply(a);

        var foreign = new Color(255, 0, 0);
        a.ColorOverride = foreign;

        InteractionHighlight.Apply(null);
        Check("** someone else's ColorOverride is not cleared (cutscene fades and hit flashes use the same slot)",
              a.ColorOverride == foreign);

        var b = NewRenderer();
        InteractionHighlight.Apply(b);
        InteractionHighlight.Apply(null);
        Check("   control: the colour we painted is cleared", b.ColorOverride == null);
    }

    private static void Check(string label, bool ok)
    {
        if (ok) { _pass++; Console.WriteLine($"  ✔ {label}"); }
        else { _fail++; Console.WriteLine($"  ✘ {label}"); }
    }
}

#endif
