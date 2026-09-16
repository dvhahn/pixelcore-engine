using System;
using System.Collections.Generic;
using System.Globalization;
using PixelCore.Runtime.Text;

namespace PixelCore.Runtime.Story;

public static class DialogueMarkup
{
    private static readonly HashSet<string> _warnedTags = new(StringComparer.Ordinal);

    public static void ResetWarnings() => _warnedTags.Clear();

    private static void WarnUnknownTag(string name, string text)
    {
        if (!_warnedTags.Add("unknown:" + name)) return;
        Console.WriteLine($"[Story] ⚠ unknown tag <{name}> - stripped, continuing. " +
                          $"supported: {string.Join(" ", StoryParser.KnownTags)}  <- \"{Snip(text)}\"");
    }

    private static void WarnUnclosedTag(string name, string text)
    {
        if (!_warnedTags.Add("unclosed:" + name)) return;
        Console.WriteLine($"[Story] ⚠ <{name}> is not closed - applied to the end of the line. " +
                          $"←  \"{Snip(text)}\"");
    }

    private static string Snip(string s) => s.Length <= 40 ? s : s[..40] + "…";

    public static string Plain(string? text) => Parse(text).Plain;

    public static DialogueText Parse(string? text)
    {
        if (string.IsNullOrEmpty(text)) return DialogueText.Empty;

        if (text.IndexOf('<') < 0 && text.IndexOf('{') < 0)
        {
            var flat = new DialogueSpan[text.Length];
            Array.Fill(flat, DialogueSpan.Default);
            return new DialogueText(text, flat, Array.Empty<DialogueStop>());
        }

        var chars = new List<char>(text.Length);
        var spans = new List<DialogueSpan>(text.Length);
        var stops = new List<DialogueStop>();
        var open = new List<(string Name, float Speed)>();

        DialogueSpan Current()
        {
            bool shake = false;
            float speed = 1f;
            for (int k = 0; k < open.Count; k++)
            {
                if (open[k].Name == TagShake) shake = true;
                else if (open[k].Name == TagSpeed) speed = open[k].Speed;
            }
            return new DialogueSpan { Shake = shake, Speed = speed };
        }

        void AppendRaw(ReadOnlySpan<char> rest)
        {
            var style = Current();
            foreach (char rc in rest) { chars.Add(rc); spans.Add(style); }
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '<')
            {
                int end = text.IndexOf('>', i + 1);
                if (end < 0) { AppendRaw(text.AsSpan(i)); break; }

                string inner = text[(i + 1)..end].Trim();
                if (inner.Length > 0)
                {
                    bool closing = inner[0] == '/';
                    if (closing) inner = inner[1..].Trim();
                    int sp = inner.IndexOf(' ');
                    string name = sp > 0 ? inner[..sp] : inner;
                    string arg = sp > 0 ? inner[(sp + 1)..].Trim() : "";

                    if (name.Length > 0)
                    {
                        if (Array.IndexOf(StoryParser.KnownTags, name) < 0) WarnUnknownTag(name, text);
                        else if (closing) CloseTag(open, name);
                        else open.Add((name, ParseSpeed(arg)));
                    }
                }

                i = end;
                continue;
            }

            if (c == '{')
            {
                int end = text.IndexOf('}', i + 1);
                if (end < 0) { AppendRaw(text.AsSpan(i)); break; }
                string token = text[(i + 1)..end];
                if (TextFormat.IsTimingToken(token))
                {
                    stops.Add(new DialogueStop { Index = chars.Count, Token = token });
                    i = end;
                    continue;
                }
                chars.Add(c); spans.Add(Current());
                continue;
            }

            if (c == ' ' && chars.Count > 0 && chars[^1] == ' ') continue;

            chars.Add(c); spans.Add(Current());
        }

        foreach (var (name, _) in open) WarnUnclosedTag(name, text);

        int lead = 0;
        while (lead < chars.Count && char.IsWhiteSpace(chars[lead])) lead++;
        int tail = chars.Count;
        while (tail > lead && char.IsWhiteSpace(chars[tail - 1])) tail--;

        int len = tail - lead;
        var plain = new char[len];
        var styles = new DialogueSpan[len];
        for (int k = 0; k < len; k++) { plain[k] = chars[lead + k]; styles[k] = spans[lead + k]; }

        if (lead > 0 || tail < chars.Count)
            for (int k = 0; k < stops.Count; k++)
                stops[k] = new DialogueStop
                {
                    Index = Math.Clamp(stops[k].Index - lead, 0, len),
                    Token = stops[k].Token,
                };

        return new DialogueText(new string(plain), styles, stops);
    }

    private const string TagShake = "shake";
    private const string TagSpeed = "speed";

    private static void CloseTag(List<(string Name, float Speed)> open, string name)
    {
        for (int k = open.Count - 1; k >= 0; k--)
            if (open[k].Name == name) { open.RemoveAt(k); return; }
    }

    private static float ParseSpeed(string arg)
        => float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v > 0f
            ? v : 1f;
}
