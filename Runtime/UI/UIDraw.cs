using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PixelCore.Runtime.UI;

public static class UIDraw
{
    private static Texture2D? _pixel;

    public static Texture2D Pixel(GraphicsDevice gd)
    {
        if (_pixel == null || _pixel.IsDisposed)
        {
            _pixel = new Texture2D(gd, 1, 1);
            _pixel.SetData(new[] { Color.White });
        }
        return _pixel;
    }

    public static void Panel(SpriteBatch sb, Rectangle rect, Color fill, Color border, int borderPx)
    {
        var px = Pixel(sb.GraphicsDevice);
        sb.Draw(px, rect, fill);
        if (borderPx <= 0) return;
        sb.Draw(px, new Rectangle(rect.X, rect.Y, rect.Width, borderPx), border);
        sb.Draw(px, new Rectangle(rect.X, rect.Bottom - borderPx, rect.Width, borderPx), border);
        sb.Draw(px, new Rectangle(rect.X, rect.Y, borderPx, rect.Height), border);
        sb.Draw(px, new Rectangle(rect.Right - borderPx, rect.Y, borderPx, rect.Height), border);
    }

    public static void NineSlice(SpriteBatch sb, Texture2D tex, Rectangle src, Rectangle dest,
        int left, int top, int right, int bottom, int scale, Color color)
    {
        int ls = left * scale, ts = top * scale, rs = right * scale, bs = bottom * scale;
        if (dest.Width < ls + rs || dest.Height < ts + bs ||
            src.Width < left + right || src.Height < top + bottom)
        {
            sb.Draw(tex, dest, src, color);
            return;
        }

        int sx = src.X, sy = src.Y, sw = src.Width, sh = src.Height;
        int dx = dest.X, dy = dest.Y, dw = dest.Width, dh = dest.Height;
        int smw = sw - left - right, smh = sh - top - bottom;
        int dmw = dw - ls - rs, dmh = dh - ts - bs;

        sb.Draw(tex, new Rectangle(dx, dy, ls, ts), new Rectangle(sx, sy, left, top), color);
        sb.Draw(tex, new Rectangle(dx + dw - rs, dy, rs, ts), new Rectangle(sx + sw - right, sy, right, top), color);
        sb.Draw(tex, new Rectangle(dx, dy + dh - bs, ls, bs), new Rectangle(sx, sy + sh - bottom, left, bottom), color);
        sb.Draw(tex, new Rectangle(dx + dw - rs, dy + dh - bs, rs, bs), new Rectangle(sx + sw - right, sy + sh - bottom, right, bottom), color);
        sb.Draw(tex, new Rectangle(dx + ls, dy, dmw, ts), new Rectangle(sx + left, sy, smw, top), color);
        sb.Draw(tex, new Rectangle(dx + ls, dy + dh - bs, dmw, bs), new Rectangle(sx + left, sy + sh - bottom, smw, bottom), color);
        sb.Draw(tex, new Rectangle(dx, dy + ts, ls, dmh), new Rectangle(sx, sy + top, left, smh), color);
        sb.Draw(tex, new Rectangle(dx + dw - rs, dy + ts, rs, dmh), new Rectangle(sx + sw - right, sy + top, right, smh), color);
        sb.Draw(tex, new Rectangle(dx + ls, dy + ts, dmw, dmh), new Rectangle(sx + left, sy + top, smw, smh), color);
    }

    public static void NineSlice(SpriteBatch sb, Texture2D tex, Rectangle src, Rectangle dest,
        int corner, int scale, Color color)
    {
        int c = corner;
        int cs = c * scale;
        if (dest.Width < cs * 2 || dest.Height < cs * 2 || src.Width < c * 2 || src.Height < c * 2)
        {
            sb.Draw(tex, dest, src, color);
            return;
        }

        int sx = src.X, sy = src.Y, sw = src.Width, sh = src.Height;
        int dx = dest.X, dy = dest.Y, dw = dest.Width, dh = dest.Height;
        int smw = sw - c * 2, smh = sh - c * 2;
        int dmw = dw - cs * 2, dmh = dh - cs * 2;

        sb.Draw(tex, new Rectangle(dx, dy, cs, cs), new Rectangle(sx, sy, c, c), color);
        sb.Draw(tex, new Rectangle(dx + dw - cs, dy, cs, cs), new Rectangle(sx + sw - c, sy, c, c), color);
        sb.Draw(tex, new Rectangle(dx, dy + dh - cs, cs, cs), new Rectangle(sx, sy + sh - c, c, c), color);
        sb.Draw(tex, new Rectangle(dx + dw - cs, dy + dh - cs, cs, cs), new Rectangle(sx + sw - c, sy + sh - c, c, c), color);
        sb.Draw(tex, new Rectangle(dx + cs, dy, dmw, cs), new Rectangle(sx + c, sy, smw, c), color);
        sb.Draw(tex, new Rectangle(dx + cs, dy + dh - cs, dmw, cs), new Rectangle(sx + c, sy + sh - c, smw, c), color);
        sb.Draw(tex, new Rectangle(dx, dy + cs, cs, dmh), new Rectangle(sx, sy + c, c, smh), color);
        sb.Draw(tex, new Rectangle(dx + dw - cs, dy + cs, cs, dmh), new Rectangle(sx + sw - c, sy + c, c, smh), color);
        sb.Draw(tex, new Rectangle(dx + cs, dy + cs, dmw, dmh), new Rectangle(sx + c, sy + c, smw, smh), color);
    }
}
