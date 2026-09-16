using System;
using System.IO;
using FontStashSharp;
using FontStashSharp.Interfaces;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PixelCore.Runtime.Text;

public enum FontSlot
{
    Ui = 0,

    Dialogue = 1,
}

public static class TextService
{
    private const int SlotCount = 2;

    private static readonly FontSystem?[] _slots = new FontSystem?[SlotCount];
    private static readonly bool[] _slotWarned = new bool[SlotCount];
    private static FnaFontRenderer? _renderer;

    public static bool Ready => _slots[(int)FontSlot.Ui] != null;

    public static bool HasSlot(FontSlot slot) => _slots[(int)slot] != null;

    public static void Initialize(GraphicsDevice gd, string ttfPath)
    {
        _renderer = new FnaFontRenderer(gd);
        Load(FontSlot.Ui, ttfPath);
    }

    public const float PixelFontBakePx = 11f;

    public static void Load(FontSlot slot, string ttfPath)
    {
        if (!File.Exists(ttfPath))
        {
            Console.WriteLine($"[Text] ⚠ font not found ({slot}): {ttfPath}");
            return;
        }
        var fs = new FontSystem();
        fs.AddFont(File.ReadAllBytes(ttfPath));
        _slots[(int)slot] = fs;
        _slotWarned[(int)slot] = false;
        Console.WriteLine($"[Text] font loaded ({slot}): {Path.GetFileName(ttfPath)}");
    }

    private static FontSystem? Pick(FontSlot slot)
    {
        var fs = _slots[(int)slot];
        if (fs != null) return fs;

        if (slot != FontSlot.Ui && !_slotWarned[(int)slot])
        {
            _slotWarned[(int)slot] = true;
            Console.WriteLine($"[Text] ⚠ no font in slot {slot} - drawing with the default");
        }
        return _slots[(int)FontSlot.Ui];
    }

    public static void Draw(SpriteBatch sb, string text, Vector2 pos, float size, Color color,
        FontSlot slot = FontSlot.Ui)
    {
        var fs = Pick(slot);
        if (fs == null || _renderer == null) return;
        _renderer.Batch = sb;
        fs.GetFont(size).DrawText(_renderer, text,
            new System.Numerics.Vector2(pos.X, pos.Y),
            new FSColor(color.R, color.G, color.B, color.A));
    }

    public static void DrawScaled(SpriteBatch sb, string text, Vector2 pos, float basePx,
        float scale, Color color, FontSlot slot = FontSlot.Ui)
    {
        var fs = Pick(slot);
        if (fs == null || _renderer == null) return;
        _renderer.Batch = sb;
        fs.GetFont(basePx).DrawText(_renderer, text,
            new System.Numerics.Vector2(pos.X, pos.Y),
            new FSColor(color.R, color.G, color.B, color.A),
            scale: new System.Numerics.Vector2(scale, scale));
    }

    public static Vector2 Measure(string text, float size, FontSlot slot = FontSlot.Ui)
    {
        var fs = Pick(slot);
        if (fs == null) return Vector2.Zero;
        var m = fs.GetFont(size).MeasureString(text);
        return new Vector2(m.X, m.Y);
    }
}

internal sealed class FnaTextureManager : ITexture2DManager
{
    private readonly GraphicsDevice _gd;
    public FnaTextureManager(GraphicsDevice gd) => _gd = gd;

    public object CreateTexture(int width, int height) => new Texture2D(_gd, width, height);

    public System.Drawing.Point GetTextureSize(object texture)
    {
        var t = (Texture2D)texture;
        return new System.Drawing.Point(t.Width, t.Height);
    }

    public void SetTextureData(object texture, System.Drawing.Rectangle bounds, byte[] data)
    {
        ((Texture2D)texture).SetData(0,
            new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            data, 0, bounds.Width * bounds.Height * 4);
    }
}

internal sealed class FnaFontRenderer : IFontStashRenderer
{
    public SpriteBatch? Batch;
    public ITexture2DManager TextureManager { get; }

    public FnaFontRenderer(GraphicsDevice gd) => TextureManager = new FnaTextureManager(gd);

    public void Draw(object texture, System.Numerics.Vector2 pos, System.Drawing.Rectangle? src,
        FSColor color, float rotation, System.Numerics.Vector2 scale, float depth)
    {
        if (Batch == null) return;
        Rectangle? xnaSrc = src.HasValue
            ? new Rectangle(src.Value.X, src.Value.Y, src.Value.Width, src.Value.Height)
            : null;
        Batch.Draw((Texture2D)texture,
            new Vector2(pos.X, pos.Y), xnaSrc,
            new Color(color.R, color.G, color.B, color.A),
            rotation, Vector2.Zero, new Vector2(scale.X, scale.Y), SpriteEffects.None, depth);
    }
}
