using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Story;
using PixelCore.Runtime.Text;

namespace PixelCore.Runtime.UI;

public sealed class DialogueBubble
{
    public static float FontPx { get; set; } = Text.TextService.PixelFontBakePx;

    public static float LineHeight { get; set; } = 13f;

    public static int PadLeft { get; set; } = 6;
    public static int PadRight { get; set; } = 6;
    public static int PadTop { get; set; } = 4;
    public static int PadBottom { get; set; } = 10;

    public static int NameGap { get; set; } = 2;

    public static float MaxWidthRatio { get; set; } = 0.30f;

    public static float MinWidth { get; set; } = 28f;

    public static float OpenStepSeconds { get; set; } = 1f / 60f;

    public static float NameDelaySeconds { get; set; } = 0.05f;

    public static float BodyDelaySeconds { get; set; } = 0.20f;

    public static float ShakeSeconds { get; set; } = 0.25f;

    public static float ShakeAmplitude { get; set; } = 1f;

    public static float ShakeStepSeconds { get; set; } = 2f / 60f;

    public static Color FillColor { get; set; } = new(24, 22, 28, 240);
    public static Color TextColor { get; set; } = new(236, 234, 242);
    public static Color NameColor { get; set; } = new(255, 236, 180);
    public static Color ShadowColor { get; set; } = new(0, 0, 0, 110);

    public const string SkinPath = "Sprites/UI/DialogueUI.png";
    public const int BorderLeft = 9, BorderBottom = 8, BorderRight = 5, BorderTop = 5;

    public const int TailTipX = 5;

    private static bool _skinWarned;

    public static Texture2D? Skin()
    {
        var tex = TextureLoader.Instance.Load(SkinPath);
        if (tex == null)
        {
            if (!_skinWarned)
            {
                _skinWarned = true;
                Console.WriteLine($"[Bubble] ⚠ no bubble skin: Content/{SkinPath} - falling back to the bottom panel");
            }
            return null;
        }
        if (tex.Width < BorderLeft + BorderRight || tex.Height < BorderTop + BorderBottom)
        {
            if (!_skinWarned)
            {
                _skinWarned = true;
                Console.WriteLine($"[Bubble] ⚠ the bubble skin is smaller than the 9-slice frame " +
                                  $"({tex.Width}x{tex.Height} < {BorderLeft + BorderRight}x{BorderTop + BorderBottom}) - falling back to the bottom panel");
            }
            return null;
        }
        return tex;
    }

    public static void ResetSkinWarning() => _skinWarned = false;

    public static Point GizmoNominalSize() => new(
        (int)MathF.Round(80 / BubbleScales.TextDivisor + (PadLeft + PadRight) / BubbleScales.SkinDivisor),
        (int)MathF.Round(2 * LineHeight / BubbleScales.TextDivisor + (PadTop + PadBottom) / BubbleScales.SkinDivisor));

    public string SpeakerId { get; private set; } = "";

    public string? Name { get; private set; }

    private readonly List<(DialogueText Text, BubbleLayout Layout)> _parts = new();

    private readonly List<(int Part, int Page)> _pages = new();

    private string[] _rawParts = Array.Empty<string>();

    private float _laidOutWidth = -1f;
    private int _laidOutLines;

    public float ContentWidth { get; private set; }
    public float ContentHeight { get; private set; }

    public long Stamp { get; set; }

    private float _openTime;
    private bool _introDone;
    private float _shakeLeft;
    private int _shakeStep;
    private float _shakeAcc;
    private int _shakeScanned;
    private int _shakeScannedPage = -1;

    private static float OpenSeconds => OpenStepSeconds * 3f;

    public bool BoxOpen => _introDone || _openTime >= OpenSeconds;

    public bool NameShown => _introDone || _openTime >= OpenSeconds + NameDelaySeconds;

    public bool BodyReady => _introDone || _openTime >= OpenSeconds + NameDelaySeconds + BodyDelaySeconds;

    public float OpenFraction
    {
        get
        {
            if (_introDone) return 1f;
            int step = (int)(_openTime / MathF.Max(0.0001f, OpenStepSeconds));
            return step switch { 0 => 0f, 1 => 0.40f, 2 => 0.75f, _ => 1f };
        }
    }

    public void SkipIntro() => _introDone = true;

    public void Tick(float deltaTime, int page, int revealed)
    {
        _openTime += deltaTime;

        if (page != _shakeScannedPage) { _shakeScannedPage = page; _shakeScanned = 0; }

        var text = TextOf(page);
        bool fired = false;
        if (revealed > _shakeScanned)
        {
            for (int i = _shakeScanned; i < revealed; i++)
            {
                int plainIndex = PlainIndexOf(page, i);
                if (plainIndex < 0 || plainIndex >= text.Styles.Length) continue;
                bool here = text.Styles[plainIndex].Shake;
                bool before = plainIndex > 0 && text.Styles[plainIndex - 1].Shake;
                if (here && !before)
                {
                    _shakeLeft = ShakeSeconds; _shakeStep = 0; _shakeAcc = 0f; fired = true;
                }
            }
            _shakeScanned = revealed;
        }

        if (fired) return;

        if (_shakeLeft <= 0f) return;
        _shakeLeft = MathF.Max(0f, _shakeLeft - deltaTime);
        _shakeAcc += deltaTime;
        while (_shakeAcc >= ShakeStepSeconds) { _shakeStep++; _shakeAcc -= ShakeStepSeconds; }
    }

    public bool Shaking => _shakeLeft > 0f;

    private static Vector2 ShakeDir(int step) => (step & 7) switch
    {
        0 => new Vector2(1, 0),
        1 => new Vector2(-1, 1),
        2 => new Vector2(1, -1),
        3 => new Vector2(-1, 0),
        4 => new Vector2(0, 1),
        5 => new Vector2(1, 1),
        6 => new Vector2(-1, -1),
        _ => new Vector2(0, -1),
    };

    public Vector2 ShakeOffset(int textScale)
    {
        if (_shakeLeft <= 0f) return Vector2.Zero;
        float k = _shakeLeft / MathF.Max(0.0001f, ShakeSeconds);
        return ShakeDir(_shakeStep) * (ShakeAmplitude * k * k * textScale);
    }

    public Vector2 BackgroundShakeOffset(int textScale) => ShakeOffset(textScale) * -2f;

    public int PageCount => _pages.Count;

    public DialogueText TextOf(int page)
        => page >= 0 && page < _pages.Count ? _parts[_pages[page].Part].Text : DialogueText.Empty;

    public int CharCountOf(int page)
    {
        if (page < 0 || page >= _pages.Count) return 0;
        var (part, sub) = _pages[page];
        return _parts[part].Layout.PageCharCount(sub);
    }

    public string PlainOf(int page)
    {
        if (page < 0 || page >= _pages.Count) return "";
        var (part, sub) = _pages[page];
        var (text, layout) = _parts[part];
        var sb = new System.Text.StringBuilder();
        int from = layout.PageLineStart(sub), count = layout.PageLineCount(sub);
        for (int i = from; i < from + count; i++)
        {
            var line = layout.Lines[i];
            for (int k = 0; k < line.Count; k++) sb.Append(text.Plain[layout.Order[line.Start + k]]);
        }
        return sb.ToString();
    }

    public int PlainIndexOf(int page, int drawIndex)
    {
        if (page < 0 || page >= _pages.Count || drawIndex < 0) return -1;
        var (part, sub) = _pages[page];
        var layout = _parts[part].Layout;
        int from = layout.PageLineStart(sub), count = layout.PageLineCount(sub);
        int seen = 0;
        for (int i = from; i < from + count; i++)
        {
            var line = layout.Lines[i];
            if (drawIndex < seen + line.Count) return layout.Order[line.Start + (drawIndex - seen)];
            seen += line.Count;
        }
        return -1;
    }

    public void SetContent(string speakerId, string? name, IReadOnlyList<string> rawParts,
        float maxContentWidth, int maxLinesPerPage, IGlyphMetrics? metrics = null)
    {
        SpeakerId = speakerId ?? "";
        Name = string.IsNullOrWhiteSpace(name) ? null : name;
        ResetClocks();

        _rawParts = new string[rawParts.Count];
        for (int i = 0; i < rawParts.Count; i++) _rawParts[i] = rawParts[i];

        BuildLayout(maxContentWidth, maxLinesPerPage, metrics);
    }

    public bool NeedsRelayout(float maxContentWidth, int maxLinesPerPage)
        => _pages.Count > 0
           && (MathF.Abs(_laidOutWidth - maxContentWidth) > 0.5f || _laidOutLines != maxLinesPerPage);

    public void Relayout(float maxContentWidth, int maxLinesPerPage, IGlyphMetrics? metrics = null)
        => BuildLayout(maxContentWidth, maxLinesPerPage, metrics);

    private void BuildLayout(float maxContentWidth, int maxLinesPerPage, IGlyphMetrics? metrics)
    {
        _parts.Clear();
        _pages.Clear();
        _laidOutWidth = maxContentWidth;
        _laidOutLines = maxLinesPerPage;

        metrics ??= GlyphMetrics.For(FontPx, LineHeight, FontSlot.Dialogue);

        float widest = 0f;
        int tallest = 1;
        foreach (var raw in _rawParts)
        {
            if (string.IsNullOrEmpty(raw)) continue;
            var text = DialogueMarkup.Parse(raw);
            if (text.Length == 0) continue;
            var layout = BubbleLayout.Build(text.Plain, metrics, maxContentWidth, MinWidth, maxLinesPerPage);
            _parts.Add((text, layout));
            widest = MathF.Max(widest, layout.Width);
            tallest = Math.Max(tallest, layout.LinesPerPage);
        }

        ContentWidth = widest;
        ContentHeight = tallest * metrics.LineHeight;

        for (int p = 0; p < _parts.Count; p++)
            for (int sub = 0; sub < _parts[p].Layout.PageCount; sub++)
                _pages.Add((p, sub));
    }

    public void Clear()
    {
        SpeakerId = "";
        Name = null;
        _parts.Clear();
        _pages.Clear();
        _rawParts = Array.Empty<string>();
        _laidOutWidth = -1f;
        ContentWidth = ContentHeight = 0f;
        ResetClocks();
    }

    private void ResetClocks()
    {
        _openTime = 0f;
        _introDone = false;
        _shakeLeft = 0f;
        _shakeStep = 0;
        _shakeAcc = 0f;
        _shakeScanned = 0;
        _shakeScannedPage = -1;
    }

    private static readonly HashSet<string> _warnedSpeakers = new(StringComparer.Ordinal);

    public static void ResetWarnings() => _warnedSpeakers.Clear();

    public static Entity? ResolveSpeaker(Scene? scene, string speakerId)
    {
        if (scene == null || string.IsNullOrEmpty(speakerId)) return null;

        foreach (var candidate in new[] { Cutscenes.CutsceneDirector.ResolveAlias(speakerId), speakerId })
        {
            var hits = scene.FindEntities(candidate);
            if (hits.Count > 1)
            {
                if (_warnedSpeakers.Add(speakerId))
                    Console.WriteLine($"[Bubble] ⚠ speaker '{speakerId}' is ambiguous - {hits.Count} candidates for '{candidate}'. " +
                                      "the bubble cannot be seated (the names have to be made distinct)");
                return null;
            }
            if (hits.Count == 1) return hits[0];
        }

        if (_warnedSpeakers.Add(speakerId))
            Console.WriteLine($"[Bubble] ⚠ no entity in the scene matches speaker '{speakerId}' - " +
                              "drawing the bottom panel instead (check the cast alias registration or the entity name)");
        return null;
    }

    public Point BoxSize(in BubbleScales s)
    {
        int contentW = (int)MathF.Ceiling(ContentWidth) * s.Text;
        int contentH = (int)MathF.Ceiling(ContentHeight) * s.Text;
        int nameH = Name != null ? (int)MathF.Ceiling(LineHeight + NameGap) * s.Text : 0;
        return new Point(
            contentW + (PadLeft + PadRight) * s.Skin,
            contentH + nameH + (PadTop + PadBottom) * s.Skin);
    }

    public void Draw(SpriteBatch sb, Texture2D skin, Vector2 anchorScreen, in BubbleScales scales,
        int page, int revealed, bool showCaret, float blinkTimer)
    {
        if (page < 0 || page >= _pages.Count) return;
        int scale = scales.Skin;

        var size = BoxSize(scales);

        float openF = OpenFraction;
        int drawnH = openF >= 1f
            ? size.Y
            : Math.Max(scale, (int)MathF.Round(size.Y * openF / scale) * scale);

        var shake = ShakeOffset(scales.Text);
        var bgShake = BackgroundShakeOffset(scales.Text);

        var target = new Vector2(anchorScreen.X - TailTipX * scale, anchorScreen.Y - drawnH);
        var final = target;

        final += bgShake;

        var box = new Rectangle((int)MathF.Floor(final.X), (int)MathF.Floor(final.Y), size.X, drawnH);

        if (drawnH < (BorderTop + BorderBottom) * scale)
        {
            var px1 = UIDraw.Pixel(sb.GraphicsDevice);
            sb.Draw(px1, new Rectangle(box.X + scale, box.Y + scale, box.Width, box.Height), ShadowColor);
            sb.Draw(px1, box, FillColor);
            return;
        }

        UIDraw.NineSlice(sb, skin, new Rectangle(0, 0, skin.Width, skin.Height),
            new Rectangle(box.X + scale, box.Y + scale, box.Width, box.Height),
            BorderLeft, BorderTop, BorderRight, BorderBottom, scale, ShadowColor);
        UIDraw.NineSlice(sb, skin, new Rectangle(0, 0, skin.Width, skin.Height), box,
            BorderLeft, BorderTop, BorderRight, BorderBottom, scale, FillColor);

        if (!BoxOpen) return;

        int textLeft = box.X + PadLeft * scale + (int)MathF.Round(shake.X);
        int textTop = box.Y + PadTop * scale + (int)MathF.Round(shake.Y);

        if (Name != null)
        {
            if (NameShown)
                TextService.DrawScaled(sb, Name, new Vector2(textLeft, textTop),
                    FontPx, scales.Text, NameColor, FontSlot.Dialogue);
            textTop += (int)MathF.Ceiling(LineHeight + NameGap) * scales.Text;
        }

        DrawText(sb, new Point(textLeft, textTop), scales, page, revealed, TextColor);

        if (showCaret && blinkTimer % 0.8f < 0.5f)
        {
            var px = UIDraw.Pixel(sb.GraphicsDevice);
            int tx = box.Right - (PadRight + 5) * scale;
            int ty = box.Bottom - (PadBottom - 1) * scale;
            sb.Draw(px, new Rectangle(tx, ty, 5 * scale, scale), NameColor);
            sb.Draw(px, new Rectangle(tx + scale, ty + scale, 3 * scale, scale), NameColor);
            sb.Draw(px, new Rectangle(tx + 2 * scale, ty + 2 * scale, scale, scale), NameColor);
        }
    }

    public void DrawText(SpriteBatch sb, Point origin, in BubbleScales scales, int page, int revealed, Color color)
    {
        int scale = scales.Text;
        if (page < 0 || page >= _pages.Count || scale <= 0) return;
        var (partIndex, sub) = _pages[page];
        var (text, layout) = _parts[partIndex];

        var metrics = GlyphMetrics.For(FontPx, LineHeight, FontSlot.Dialogue);
        int budget = revealed;
        int from = layout.PageLineStart(sub), count = layout.PageLineCount(sub);
        for (int i = from; i < from + count && budget > 0; i++)
        {
            var line = layout.Lines[i];
            float penX = 0f;
            int lineTop = origin.Y + (int)MathF.Round((i - from) * LineHeight) * scale;

            for (int k = 0; k < line.Count && budget > 0; k++, budget--)
            {
                char ch = text.Plain[layout.Order[line.Start + k]];
                float adv = metrics.Advance(ch);
                if (ch != ' ')
                {
                    TextService.DrawScaled(sb, ch.ToString(),
                        new Vector2(origin.X + MathF.Round(penX * scale), lineTop),
                        FontPx, scale, color, FontSlot.Dialogue);
                }
                penX += adv;
            }
        }
    }
}

internal sealed class GlyphMetrics : IGlyphMetrics
{
    private static GlyphMetrics? _cached;

    private readonly Dictionary<char, float> _advance = new();
    private readonly float _basePx;
    private readonly FontSlot _slot;

    public float LineHeight { get; }

    private GlyphMetrics(float basePx, float lineHeight, FontSlot slot)
    {
        _basePx = basePx;
        _slot = slot;
        LineHeight = lineHeight;
    }

    public static GlyphMetrics For(float basePx, float lineHeight, FontSlot slot)
    {
        if (_cached == null || _cached._basePx != basePx || _cached.LineHeight != lineHeight || _cached._slot != slot)
            _cached = new GlyphMetrics(basePx, lineHeight, slot);
        return _cached;
    }

    public float Advance(char c)
    {
        if (_advance.TryGetValue(c, out float w)) return w;
        w = TextService.Measure(c.ToString(), _basePx, _slot).X;
        _advance[c] = w;
        return w;
    }
}
