using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Text;

namespace PixelCore.Runtime.UI;

public readonly struct MenuItem
{
    public readonly string Label;
    public readonly Action? OnChosen;
    public readonly Func<string>? Value;
    public readonly Action? OnLeft;
    public readonly Action? OnRight;
    public readonly bool Enabled;

    public MenuItem(string label, Action? onChosen, bool enabled = true)
    { Label = label; OnChosen = onChosen; Value = null; OnLeft = OnRight = null; Enabled = enabled; }

    public MenuItem(string label, Func<string> value, Action onLeft, Action onRight)
    { Label = label; OnChosen = null; Value = value; OnLeft = onLeft; OnRight = onRight; Enabled = true; }

    public bool IsValue => Value != null;
}

public static class PauseScreen
{
    public static bool Active { get; private set; }

    public static int TitleTop { get; set; } = 40;

    public static int MenuCenterY { get; set; } = 112;
    public static int MenuLineHeight { get; set; } = 15;
    public static int HintBottom { get; set; } = 10;
    public static int CaretGap { get; set; } = 3;
    public static int PanelWidth { get; set; } = 132;

    public static Color DimColor { get; set; } = new(6, 5, 9, 205);
    public static Color TitleColor { get; set; } = new(238, 236, 244);
    public static Color ItemColor { get; set; } = new(198, 194, 208);
    public static Color SelectedColor { get; set; } = new(240, 198, 120);
    public static Color DisabledColor { get; set; } = new(88, 84, 98);
    public static Color ValueColor { get; set; } = new(150, 146, 164);
    public static Color HintColor { get; set; } = new(104, 100, 116);

    private const string Caret = "▶";

    private static readonly List<Page> _stack = new();
    private static bool _warnedFont;

    private sealed class Page
    {
        public string Title = "";
        public readonly List<MenuItem> Items = new();
        public int Index;
    }

    public static int Depth => _stack.Count;
    public static int SelectedIndex => _stack.Count > 0 ? _stack[^1].Index : 0;
    public static int ItemCount => _stack.Count > 0 ? _stack[^1].Items.Count : 0;

    public static void Open(string title, IEnumerable<MenuItem> items)
    {
        if (Active) return;
        _stack.Clear();
        Push(title, items);
        Active = true;
    }

    public static void Push(string title, IEnumerable<MenuItem> items)
    {
        var p = new Page { Title = title ?? "" };
        p.Items.AddRange(items);
        p.Index = FirstEnabled(p);
        _stack.Add(p);
        Active = true;
    }

    public static bool Back()
    {
        if (_stack.Count == 0) { Active = false; return true; }
        _stack.RemoveAt(_stack.Count - 1);
        if (_stack.Count > 0) return false;
        Active = false;
        return true;
    }

    public static void Close()
    {
        _stack.Clear();
        Active = false;
    }

    public static void ResetAll()
    {
        Close();
        _warnedFont = false;
    }

    private static int FirstEnabled(Page p)
    {
        for (int i = 0; i < p.Items.Count; i++) if (p.Items[i].Enabled) return i;
        return 0;
    }

    public static void Update(Action? onClosed = null)
    {
        if (!Active || _stack.Count == 0) return;
        var page = _stack[^1];
        if (page.Items.Count == 0) return;

        if (InputMap.IsPressed(GameAction.Pause))
        {
            if (Back()) onClosed?.Invoke();
            return;
        }

        if (InputMap.IsPressed(GameAction.MoveUp)) Move(page, -1);
        if (InputMap.IsPressed(GameAction.MoveDown)) Move(page, +1);

        var item = page.Items[Math.Clamp(page.Index, 0, page.Items.Count - 1)];
        if (!item.Enabled) return;

        if (item.IsValue)
        {
            if (InputMap.IsPressed(GameAction.MoveLeft)) item.OnLeft?.Invoke();
            if (InputMap.IsPressed(GameAction.MoveRight)) item.OnRight?.Invoke();
            return;
        }

        if (InputMap.IsPressed(GameAction.Confirm))
        {
            var action = item.OnChosen;
            action?.Invoke();
        }
    }

    private static void Move(Page p, int step)
    {
        int n = p.Items.Count;
        for (int i = 1; i <= n; i++)
        {
            int c = ((p.Index + step * i) % n + n) % n;
            if (!p.Items[c].Enabled) continue;
            p.Index = c;
            return;
        }
    }

    public static void Draw(SpriteBatch sb, Rectangle area, float uiScale)
    {
        if (!Active || _stack.Count == 0 || uiScale <= 0f) return;
        var page = _stack[^1];

        var scales = BubbleScales.For(uiScale);
        int text = scales.Text;
        int title = Math.Max(2, text * 2);

        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);

        sb.Draw(UIDraw.Pixel(sb.GraphicsDevice), area, DimColor);

        var slot = PickSlot();
        if (slot == null) { sb.End(); return; }

        float cx = area.X + area.Width / 2f;
        float half = PanelWidth * uiScale / 2f;
        float top = MenuCenterY - (page.Items.Count - 1) * MenuLineHeight / 2f;

        DrawCentered(sb, page.Title, cx, area.Y + TitleTop * uiScale, title, TitleColor, slot.Value);

        for (int i = 0; i < page.Items.Count; i++)
        {
            var item = page.Items[i];
            bool on = i == page.Index;
            var color = !item.Enabled ? DisabledColor : on ? SelectedColor : ItemColor;
            float y = area.Y + (top + i * MenuLineHeight) * uiScale;

            float labelLeft;
            if (item.IsValue)
            {
                labelLeft = cx - half;
                TextService.DrawScaled(sb, item.Label, new Vector2(MathF.Round(labelLeft), MathF.Round(y)),
                    DialogueBubble.FontPx, text, color, slot.Value);

                string v = item.Value!();
                float vw = TextService.Measure(v, DialogueBubble.FontPx, slot.Value).X * text;
                TextService.DrawScaled(sb, v, new Vector2(MathF.Round(cx + half - vw), MathF.Round(y)),
                    DialogueBubble.FontPx, text, on ? color : ValueColor, slot.Value);
            }
            else
            {
                float w = TextService.Measure(item.Label, DialogueBubble.FontPx, slot.Value).X * text;
                labelLeft = cx - w / 2f;
                TextService.DrawScaled(sb, item.Label, new Vector2(MathF.Round(labelLeft), MathF.Round(y)),
                    DialogueBubble.FontPx, text, color, slot.Value);
            }

            if (!on) continue;
            float caretW = TextService.Measure(Caret, DialogueBubble.FontPx, slot.Value).X * text;
            TextService.DrawScaled(sb, Caret,
                new Vector2(MathF.Round(labelLeft - caretW - CaretGap * uiScale), MathF.Round(y)),
                DialogueBubble.FontPx, text, color, slot.Value);
        }

        DrawCentered(sb, Hint(page), cx,
            area.Bottom - (HintBottom + DialogueBubble.LineHeight) * uiScale,
            Math.Max(1, text - 1), HintColor, slot.Value);

        sb.End();
    }

    private static string Hint(Page page)
    {
        bool anyValue = page.Items.Exists(i => i.IsValue);
        string move = $"{InputMap.KeyLabel(GameAction.MoveUp)}/{InputMap.KeyLabel(GameAction.MoveDown)} select";
        string act = anyValue
            ? $"{InputMap.KeyLabel(GameAction.MoveLeft)}/{InputMap.KeyLabel(GameAction.MoveRight)} adjust"
            : $"[{InputMap.KeyLabel(GameAction.Confirm)}] confirm";
        return $"{move} · {act} · [{InputMap.KeyLabel(GameAction.Pause)}] back";
    }

    private static float DrawCentered(SpriteBatch sb, string s, float cx, float y,
                                      int scale, Color color, FontSlot slot)
    {
        if (string.IsNullOrEmpty(s)) return 0f;
        float w = TextService.Measure(s, DialogueBubble.FontPx, slot).X * scale;
        TextService.DrawScaled(sb, s, new Vector2(MathF.Round(cx - w / 2f), MathF.Round(y)),
            DialogueBubble.FontPx, scale, color, slot);
        return w;
    }

    private static FontSlot? PickSlot()
    {
        if (TextService.HasSlot(FontSlot.Dialogue)) return FontSlot.Dialogue;
        if (TextService.Ready)
        {
            if (!_warnedFont)
            {
                _warnedFont = true;
                Console.WriteLine("[Pause] ⚠ no pixel font (Dialogue slot); drawing with the UI font");
            }
            return FontSlot.Ui;
        }
        if (!_warnedFont)
        {
            _warnedFont = true;
            Console.Error.WriteLine("[Pause] ✘ no font at all - the menu will show only the darkness");
        }
        return null;
    }
}
