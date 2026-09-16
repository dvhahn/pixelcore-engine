using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;

namespace PixelCore.Editor.Commands;

public class MoveEntityCommand : ICommand
{
    private readonly Scene? _scene;
    private readonly int _entityId;
    private readonly string _name;
    private readonly Vector2 _oldPosition;
    private readonly Vector2 _newPosition;

    public string Description => "Move Entity";

    public MoveEntityCommand(Entity entity, Vector2 oldPosition, Vector2 newPosition)
    {
        _scene = entity.Scene;
        _entityId = entity.Id;
        _name = entity.Name;
        _oldPosition = oldPosition;
        _newPosition = newPosition;
    }

    public void Execute() => Apply(_newPosition);
    public void Undo() => Apply(_oldPosition);

    private void Apply(Vector2 position)
    {
        var e = CommandTarget.Resolve(_scene, _entityId, $"Move '{_name}'");
        if (e?.GetComponent<Transform>() is { } transform)
            transform.Position = position;
    }
}

public class RotateEntityCommand : ICommand
{
    private readonly Scene? _scene;
    private readonly int _entityId;
    private readonly string _name;
    private readonly float _oldRotation;
    private readonly float _newRotation;

    public string Description => "Rotate Entity";

    public RotateEntityCommand(Entity entity, float oldRotation, float newRotation)
    {
        _scene = entity.Scene;
        _entityId = entity.Id;
        _name = entity.Name;
        _oldRotation = oldRotation;
        _newRotation = newRotation;
    }

    public void Execute() => Apply(_newRotation);
    public void Undo() => Apply(_oldRotation);

    private void Apply(float rotation)
    {
        var e = CommandTarget.Resolve(_scene, _entityId, $"Rotate '{_name}'");
        if (e?.GetComponent<Transform>() is { } transform)
            transform.Rotation = rotation;
    }
}
