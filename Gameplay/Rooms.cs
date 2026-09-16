namespace PixelCore.Gameplay;

public readonly struct RoomRef
{
    public string SceneId { get; }
    public string Name { get; }

    public RoomRef(string sceneId, string name)
    {
        SceneId = sceneId;
        Name = name;
    }
}

public static class Rooms
{
    public static readonly RoomRef Village = new("5a1c7e20", "Village");

    public static readonly RoomRef House = new("b83f0d4e", "House");
}
