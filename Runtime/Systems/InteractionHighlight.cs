using Microsoft.Xna.Framework;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Systems;

public static class InteractionHighlight
{
    public static InteractableHighlight Mode { get; set; } = InteractableHighlight.None;

    public static Color OutlineColor { get; set; } = new(16, 14, 20);

    public static Color TintColor { get; set; } = new(255, 244, 206);

    private static SpriteRenderer? _painted;

    public static void Update(Scene? scene)
        => Apply(scene?.FindPlayer()?.GetComponent<Interactor>()?.Current?.Entity
                      .GetComponent<SpriteRenderer>());

    public static void Apply(SpriteRenderer? sr)
    {
        if (ReferenceEquals(sr, _painted) && Mode != InteractableHighlight.None) return;

        Unpaint();
        if (sr == null || Mode == InteractableHighlight.None) return;

        switch (Mode)
        {
            case InteractableHighlight.Outline: sr.OutlineOverride = OutlineColor; break;
            case InteractableHighlight.Tint:    sr.ColorOverride   = TintColor;    break;
        }
        _painted = sr;
    }

    public static void Clear()
    {
        Unpaint();
        _painted = null;
    }

    private static void Unpaint()
    {
        if (_painted == null) return;
        _painted.OutlineOverride = null;
        if (_painted.ColorOverride == TintColor) _painted.ColorOverride = null;
        _painted = null;
    }
}
