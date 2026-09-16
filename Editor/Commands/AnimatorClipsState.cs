using System.Collections.Generic;
using System.Linq;
using PixelCore.Runtime.Animation;
using PixelCore.Runtime.Components;

namespace PixelCore.Editor.Commands;

internal readonly struct AnimatorClipsState
{
    public readonly AnimationClip[] Clips;
    public readonly string? DefaultClip;

    private AnimatorClipsState(Animator a)
    {
        Clips = a.Clips.ToArray();
        DefaultClip = a.DefaultClip;
    }

    public static AnimatorClipsState Capture(Animator a) => new(a);

    public static void Apply(Animator a)
    {
        if (!string.IsNullOrEmpty(a.DefaultClip) && a.HasClip(a.DefaultClip!))
        {
            a.Play(a.DefaultClip!, restart: true);
            return;
        }

        a.RestorePlayback(default);
        a.ClearFrame();
    }

    public static void SetDefault(Animator a, string? clipName)
    {
        a.DefaultClip = clipName;
        Apply(a);
    }

    public static bool Remove(Animator a, string clipName)
    {
        if (!a.RemoveClip(clipName)) return false;
        Apply(a);
        return true;
    }

    public static void Restore(Animator a, in AnimatorClipsState s)
    {
        foreach (var name in a.Clips.Select(c => c.Name).ToList())
            a.RemoveClip(name);

        foreach (var clip in s.Clips)
            a.AddClip(clip);

        a.DefaultClip = s.DefaultClip;

        Apply(a);
    }

    public bool SameAs(in AnimatorClipsState o)
    {
        if (DefaultClip != o.DefaultClip || Clips.Length != o.Clips.Length) return false;
        var mine = Clips.Select(c => c.Name).OrderBy(n => n, System.StringComparer.Ordinal);
        var theirs = o.Clips.Select(c => c.Name).OrderBy(n => n, System.StringComparer.Ordinal);
        return mine.SequenceEqual(theirs);
    }
}
