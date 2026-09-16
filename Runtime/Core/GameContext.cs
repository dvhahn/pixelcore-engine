using PixelCore.Runtime.Audio;
using PixelCore.Runtime.Systems;

namespace PixelCore.Runtime.Core;

public class GameContext
{
    public TimeControl Time { get; } = new();

    public AudioManager Audio => AudioManager.Instance;

    public Blackboard Blackboard { get; } = new();

    public Save.FlagExpiry FlagExpiry { get; } = new();

    public CoroutineRunner Coroutines { get; } = new();

    public TimeOfDay Clock { get; } = new();

    public Save.SaveSystem Save { get; } = new();

    public Scene Scene { get; set; } = null!;

    public Camera Camera { get; set; } = null!;

    public PhysicsSystem Physics { get; set; } = null!;
}
