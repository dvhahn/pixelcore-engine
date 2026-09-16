using System;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Cutscenes;
using PixelCore.Runtime.UI;
using PixelCore.Gameplay.Player;

namespace PixelCore.Gameplay.Systems;

public static class GameSystems
{
    public const string Music = "Audio/BGM/CalmVillage";

    private static Entity? _player;
    private static bool _dialogueTestShown;

    public static void Initialize(Scene scene, Entity? player)
    {
        _player = player;

        Interactable.DefaultHandler = InteractionDispatcher.Handle;

        CutsceneDirector.SetCast("hero", Scene.PlayerName);
        CutsceneDirector.SetCast("elder", "OldMan");
        Cutscenes.VillageCutscenes.Register();

        GameFlow.DayStarted -= StartMusic;
        GameFlow.DayStarted += StartMusic;

        Player.PlayerClips.Verify(scene);
    }

    private static void StartMusic() => Runtime.Audio.AudioManager.Instance.PlayBGM(Music);

    public static void Update(Scene scene, Camera camera, float deltaTime)
    {
        _player = scene.FindPlayer();

        DialogueBox.Update(deltaTime);

        PixelCore.Runtime.Story.DialogueRunner.Update(deltaTime);

        if (!_dialogueTestShown &&
            Environment.GetEnvironmentVariable("PIXELCORE_DIALOGUETEST") == "1")
        {
            _dialogueTestShown = true;
            DialogueBox.ShowLine("hero", PixelCore.Runtime.Story.DialogueRunner.SpeakerLabel("hero"),
                "A long line wraps by itself, and anything past three lines continues on the next page.");
        }

        RoomFlow.Update(scene, camera);

        Combat.Respawn.Update(scene, deltaTime);

        PixelCore.Runtime.Systems.InteractionHighlight.Update(scene);
    }

    public static void FixedTick(float dt)
    {
    }
}
