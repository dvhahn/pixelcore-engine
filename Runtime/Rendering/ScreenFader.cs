using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PixelCore.Runtime.Rendering;

public static class ScreenFader
{
    public static float Alpha { get; private set; }
    private static float _target;
    private static float _speed = 1000f;

    public static void FadeTo(float target, float duration)
    {
        _target = MathHelper.Clamp(target, 0f, 1f);
        _speed = duration > 0f ? 1f / duration : 1000f;
    }

    public static void Set(float alpha) => Alpha = _target = MathHelper.Clamp(alpha, 0f, 1f);

    public static void Update(float dt)
    {
        if (Alpha < _target) Alpha = MathF.Min(_target, Alpha + _speed * dt);
        else if (Alpha > _target) Alpha = MathF.Max(_target, Alpha - _speed * dt);
    }

    public static void Draw(SpriteBatch sb, Texture2D pixel, int vw, int vh)
    {
        if (Alpha <= 0.001f) return;
        sb.Begin();
        sb.Draw(pixel, new Rectangle(0, 0, vw, vh), Color.Black * Alpha);
        sb.End();
    }
}
