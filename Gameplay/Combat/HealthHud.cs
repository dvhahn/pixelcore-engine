using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Core;

namespace PixelCore.Gameplay.Combat;

public static class HealthHud
{
    public const int Margin = 4;

    public const int HeartSize = 16;

    public const int Gap = 1;

    public static int FrameFor(int current, int heart)
        => Math.Clamp(current - heart * Health.QuartersPerHeart, 0, Health.QuartersPerHeart);

    public static int HeartCount(int max)
        => (Math.Max(0, max) + Health.QuartersPerHeart - 1) / Health.QuartersPerHeart;

    public static void DrawScreen(SpriteBatch sb, Scene scene, Rectangle area, float scale,
        RasterizerState? rasterizer = null)
    {
        var health = scene.FindPlayer()?.GetComponent<Health>();
        if (health == null || !(scale > 0f)) return;

        var texture = TextureLoader.Instance.Load(CombatAssets.HeartTexture);
        if (texture == null) return;

        int s = Math.Max(1, (int)MathF.Round(scale));
        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, rasterizer);
        for (int i = 0, n = HeartCount(health.Max); i < n; i++)
        {
            var source = new Rectangle(FrameFor(health.Current, i) * HeartSize, 0, HeartSize, HeartSize);
            var target = new Rectangle(
                area.X + (Margin + i * (HeartSize + Gap)) * s,
                area.Y + Margin * s,
                HeartSize * s, HeartSize * s);
            sb.Draw(texture, target, source, Color.White);
        }
        sb.End();
    }
}
