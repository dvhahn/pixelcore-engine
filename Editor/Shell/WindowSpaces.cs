using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PixelCore.Editor;

internal readonly struct WindowSpaces
{
    public readonly int ClientW, ClientH;
    public readonly int DrawW, DrawH;
    public readonly int BackW, BackH;

    private WindowSpaces(int cw, int ch, int dw, int dh, int bw, int bh)
    {
        ClientW = cw; ClientH = ch;
        DrawW = dw; DrawH = dh;
        BackW = bw; BackH = bh;
    }

    public static WindowSpaces Capture(GameWindow window, GraphicsDevice device)
    {
        var cb = window.ClientBounds;
        SDL3.SDL.SDL_GetWindowSizeInPixels(window.Handle, out int dw, out int dh);
        var pp = device.PresentationParameters;
        if (dw <= 0 || dh <= 0) { dw = pp.BackBufferWidth; dh = pp.BackBufferHeight; }
        return new WindowSpaces(cb.Width, cb.Height, dw, dh, pp.BackBufferWidth, pp.BackBufferHeight);
    }

    public float Scale => ClientW > 0 ? (float)DrawW / ClientW : 1f;

    public System.Numerics.Vector2 DisplaySize
    {
        get
        {
            float s = Scale;
            return s > 0
                ? new System.Numerics.Vector2(DrawW / s, DrawH / s)
                : new System.Numerics.Vector2(ClientW, ClientH);
        }
    }

    public System.Numerics.Vector2 MouseToPoint(float x, float y) => new(
        BackW > 0 ? x * ((float)ClientW / BackW) : x,
        BackH > 0 ? y * ((float)ClientH / BackH) : y);
}
