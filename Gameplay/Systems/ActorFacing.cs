using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;
using PixelCore.Gameplay.Player;

namespace PixelCore.Gameplay.Systems;

public static class ActorFacing
{
    public static bool Set(Entity? actor, Vector2 dir)
    {
        var pc = actor?.GetComponent<PlayerController>();
        if (pc == null) return false;

        pc.Facing = dir;
        return true;
    }

    public static Vector2? Get(Entity? actor) => actor?.GetComponent<PlayerController>()?.Facing;
}
