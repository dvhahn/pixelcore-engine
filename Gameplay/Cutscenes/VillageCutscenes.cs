using System.Collections.Generic;
using PixelCore.Gameplay.Village;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Cutscenes;

namespace PixelCore.Gameplay.Cutscenes;

public static class VillageCutscenes
{
    public const string Stone = "village.stone";

    public static void Register()
    {
        CutsceneDirector.Register(Stone, StoneScene);
    }

    private static IEnumerator<Wait> StoneScene(Cutscene c)
    {
        using var _ = c.Freeze();

        var hero = c.Actor("hero");
        bool raining = Weather.IsRaining;

        if (!raining)
            yield return hero.Say("The stone is warm. It is humming.", 1);
        else
            yield return hero.Say("Once more, then.", 2);

        yield return c.Shake(0.6f, 0.4f);
        Weather.SetRain(!raining);
        yield return Wait.Seconds(0.8f);

        if (!raining)
            yield return hero.Say("Rain. He was not joking.", 3);
        else
            yield return hero.Say("The clouds are drifting off.", 4);
    }
}
