using System.IO;
using PixelCore.Runtime.Assets;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Editor.Commands;

public class InstantiateSceneCommand : ICommand
{
    private readonly Scene _scene;
    private readonly string _sceneId;
    private readonly Entity? _parent;
    private readonly Microsoft.Xna.Framework.Vector2? _position;
    private Entity? _createdEntity;

    public string Description => "Instantiate Scene";
    public Entity? CreatedEntity => _createdEntity;

    public InstantiateSceneCommand(Scene scene, string sceneId, Entity? parent = null,
        Microsoft.Xna.Framework.Vector2? position = null)
    {
        _scene = scene;
        _sceneId = sceneId;
        _parent = parent;
        _position = position;
    }

    public void Execute()
    {
        var path = AssetRegistry.Instance.GetPath(_sceneId);
        if (string.IsNullOrEmpty(path))
            return;

        var name = Path.GetFileNameWithoutExtension(path);

        _createdEntity = _scene.CreateEntity(name);

        if (_parent != null)
            _createdEntity.SetParent(_parent);

        if (_position.HasValue)
            _createdEntity.GetComponent<Transform>()!.Position = _position.Value;

        var sceneInst = _createdEntity.AddComponent<SceneInstance>();
        sceneInst.SceneId = _sceneId;
        sceneInst.Load();
    }

    public void Undo()
    {
        if (_createdEntity != null)
        {
            _scene.DestroyEntity(_createdEntity);
            _createdEntity = null;
        }
    }
}

public class SaveSceneCommand : ICommand
{
    private readonly Scene _scene;
    private readonly string _filePath;

    public string Description => "Save Scene";

    public SaveSceneCommand(Scene scene, string filePath)
    {
        _scene = scene;
        _filePath = filePath;
    }

    public void Execute()
    {
        var data = SceneSerializer.ToData(_scene);
        SceneSerializer.SaveToFile(data, _filePath);

        AssetRegistry.Instance.GetOrCreateId(_filePath);
        if (!AssetRegistry.Instance.Save())
            System.Console.Error.WriteLine($"[Scene] could not record the id for '{_filePath}' in the registry - references pointing at this scene break on the next boot");
    }

    public void Undo()
    {
    }
}
