using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Physics;

namespace PixelCore.Runtime.Components;

[ComponentCategory(ComponentCategories.Sound)]
public class SurfaceArea : Component
{
    public string SurfaceId { get; set; } = "";

    public int Priority { get; set; }

    public bool Contains(Vector2 point)
    {
        foreach (var collider in Entity.GetComponents<Collider2D>())
            if (ShapeTests.ContainsPoint(collider, point))
                return true;
        return false;
    }

    public static SurfaceArea? FindAt(Scene? scene, Vector2 point)
    {
        if (scene == null) return null;

        SurfaceArea? best = null;
        foreach (var entity in scene.FindEntitiesWithComponent<SurfaceArea>())
        {
            if (!entity.ActiveInHierarchy) continue;
            var area = entity.GetComponent<SurfaceArea>();
            if (area == null || !area.Enabled) continue;
            if (string.IsNullOrEmpty(area.SurfaceId)) continue;

            if (best != null && area.Priority <= best.Priority) continue;
            if (!area.Contains(point)) continue;

            best = area;
        }
        return best;
    }
}
