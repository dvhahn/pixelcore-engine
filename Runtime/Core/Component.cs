using PixelCore.Runtime.Physics;
using PixelCore.Runtime.Components;

namespace PixelCore.Runtime.Core;

public abstract class Component
{
    public Entity Entity { get; internal set; } = null!;

    private bool _enabled = true;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (Entity == null || Entity.ActiveInHierarchy)
            {
                if (value) OnEnable();
                else OnDisable();
            }
        }
    }

    public virtual void Initialize() { }
    public virtual void Update(float deltaTime) { }

    public virtual void FixedTick(float fixedDeltaTime) { }
    public virtual void Draw() { }
    public virtual void OnDestroy() { }

    public virtual void OnEnable() { }

    public virtual void OnDisable() { }

    public virtual void OnCollisionEnter(CollisionInfo info) { }
    public virtual void OnCollisionStay(CollisionInfo info) { }
    public virtual void OnCollisionExit(CollisionInfo info) { }
    public virtual void OnTriggerEnter(Collider2D other) { }
    public virtual void OnTriggerStay(Collider2D other) { }
    public virtual void OnTriggerExit(Collider2D other) { }

    internal void RaiseOnEnable() { if (_enabled) OnEnable(); }
    internal void RaiseOnDisable() { if (_enabled) OnDisable(); }
}
