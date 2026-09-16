using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;

namespace PixelCore.Editor;

public static class EntityKinds
{
    public static string Icon(Component c) => c switch
    {
        Transform => Icons.Crosshair,
        Animator => Icons.Film,
        SpriteRenderer => Icons.Image,
        TilemapRenderer => Icons.Map,
        Light2D => Icons.Lightbulb,
        SceneInstance => Icons.Cubes,
        SortingGroup => Icons.Sitemap,
        Collider2D => Icons.Stop,
        Rigidbody2D => Icons.Circle,
        _ => Icons.Cube,
    };

    public static string Label(Entity e)
    {
        if (e.GetComponent<SceneInstance>() is { } si)
        {
            var baseName = System.IO.Path.GetFileNameWithoutExtension(si.ScenePath ?? "");
            if (baseName.Length == 0) return "Scene";
            return baseName == e.Name ? "" : baseName;
        }
        if (e.HasComponent<Light2D>()) return "Light";
        if (e.HasComponent<TilemapRenderer>()) return "Tile";
        if (e.HasComponent<Animator>()) return "Anim";
        if (e.HasComponent<SpriteRenderer>()) return "Sprite";
        if (e.HasComponent<BoxCollider2D>()) return "Col";
        return "";
    }
}
