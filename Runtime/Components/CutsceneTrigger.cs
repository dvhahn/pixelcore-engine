using PixelCore.Runtime.Core;
using PixelCore.Runtime.Cutscenes;

namespace PixelCore.Runtime.Components;

[ComponentCategory(ComponentCategories.Interaction)]
public class CutsceneTrigger : Component
{
    public string CutsceneId { get; set; } = "";

    public bool Once { get; set; } = true;

    public override void OnTriggerEnter(Collider2D other)
    {
        if (string.IsNullOrEmpty(CutsceneId)) return;

        if (CutsceneDirector.SuppressTriggers) return;

        if (other.Entity?.GetComponent<Rigidbody2D>() == null) return;

        if (Once && CutsceneDirector.HasPlayed(CutsceneId)) return;
        if (CutsceneDirector.IsPlaying) return;

        CutsceneDirector.Play(CutsceneId);
    }
}
