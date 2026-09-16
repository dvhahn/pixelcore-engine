using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Text;
using PixelCore.Runtime.UI;

namespace PixelCore.Runtime.Systems;

public static class InteractionPrompt
{
    public static bool Enabled = true;

    public const float BakePx = TextService.PixelFontBakePx;

    public const float ScaleDivisor = 2f;

    private static FontSlot Slot =>
        TextService.HasSlot(FontSlot.Dialogue) ? FontSlot.Dialogue : FontSlot.Ui;

    private static readonly bool ForceNearest =
        Environment.GetEnvironmentVariable("PIXELCORE_PROMPTTEST") == "1";

    public static void DrawScreen(SpriteBatch sb, Scene scene,
        Func<Vector2, Vector2> worldToScreen, float uiScale, RasterizerState? rasterizer = null)
    {
        if (!Enabled && !ForceNearest) return;
        if (!TextService.Ready || uiScale <= 0f) return;

        var player = scene.FindPlayer();
        var pt = player?.GetComponent<Transform>();
        var target = player?.GetComponent<Interactor>()?.Current;

        if (target == null && ForceNearest && pt != null)
            target = FindNearest(scene, player!, pt.Position);
        if (target == null) return;

        var tt = target.Entity.GetComponent<Transform>();
        if (tt == null) return;

        int scale = Math.Max(1, (int)MathF.Round(
            BubbleScales.For(uiScale).World / ScaleDivisor, MidpointRounding.AwayFromZero));
        string text = LabelFor(target);
        var size = TextService.Measure(text, BakePx, Slot) * scale;
        var anchor = worldToScreen(new Vector2(
            tt.Position.X,
            tt.Position.Y - VisualHalfHeight(target.Entity) - 3f));
        var pos = new Vector2(MathF.Round(anchor.X - size.X / 2f), MathF.Round(anchor.Y - size.Y));

        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            null, rasterizer);

        float o = scale;
        var shadow = new Color(20, 18, 24, 235);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                TextService.DrawScaled(sb, text, pos + new Vector2(dx * o, dy * o), BakePx, scale, shadow, Slot);
            }
        TextService.DrawScaled(sb, text, pos, BakePx, scale, new Color(255, 236, 180), Slot);

        sb.End();
    }

    public static string LabelFor(Interactable target)
        => $"[{InputMap.KeyLabel(GameAction.Interact)}] {Loc.T(target.Prompt)}";

    private static Interactable? FindNearest(Scene scene, Entity player, Vector2 from)
    {
        Interactable? best = null;
        float bestDist = float.MaxValue;
        foreach (var e in scene.Entities)
        {
            if (e == player || !e.ActiveInHierarchy) continue;
            var it = e.GetComponent<Interactable>();
            var t = e.GetComponent<Transform>();
            if (it == null || t == null) continue;
            float d = (t.Position - from).LengthSquared();
            if (d < bestDist) { best = it; bestDist = d; }
        }
        return best;
    }

    private static float VisualHalfHeight(Entity e) => Talker.VisualHeight(e) / 2f;
}
