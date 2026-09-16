using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Components;

[ComponentCategory(ComponentCategories.Render)]
public class SortingGroup : Component
{
    public int RenderLayer { get; set; } = RenderLayers.Entities;

    public float SortOffset { get; set; } = 0f;

    public float SortY
    {
        get
        {
            if (RenderLayer != RenderLayers.Entities) return SortOffset;
            var t = Entity.GetComponent<Transform>();
            if (t == null) return SortOffset;
            return t.Position.Y + SortOffset;
        }
    }

    public static SortingGroup? Find(Entity? entity)
    {
        for (var e = entity; e != null; e = e.Parent)
        {
            var g = e.GetComponent<SortingGroup>();
            if (g is { Enabled: true }) return g;
        }
        return null;
    }
}
