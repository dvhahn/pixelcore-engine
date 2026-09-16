using System;
using System.Collections.Generic;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;

namespace PixelCore.Gameplay.Player;

public static class PlayerClips
{
    private static readonly string[] Dirs = { "D", "U", "L", "R" };

    public static IEnumerable<string> Required()
    {
        foreach (var d in Dirs) yield return $"Walk_{d}";
        foreach (var d in Dirs) yield return $"Idle_{d}";
        foreach (var d in Dirs) yield return $"Attack_{d}";
        yield return "Dead";
    }

    public static int Verify(Scene? scene)
    {
        var player = scene?.FindPlayer();
        if (player == null)
        {
            return 0;
        }

        var anim = player.GetComponent<Animator>();
        if (anim == null)
        {
            Console.WriteLine("[Clips] ⚠ the player has no Animator - did the prefab fail to load?");
            return -1;
        }

        int missing = 0;
        foreach (var name in Required())
        {
            if (anim.HasClip(name)) continue;
            Console.WriteLine($"[Clips] ⚠ missing clip: '{name}' - code calls Play with this name " +
                              "(the .anim clip name and the Play argument have to match)");
            missing++;
        }
        return missing;
    }
}
