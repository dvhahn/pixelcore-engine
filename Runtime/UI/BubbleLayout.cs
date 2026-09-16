using System;
using System.Collections.Generic;

namespace PixelCore.Runtime.UI;

public interface IGlyphMetrics
{
    float Advance(char c);

    float LineHeight { get; }
}

public readonly struct BubbleLine
{
    public int Start { get; init; }
    public int Count { get; init; }
    public float Width { get; init; }
}

public sealed class BubbleLayout
{
    public int[] Order { get; }

    public BubbleLine[] Lines { get; }

    public int LinesPerPage { get; }

    public int PageCount { get; }

    public float Width { get; }

    public float Height { get; }

    private BubbleLayout(int[] order, BubbleLine[] lines, int linesPerPage, float width, float height)
    {
        Order = order;
        Lines = lines;
        LinesPerPage = Math.Max(1, linesPerPage);
        PageCount = lines.Length == 0 ? 1 : (lines.Length + LinesPerPage - 1) / LinesPerPage;
        Width = width;
        Height = height;
    }

    public int PageLineStart(int page) => page * LinesPerPage;

    public int PageLineCount(int page)
        => Math.Clamp(Lines.Length - PageLineStart(page), 0, LinesPerPage);

    public int PageCharCount(int page)
    {
        int n = 0, from = PageLineStart(page), count = PageLineCount(page);
        for (int i = from; i < from + count; i++) n += Lines[i].Count;
        return n;
    }

    public static readonly BubbleLayout Empty =
        new(Array.Empty<int>(), Array.Empty<BubbleLine>(), 1, 0f, 0f);

    private const string ForbiddenLineStart = ".,!?;:)]}>\"'…~·、。」』〉》”’%";

    public static bool IsForbiddenLineStart(char c) => ForbiddenLineStart.IndexOf(c) >= 0;

    public static bool IsCjk(char c) =>
        (c >= 0x1100 && c <= 0x11FF) ||
        (c >= 0x3000 && c <= 0x9FFF) ||
        (c >= 0xAC00 && c <= 0xD7AF);

    public static BubbleLayout Build(string plain, IGlyphMetrics metrics,
        float maxWidth, float minWidth, int maxLinesPerPage)
    {
        if (string.IsNullOrEmpty(plain) || maxWidth <= 0f) return Empty;

        float natural = 0f, run = 0f;
        foreach (char c in plain)
        {
            if (c == '\n') { natural = MathF.Max(natural, run); run = 0f; continue; }
            run += metrics.Advance(c);
        }
        natural = MathF.Max(natural, run);

        float wrapWidth = MathF.Min(natural, maxWidth);
        if (wrapWidth <= 0f) wrapWidth = maxWidth;

        var atoms = Atomize(plain);
        var order = new List<int>(plain.Length);
        var lines = new List<BubbleLine>();

        var placed = new List<(int OrderStart, float Width)>();
        int lineStart = 0;
        float lineWidth = 0f;

        void Emit()
        {
            lines.Add(new BubbleLine { Start = lineStart, Count = order.Count - lineStart, Width = lineWidth });
            lineStart = order.Count;
            lineWidth = 0f;
            placed.Clear();
        }

        float Put(in Atom a, bool atLineStart)
        {
            int start = order.Count;
            float w = 0f;
            int from = atLineStart ? a.TextStart : a.SpaceStart;
            for (int i = from; i < a.End; i++) { order.Add(i); w += metrics.Advance(plain[i]); }
            placed.Add((start, w));
            lineWidth += w;
            return w;
        }

        for (int k = 0; k < atoms.Count; k++)
        {
            var a = atoms[k];

            if (a.HardBreak) { Emit(); continue; }

            if (placed.Count == 0) { Put(a, atLineStart: true); continue; }

            float add = 0f;
            for (int i = a.SpaceStart; i < a.End; i++) add += metrics.Advance(plain[i]);
            if (lineWidth + add <= wrapWidth + 0.001f) { Put(a, atLineStart: false); continue; }

            if (IsForbiddenLineStart(plain[a.TextStart]))
            {
                if (placed.Count >= 2)
                {
                    var last = placed[^1];
                    order.RemoveRange(last.OrderStart, order.Count - last.OrderStart);
                    lineWidth -= last.Width;
                    placed.RemoveAt(placed.Count - 1);
                    Emit();
                    Put(atoms[k - 1], atLineStart: true);
                    Put(a, atLineStart: false);
                }
                else
                {
                    Put(a, atLineStart: false);
                }
                continue;
            }

            Emit();
            Put(a, atLineStart: true);
        }
        if (order.Count > lineStart || lines.Count == 0) Emit();

        float widest = 0f;
        foreach (var l in lines) widest = MathF.Max(widest, l.Width);
        float finalWidth = Math.Clamp(widest, MathF.Min(minWidth, maxWidth), maxWidth);

        int perPage = Math.Max(1, Math.Min(maxLinesPerPage, lines.Count));
        return new BubbleLayout(order.ToArray(), lines.ToArray(), perPage,
            finalWidth, perPage * metrics.LineHeight);
    }

    private readonly struct Atom
    {
        public int SpaceStart { get; init; }
        public int TextStart { get; init; }
        public int End { get; init; }
        public bool HardBreak { get; init; }
    }

    private static List<Atom> Atomize(string text)
    {
        var list = new List<Atom>();
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '\n') { list.Add(new Atom { HardBreak = true }); i++; continue; }

            int spaceStart = i;
            while (i < text.Length && text[i] == ' ') i++;
            if (i >= text.Length)
            {
                break;
            }
            if (text[i] == '\n')
            {
                list.Add(new Atom { HardBreak = true });
                i++;
                continue;
            }

            int textStart = i;
            if (IsCjk(text[i])) i++;
            else while (i < text.Length && text[i] != ' ' && text[i] != '\n' && !IsCjk(text[i])) i++;

            list.Add(new Atom { SpaceStart = spaceStart, TextStart = textStart, End = i });
        }
        return list;
    }
}
