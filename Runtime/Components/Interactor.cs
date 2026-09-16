using Microsoft.Xna.Framework;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Components;

[ComponentCategory(ComponentCategories.Interaction)]
public class Interactor : Component
{
    public const float DefaultReach = 20f;

    public float Reach { get; set; } = DefaultReach;

    public float FacingDot { get; set; } = 0.35f;

    public Vector2 Facing { get; private set; } = new Vector2(0f, 1f);

    public Interactable? Current { get; private set; }

    public static bool GlobalLock;

    public void SetFacing(Vector2 dir)
    {
        if (dir != Vector2.Zero) Facing = Vector2.Normalize(dir);
    }

    public override void Update(float deltaTime)
    {
        if (GlobalLock || Cutscenes.FreezeState.IsFrozen(Cutscenes.FreezeFlags.Interaction)
            || UI.DialogueBox.Active || UI.DialogueBox.JustClosed)
        {
            SetCurrent(null);
            return;
        }

        var transform = Entity.GetComponent<Transform>();
        var scene = Entity.Scene;
        if (transform == null || scene == null) { SetCurrent(null); return; }

        Interactable? best = null;
        float bestDist = float.MaxValue;

        foreach (var e in scene.Entities)
        {
            if (e == Entity || !e.ActiveInHierarchy) continue;
            var it = e.GetComponent<Interactable>();
            if (it == null || !it.Enabled || !it.CanInteract(Entity)) continue;

            var t = e.GetComponent<Transform>();
            if (t == null) continue;

            var to = t.Position - transform.Position;
            float dist = to.Length();
            if (dist > Reach + it.Range) continue;
            if (dist > 0.01f && Vector2.Dot(to / dist, Facing) < FacingDot) continue;

            if (dist < bestDist) { best = it; bestDist = dist; }
        }

        SetCurrent(best);

        if (Current != null && InputMap.IsPressed(GameAction.Interact))
            Current.Interact(Entity);
    }

    private void SetCurrent(Interactable? next)
    {
        if (Current == next) return;
        if (Current != null) Current.Focused = false;
        Current = next;
        if (Current != null) Current.Focused = true;
    }

    public override void OnDisable() => SetCurrent(null);
}
