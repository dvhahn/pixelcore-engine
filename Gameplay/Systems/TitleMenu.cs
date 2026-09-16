using System;
using System.Collections.Generic;
using PixelCore.Runtime.Rendering;
using PixelCore.Runtime.UI;

namespace PixelCore.Gameplay.Systems;

public static class TitleMenu
{
    public static Action? QuitRequested;

    public static void Open()
    {
        var ctx = GameContextRef;
        bool hasSave = ctx?.Save.HasSave ?? false;

        TitleScreen.Open("PixelCore", "sample game", new List<TitleItem>
        {
            new("New game", NewGame),
            new("Continue", Continue, enabled: hasSave),
            new("Quit", () => QuitRequested?.Invoke(), enabled: QuitRequested != null),
        });
    }

    private static Runtime.Core.GameContext? GameContextRef => GameFlow.Context;

    private static void NewGame()
    {
        var ctx = GameContextRef;
        if (ctx == null)
        {
            Console.Error.WriteLine("[Title] ✘ GameFlow.Bind was never called - cannot start a new game");
            return;
        }

        ctx.Save.ResetForNewGame();
        Begin(GameFlow.StartDay(1, DayPhase.Morning));
    }

    private static void Continue()
    {
        var ctx = GameContextRef;
        if (ctx == null)
        {
            Console.Error.WriteLine("[Title] ✘ GameFlow.Bind was never called - cannot continue");
            return;
        }

        var result = ctx.Save.LoadLatest();
        Begin(result.Ok);
    }

    private static void Begin(bool ok)
    {
        if (!ok) return;

        TitleScreen.Close();

        ScreenFader.Set(1f);
        ScreenFader.FadeTo(0f, 0.45f);
    }
}
