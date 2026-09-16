using System.Collections.Generic;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;
using PixelCore.Editor.Commands;

namespace PixelCore.Editor;

public class SceneDoc
{
    private static int _nextId = 1;

    public readonly int Id = _nextId++;

    public Scene Scene;

    public string? Path;

    public bool Dirty;

    public CommandHistory History = new();

    public readonly List<Entity> Selection = new();

    public Vector2 CameraPos;
    public float CameraZoom = 1f;

    public SceneDoc(Scene scene, string? path)
    {
        Scene = scene;
        Path = path;
    }
}
