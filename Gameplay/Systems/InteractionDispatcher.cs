using System;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Cutscenes;
using PixelCore.Runtime.Story;
using PixelCore.Runtime.UI;

namespace PixelCore.Gameplay.Systems;

public static class InteractionDispatcher
{
    public static void Handle(Interactable it, Entity actor)
    {
        switch (it.Kind)
        {
            case InteractKind.Read:
                if (!string.IsNullOrEmpty(it.Block)) { DialogueRunner.Play(it.Block); break; }

                DialogueBox.Show(null, string.IsNullOrEmpty(it.Text) ? "(empty text)" : it.Text);
                break;

            case InteractKind.Door:
                RoomFlow.Transition(it.TargetSceneId, it.Spawn, source: it.Entity.Name,
                                    facing: it.Facing);
                break;

            case InteractKind.Event:
                if (CutsceneDirector.IsRegistered(it.Event)) { CutsceneDirector.Play(it.Event); break; }
                if (Runtime.Systems.GameActions.TryRun(it.Event, actor)) break;
                Console.WriteLine($"[Event] {(string.IsNullOrEmpty(it.Event) ? "(empty event)" : it.Event)} - neither a registered cutscene nor a registered event");
                break;
        }
    }
}
