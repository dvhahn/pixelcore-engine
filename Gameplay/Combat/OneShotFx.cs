using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;

namespace PixelCore.Gameplay.Combat;

public class OneShotFx : Component
{
    private bool _started;

    public override void Update(float deltaTime)
    {
        var animator = Entity.GetComponent<Animator>();
        if (animator != null && animator.IsPlaying)
        {
            _started = true;
            return;
        }

        if (!_started && animator?.CurrentClip != null)
        {
            _started = true;
            animator.Play(animator.CurrentClip, restart: true);
            return;
        }

        Entity.Scene?.DestroyEntity(Entity);
    }
}
