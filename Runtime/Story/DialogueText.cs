using System;
using System.Collections.Generic;

namespace PixelCore.Runtime.Story;

public readonly struct DialogueSpan
{
    public bool Shake { get; init; }

    public float Speed { get; init; }

    public static readonly DialogueSpan Default = new() { Speed = 1f };
}

public readonly struct DialogueStop
{
    public int Index { get; init; }

    public string Token { get; init; }

    public bool IsPageBreak => Token.Length > 0 && Token[0] == '=';
}

public sealed class DialogueText
{
    public string Plain { get; }

    public DialogueSpan[] Styles { get; }

    public IReadOnlyList<DialogueStop> Stops { get; }

    public bool HasShake { get; }

    public int Length => Plain.Length;

    internal DialogueText(string plain, DialogueSpan[] styles, IReadOnlyList<DialogueStop> stops)
    {
        if (styles.Length != plain.Length)
            throw new ArgumentException(
                $"[Story] DialogueText invariant violated - {plain.Length} characters against {styles.Length} styles");
        Plain = plain;
        Styles = styles;
        Stops = stops;
        foreach (var s in styles) if (s.Shake) { HasShake = true; break; }
    }

    public static readonly DialogueText Empty =
        new("", Array.Empty<DialogueSpan>(), Array.Empty<DialogueStop>());
}
