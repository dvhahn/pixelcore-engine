using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Text;

namespace PixelCore.Runtime.UI;

public static class DialogueBox
{
    public static int VirtualWidth { get; set; } = 320;
    public static int VirtualHeight { get; set; } = 180;

    public static int MaxLinesPerPage { get; set; } = 3;
    public static int BoxMarginBottom { get; set; } = 10;
    public static int BoxPadding { get; set; } = 8;
    public static Color FillColor { get; set; } = new(14, 12, 18, 236);
    public static Color BorderColor { get; set; } = new(96, 88, 108);
    public static Color TextColor { get; set; } = new(232, 230, 238);
    public static Color SpeakerColor { get; set; } = new(255, 236, 180);

    public static bool AutoAdvance { get; set; }

    public static bool FastForward { get; set; }

    public static bool Active { get; private set; }

    public static bool JustClosed { get; private set; }

    public static string CurrentText =>
        _choices != null || _bubble == null ? "" : _bubble.PlainOf(_pageIndex);

    private static string[]? _choices;
    private static int _choiceIndex;
    private static int _chosen = -1;

    public static bool ChoiceActive => _choices != null;

    private static DialogueBubble? _bubble;

    private static string _closedSpeakerId = "";

    private static float _uiScale = 5f;

    private static string? _speaker;
    private static int _pageIndex;
    private static float _revealed;
    private static float _blinkTimer;
    private static bool _suppressInputOnce;
    private static float _charClock;
    private static float _doneTime;
    private static bool _hurry;

    public static void Show(params string[] pages) => Show(null, pages);

    [Obsolete("A one-argument Show(\"text\") call puts the text in the speaker slot and no box appears. " +
              "With no speaker, write Show(null, \"text\").", error: true)]
    public static void Show(string page)
        => throw new InvalidOperationException("unreachable - blocked at compile time");

    public static string? CurrentSpeaker => _speaker;

    public static string CurrentSpeakerId => _bubble?.SpeakerId ?? "";

    public static bool IntroPlaying => _bubble is { BodyReady: false };

    public static void Show(string? speaker, params string[] pages)
        => ShowParts(speaker ?? "", speaker, pages);

    public static void ShowLine(string speakerId, string? speakerLabel, string rawText)
        => ShowParts(speakerId, speakerLabel, new[] { rawText });

    private static void ShowParts(string speakerId, string? label, string[] parts)
    {
        bool sameBreath = JustClosed && _closedSpeakerId == (speakerId ?? "");

        var bubble = BubblePool.Acquire(speakerId ?? "");
        bubble.SetContent(speakerId, label, parts, MaxContentWidth, MaxLinesPerPage);
        _bubble = bubble;

        if (_bubble.PageCount == 0)
        {
            Console.WriteLine(
                $"[Dialogue] ⚠ zero pages, so no box is opened (speaker='{(string.IsNullOrEmpty(label) ? "(none)" : label)}', {parts.Length} pages). " +
                "body text in the speaker slot means a one-argument Show(\"text\") call - use Show(null, text)");
            _bubble.Clear();
            BubblePool.Release(_bubble);
            _bubble = null;
            return;
        }

        _choices = null;
        _chosen = -1;

        _speaker = string.IsNullOrWhiteSpace(label) ? null : label;
        _pageIndex = 0;
        _revealed = 0f;
        _blinkTimer = 0f;
        _suppressInputOnce = true;
        Active = true;
        Cutscenes.FreezeState.SetSoft(Cutscenes.FreezeSource.Dialogue, true);
        if (sameBreath || FastForward) _bubble.SkipIntro();
        BeginPage();
    }

    private static float MaxContentWidth => ComputeMaxContentWidth();

    private static float ComputeMaxContentWidth()
    {
        var s = BubbleScales.For(_uiScale);
        float maxScreen = VirtualWidth * DialogueBubble.MaxWidthRatio * s.World;
        float pad = (DialogueBubble.PadLeft + DialogueBubble.PadRight) * s.Skin;
        return MathF.Max(16f, (maxScreen - pad) / s.Text);
    }

    public static void ShowChoices(string? speaker, string[] options)
    {
        if (options == null || options.Length == 0)
        {
            Console.WriteLine("[Dialogue] ⚠ zero options, so no list is opened - a bug in the caller");
            return;
        }

        BubblePool.Release(_bubble);
        _bubble = null;
        _choices = options;
        _choiceIndex = 0;
        _chosen = -1;
        _speaker = string.IsNullOrWhiteSpace(speaker) ? null : speaker;
        _blinkTimer = 0f;
        _suppressInputOnce = true;
        Active = true;
        Cutscenes.FreezeState.SetSoft(Cutscenes.FreezeSource.Dialogue, true);
    }

    public static bool TryTakeChoice(out int index)
    {
        index = _chosen;
        if (_chosen < 0) return false;
        _chosen = -1;
        return true;
    }

    private static void BeginPage()
    {
        _charClock = 0f;
        _doneTime = 0f;
        _hurry = false;
    }

    public static void Close()
    {
        if (!Active) return;
        Active = false;
        Cutscenes.FreezeState.SetSoft(Cutscenes.FreezeSource.Dialogue, false);
        JustClosed = true;
        _closedSpeakerId = _bubble?.SpeakerId ?? "";
        BubblePool.Release(_bubble);
        _bubble = null;
        _choices = null;
        _choiceIndex = 0;
        _chosen = -1;
        _hurry = false;
        AutoAdvance = false;
    }

    public static void Update(float deltaTime)
    {
        JustClosed = false;
        if (!Active) return;

        _blinkTimer += deltaTime;
        if (_suppressInputOnce) { _suppressInputOnce = false; return; }

        if (_choices != null) { UpdateChoice(); return; }
        if (_bubble == null) return;

        if (FastForward) _bubble.SkipIntro();
        _bubble.Tick(deltaTime, _pageIndex, (int)_revealed);

        if (!_bubble.BodyReady) return;

        int total = _bubble.CharCountOf(_pageIndex);

        if (_revealed < total)
        {
            if (FastForward)
                _revealed = total;
            else
            {
                if (InputMap.IsPressed(GameAction.Confirm)) Hurry();
                AdvanceTypewriter(deltaTime, total);
            }
            if (_revealed >= total) _doneTime = 0f;
            return;
        }

        _doneTime += deltaTime;
        bool advance = FastForward
            || InputMap.IsPressed(GameAction.Confirm)
            || (AutoAdvance && _doneTime >= DialogueTiming.TailSeconds);
        if (!advance) return;

        if (_pageIndex + 1 < _bubble.PageCount)
        {
            _pageIndex++;
            _revealed = 0f;
            _blinkTimer = 0f;
            BeginPage();
        }
        else Close();
    }

    private static void UpdateChoice()
    {
        int n = _choices!.Length;
        if (InputMap.IsPressed(GameAction.MoveUp)) { _choiceIndex = (_choiceIndex - 1 + n) % n; _blinkTimer = 0f; }
        if (InputMap.IsPressed(GameAction.MoveDown)) { _choiceIndex = (_choiceIndex + 1) % n; _blinkTimer = 0f; }

        if (!InputMap.IsPressed(GameAction.Confirm)) return;

        int picked = _choiceIndex;
        _choices = null;
        Close();
        _chosen = picked;
    }

    public static void Hurry()
    {
        if (_bubble != null && !PageDone) _hurry = true;
    }

    public static bool PageRevealed => PageDone;

    private static void AdvanceTypewriter(float deltaTime, int total)
    {
        _charClock += deltaTime * (_hurry ? DialogueTiming.HurrySpeed : 1f);
        while (_revealed < total)
        {
            int draw = (int)_revealed;
            float need = NextCharSeconds(draw);
            if (_charClock < need) break;
            _charClock -= need;
            _revealed = draw + 1;
        }
    }

    private static float NextCharSeconds(int drawIndex)
    {
        var text = _bubble!.TextOf(_pageIndex);
        int plain = _bubble.PlainIndexOf(_pageIndex, drawIndex);
        if (plain < 0 || plain >= text.Length) return DialogueTiming.CjkSeconds;

        float sec = DialogueTiming.CharSeconds(text.Plain[plain]);
        float speed = text.Styles[plain].Speed;
        if (speed > 0f) sec /= speed;
        foreach (var stop in text.Stops)
            if (!stop.IsPageBreak && stop.Index == plain) sec += DialogueTiming.StopSeconds(stop.Token);
        return sec;
    }

    internal static BubbleScales ScalesFor(int areaHeight) => BubbleScales.For(UiScale.For(areaHeight));

    public static void DrawScreen(SpriteBatch sb, Rectangle area, float uiScale,
        RasterizerState? rasterizer = null,
        Scene? scene = null, Func<Vector2, Vector2>? worldToScreen = null)
    {
        if (!Active || !TextService.Ready || uiScale <= 0f) return;

        _uiScale = uiScale;

        var scales = ScalesFor(area.Height);
        int scale = scales.Skin;

        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            null, rasterizer);

        if (_choices != null) { DrawChoices(sb, area, uiScale, scales); sb.End(); return; }

        if (_bubble == null) { sb.End(); return; }

        if (_bubble.NeedsRelayout(MaxContentWidth, MaxLinesPerPage))
        {
            _bubble.Relayout(MaxContentWidth, MaxLinesPerPage);
            if (_pageIndex >= _bubble.PageCount) _pageIndex = Math.Max(0, _bubble.PageCount - 1);
            _revealed = MathF.Min(_revealed, _bubble.CharCountOf(_pageIndex));
        }

        var skin = DialogueBubble.Skin();
        var speaker = DialogueBubble.ResolveSpeaker(scene, _bubble.SpeakerId);

        if (skin != null && speaker != null && worldToScreen != null)
        {
            var anchorWorld = Components.Talker.AnchorFor(speaker);
            var anchorScreen = worldToScreen(anchorWorld);
            _bubble.Draw(sb, skin, anchorScreen, scales, _pageIndex, (int)_revealed,
                showCaret: PageDone, blinkTimer: _blinkTimer);
        }
        else
        {
            DrawBottomCard(sb, area, uiScale, scales);
        }

        sb.End();
    }

    private static bool PageDone => _bubble != null && _revealed >= _bubble.CharCountOf(_pageIndex);

    private static void DrawBottomCard(SpriteBatch sb, Rectangle area, float uiScale,
        in BubbleScales scales)
    {
        int scale = scales.Skin;
        var size = _bubble!.BoxSize(scales);
        int x = area.X + (int)MathF.Round((area.Width - size.X) / 2f);
        int y = area.Y + (int)MathF.Round(area.Height - BoxMarginBottom * uiScale) - size.Y;
        var box = new Rectangle(x, y, size.X, size.Y);

        UIDraw.Panel(sb, box, FillColor, BorderColor, Math.Max(1, scale));

        int textLeft = box.X + DialogueBubble.PadLeft * scale;
        int textTop = box.Y + DialogueBubble.PadTop * scale;
        if (_speaker != null)
        {
            TextService.DrawScaled(sb, _speaker, new Vector2(textLeft, textTop),
                DialogueBubble.FontPx, scales.Text, SpeakerColor, FontSlot.Dialogue);
            textTop += (int)MathF.Ceiling(DialogueBubble.LineHeight + DialogueBubble.NameGap) * scales.Text;
        }

        _bubble.DrawText(sb, new Point(textLeft, textTop), scales, _pageIndex, (int)_revealed, TextColor);

        if (PageDone && _blinkTimer % 0.8f < 0.5f)
        {
            var px = UIDraw.Pixel(sb.GraphicsDevice);
            int tx = box.Right - (DialogueBubble.PadRight + 5) * scale;
            int ty = box.Bottom - (DialogueBubble.PadBottom - 1) * scale;
            sb.Draw(px, new Rectangle(tx, ty, 5 * scale, scale), SpeakerColor);
            sb.Draw(px, new Rectangle(tx + scale, ty + scale, 3 * scale, scale), SpeakerColor);
            sb.Draw(px, new Rectangle(tx + 2 * scale, ty + 2 * scale, scale, scale), SpeakerColor);
        }
    }

    private static void DrawChoices(SpriteBatch sb, Rectangle area, float uiScale,
        in BubbleScales scales)
    {
        int scale = scales.Skin;
        float lh = DialogueBubble.LineHeight;
        int boxW = (int)MathF.Round(MathF.Min(area.Width, MaxContentWidth * scales.Text
                                              + (DialogueBubble.PadLeft + DialogueBubble.PadRight) * scale));
        int boxH = (DialogueBubble.PadTop + DialogueBubble.PadBottom) * scale
                   + (int)MathF.Ceiling(_choices!.Length * lh) * scales.Text;
        int x = area.X + (area.Width - boxW) / 2;
        int y = area.Y + (int)MathF.Round(area.Height - BoxMarginBottom * uiScale) - boxH;
        var box = new Rectangle(x, y, boxW, boxH);

        UIDraw.Panel(sb, box, FillColor, BorderColor, Math.Max(1, scale));

        var px = UIDraw.Pixel(sb.GraphicsDevice);
        for (int i = 0; i < _choices.Length; i++)
        {
            bool on = i == _choiceIndex;
            int rowY = box.Y + DialogueBubble.PadTop * scale + (int)MathF.Round(i * lh) * scales.Text;
            if (on)
            {
                int cx = box.X + DialogueBubble.PadLeft * scale;
                int cy = rowY + 2 * scale;
                sb.Draw(px, new Rectangle(cx, cy, scale, 5 * scale), SpeakerColor);
                sb.Draw(px, new Rectangle(cx + scale, cy + scale, scale, 3 * scale), SpeakerColor);
                sb.Draw(px, new Rectangle(cx + 2 * scale, cy + 2 * scale, scale, scale), SpeakerColor);
            }
            TextService.DrawScaled(sb, _choices[i],
                new Vector2(box.X + DialogueBubble.PadLeft * scale + 7 * scales.Text, rowY),
                DialogueBubble.FontPx, scales.Text, on ? SpeakerColor : TextColor, FontSlot.Dialogue);
        }
    }
}
