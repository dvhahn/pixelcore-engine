using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Text;

namespace PixelCore.Runtime.UI;

public static class MonologueScreen
{
    public static bool Active { get; private set; }

    public static float Backdrop { get; set; } = 1f;

    public static string Text { get; private set; } = "";

    public static float TextAlpha { get; set; } = 1f;

    public static bool AcceptInput { get; set; } = true;

    public static Color BackColor { get; set; } = new(0, 0, 0);

    public static Color TextColor { get; set; } = new(214, 211, 222);

    public static float CenterY { get; set; } = 0.46f;

    private static bool _advance;
    private static bool _prevConfirm;
    private static bool _prevMouse;
    private static bool _warnedFont;

    public static void Open()
    {
        Active = true;
        Backdrop = 1f;
        Text = "";
        TextAlpha = 1f;
        AcceptInput = true;
        ClearPending();
    }

    public static void Close()
    {
        Active = false;
        Text = "";
        Backdrop = 1f;
        TextAlpha = 1f;
        AcceptInput = true;
        ClearPending();
    }

    public static void SetLine(string text)
    {
        Text = text ?? "";
        TextAlpha = 1f;
        ClearPending();
    }

    public static void ClearLine() => SetLine("");

    public static void ResetAll() => Close();

    public static bool TakeAdvance()
    {
        if (!_advance) return false;
        _advance = false;
        return true;
    }

    public static void ClearPending()
    {
        _advance = false;
        _prevConfirm = InputMap.IsDown(GameAction.Confirm);
        _prevMouse = MouseLeftDown();
    }

    public static void Update(bool inputLive, bool allowMouse)
    {
        if (!Active) return;

        bool confirm = inputLive && InputMap.IsDown(GameAction.Confirm);
        bool mouse = inputLive && allowMouse && MouseLeftDown();

        bool pressed = (confirm && !_prevConfirm) || (mouse && !_prevMouse);
        _prevConfirm = confirm;
        _prevMouse = mouse;

        if (pressed && AcceptInput) _advance = true;
    }

    private static bool MouseLeftDown()
    {
        try
        {
            return Microsoft.Xna.Framework.Input.Mouse.GetState().LeftButton
                   == Microsoft.Xna.Framework.Input.ButtonState.Pressed;
        }
        catch
        {
            return false;
        }
    }

    public static void Draw(SpriteBatch sb, Rectangle area, float uiScale)
    {
        if (!Active || uiScale <= 0f) return;
        if (Backdrop <= 0f && (TextAlpha <= 0f || Text.Length == 0)) return;

        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);

        if (Backdrop > 0f)
            sb.Draw(UIDraw.Pixel(sb.GraphicsDevice), area, BackColor * MathHelper.Clamp(Backdrop, 0f, 1f));

        float alpha = MathHelper.Clamp(TextAlpha, 0f, 1f);
        if (Text.Length > 0 && alpha > 0f)
        {
            var slot = PickSlot();
            if (slot != null)
            {
                int scale = BubbleScales.For(uiScale).Text;
                var size = TextService.Measure(Text, DialogueBubble.FontPx, slot.Value);
                var pos = new Vector2(
                    MathF.Round(area.X + area.Width / 2f - size.X * scale / 2f),
                    MathF.Round(area.Y + area.Height * CenterY - size.Y * scale / 2f));
                TextService.DrawScaled(sb, Text, pos, DialogueBubble.FontPx, scale,
                                       TextColor * alpha, slot.Value);
            }
        }

        sb.End();
    }

    private static FontSlot? PickSlot()
    {
        if (TextService.HasSlot(FontSlot.Dialogue)) return FontSlot.Dialogue;
        if (TextService.Ready)
        {
            if (!_warnedFont)
            {
                _warnedFont = true;
                Console.WriteLine("[Monologue] ⚠ no pixel font (Dialogue slot); drawing with the UI font");
            }
            return FontSlot.Ui;
        }
        if (!_warnedFont)
        {
            _warnedFont = true;
            Console.Error.WriteLine("[Monologue] ✘ no font at all - only the black backdrop will show");
        }
        return null;
    }
}
