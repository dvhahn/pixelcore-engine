using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Text;

namespace PixelCore.Runtime.UI;

public readonly struct TitleItem
{
    public string Label { get; }

    public Action? OnChosen { get; }

    public bool Enabled { get; }

    public TitleItem(string label, Action? onChosen, bool enabled = true)
    {
        Label = label;
        OnChosen = onChosen;
        Enabled = enabled;
    }
}

public static class TitleScreen
{
    public static bool Active { get; private set; }

    public static int TitleTop { get; set; } = 44;

    public static int MenuTop { get; set; } = 104;

    public static int MenuLineHeight { get; set; } = 16;

    public static int HintBottom { get; set; } = 10;

    public static int CaretGap { get; set; } = 3;

    private const string Caret = "▶";

    public static Color BackColor { get; set; } = new(10, 9, 13);
    public static Color TitleColor { get; set; } = new(238, 236, 244);
    public static Color SubtitleColor { get; set; } = new(122, 118, 134);
    public static Color ItemColor { get; set; } = new(198, 194, 208);
    public static Color SelectedColor { get; set; } = new(240, 198, 120);
    public static Color DisabledColor { get; set; } = new(88, 84, 98);
    public static Color HintColor { get; set; } = new(104, 100, 116);

    private static string _title = "";
    private static string _subtitle = "";
    private static readonly List<TitleItem> _items = new();
    private static int _index;

    private static bool _warnedFont;

    public static int SelectedIndex => _index;

    public static int ItemCount => _items.Count;

    public static void Open(string title, string subtitle, IEnumerable<TitleItem> items)
    {
        _title = title ?? "";
        _subtitle = subtitle ?? "";
        _items.Clear();
        _items.AddRange(items);
        _index = FirstEnabled();
        Active = true;
    }

    public static void Close()
    {
        Active = false;
        _items.Clear();
        _index = 0;
    }

    public static void ResetAll()
    {
        Close();
        _title = "";
        _subtitle = "";
        _warnedFont = false;
    }

    private static int FirstEnabled()
    {
        for (int i = 0; i < _items.Count; i++)
            if (_items[i].Enabled) return i;
        return 0;
    }

    public static void Update()
    {
        if (!Active || _items.Count == 0) return;

        if (InputMap.IsPressed(GameAction.MoveUp)) Move(-1);
        if (InputMap.IsPressed(GameAction.MoveDown)) Move(+1);

        if (InputMap.IsPressed(GameAction.Confirm))
        {
            var item = _items[Math.Clamp(_index, 0, _items.Count - 1)];
            if (!item.Enabled) return;

            var action = item.OnChosen;
            action?.Invoke();
        }
    }

    private static void Move(int step)
    {
        int n = _items.Count;
        for (int i = 1; i <= n; i++)
        {
            int candidate = ((_index + step * i) % n + n) % n;
            if (!_items[candidate].Enabled) continue;
            _index = candidate;
            return;
        }
    }

    public static void Draw(SpriteBatch sb, Rectangle area, float uiScale)
    {
        if (!Active || uiScale <= 0f) return;

        var scales = BubbleScales.For(uiScale);
        int text = scales.Text;
        int title = Math.Max(2, text * 2);

        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);

        sb.Draw(UIDraw.Pixel(sb.GraphicsDevice), area, BackColor);

        var slot = PickSlot();
        if (slot == null) { sb.End(); return; }

        float cx = area.X + area.Width / 2f;

        DrawCentered(sb, _title, cx, area.Y + TitleTop * uiScale, title, TitleColor, slot.Value);
        if (_subtitle.Length > 0)
            DrawCentered(sb, _subtitle, cx,
                area.Y + (TitleTop + DialogueBubble.LineHeight * 2f) * uiScale,
                text, SubtitleColor, slot.Value);

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            bool on = i == _index;
            var color = !item.Enabled ? DisabledColor : on ? SelectedColor : ItemColor;
            float y = area.Y + (MenuTop + i * MenuLineHeight) * uiScale;

            float w = DrawCentered(sb, item.Label, cx, y, text, color, slot.Value);
            if (!on) continue;

            float caretW = TextService.Measure(Caret, DialogueBubble.FontPx, slot.Value).X * text;
            TextService.DrawScaled(sb, Caret,
                new Vector2(MathF.Round(cx - w / 2f - caretW - CaretGap * uiScale), MathF.Round(y)),
                DialogueBubble.FontPx, text, color, slot.Value);
        }

        DrawCentered(sb, Hint(), cx,
            area.Bottom - (HintBottom + DialogueBubble.LineHeight) * uiScale,
            Math.Max(1, text - 1), HintColor, slot.Value);

        sb.End();
    }

    private static string Hint()
        => $"{InputMap.KeyLabel(GameAction.MoveUp)}/{InputMap.KeyLabel(GameAction.MoveDown)} select  -  "
         + $"[{InputMap.KeyLabel(GameAction.Confirm)}] confirm";

    private static FontSlot? PickSlot()
    {
        if (TextService.HasSlot(FontSlot.Dialogue)) return FontSlot.Dialogue;
        if (TextService.Ready)
        {
            if (!_warnedFont)
            {
                _warnedFont = true;
                Console.WriteLine("[Title] ⚠ no pixel font (Dialogue slot); drawing with the UI font");
            }
            return FontSlot.Ui;
        }
        if (!_warnedFont)
        {
            _warnedFont = true;
            Console.Error.WriteLine("[Title] ✘ no font at all - the title will show only its background");
        }
        return null;
    }

    private static float DrawCentered(SpriteBatch sb, string s, float centerX, float y,
        int scale, Color color, FontSlot slot)
    {
        if (string.IsNullOrEmpty(s)) return 0f;
        float w = TextService.Measure(s, DialogueBubble.FontPx, slot).X * scale;
        var pos = new Vector2(MathF.Round(centerX - w / 2f), MathF.Round(y));
        TextService.DrawScaled(sb, s, pos, DialogueBubble.FontPx, scale, color, slot);
        return w;
    }
}
