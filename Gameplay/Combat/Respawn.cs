using PixelCore.Gameplay.Systems;
using PixelCore.Runtime.Core;

namespace PixelCore.Gameplay.Combat;

public static class Respawn
{
    public const float Delay = 1.2f;

    public const string SpawnPoint = "Spawn_Start";

    private static float _deadSeconds;

    public static void Update(Scene scene, float deltaTime)
    {
        var health = scene.FindPlayer()?.GetComponent<Health>();
        if (health is not { IsDead: true } || RoomFlow.IsTransitioning)
        {
            _deadSeconds = 0f;
            return;
        }

        _deadSeconds += deltaTime;
        if (_deadSeconds < Delay) return;

        _deadSeconds = 0f;
        RoomFlow.Transition(LevelSetup.StartRoom.SceneId, SpawnPoint, source: "respawn");
    }

#if DEBUG
    public static void DebugReset() => _deadSeconds = 0f;
#endif
}
