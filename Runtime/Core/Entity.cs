using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelCore.Runtime.Core;

public class Entity
{
    private static int _nextId = 1;

    public int Id { get; }

    public int SerializedId { get; set; }

    public string Name { get; set; }

    public bool HideFromSerialization { get; set; }

    private bool _active = true;

    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            bool wasInHierarchy = ActiveInHierarchy;
            _active = value;
            if (wasInHierarchy != ActiveInHierarchy)
                PropagateActiveChange(ActiveInHierarchy);
        }
    }

    public bool ActiveInHierarchy => _active && (Parent?.ActiveInHierarchy ?? true);

    public Scene? Scene { get; internal set; }

    public Entity? Parent { get; private set; }
    private List<Entity> _children = new();
    public IReadOnlyList<Entity> Children => _children;

    private List<Component> _components = new();
    public IReadOnlyList<Component> Components => _components;

    public Entity(string name = "Entity")
    {
        Name = name;
        Id = _nextId++;
    }

    internal Entity(string name, int reuseId)
    {
        Name = name;
        Id = reuseId;
        if (reuseId >= _nextId) _nextId = reuseId + 1;
    }

    public void SetParent(Entity? parent)
    {
        if (Parent == parent) return;
        if (parent == this) return;

        for (var p = parent?.Parent; p != null; p = p.Parent)
        {
            if (p == this)
            {
                Console.WriteLine($"[Entity] SetParent refused: '{parent!.Name}' is a descendant of '{Name}' (cycle)");
                return;
            }
        }

        Parent?._children.Remove(this);

        Parent = parent;
        parent?._children.Add(this);
    }

    public bool IsRoot => Parent == null;

    public int GetSiblingIndex()
        => Parent == null ? -1 : Parent._children.IndexOf(this);

    public void SetSiblingIndex(int index)
    {
        if (Parent == null) return;
        var list = Parent._children;
        list.Remove(this);
        list.Insert(Math.Clamp(index, 0, list.Count), this);
    }

    private void PropagateActiveChange(bool active)
    {
        for (int i = 0; i < _components.Count; i++)
        {
            if (active) _components[i].RaiseOnEnable();
            else _components[i].RaiseOnDisable();
        }
        for (int i = 0; i < _children.Count; i++)
        {
            var child = _children[i];
            if (child._active) child.PropagateActiveChange(active);
        }
    }

    public T AddComponent<T>() where T : Component, new() => (T)AddComponent(new T());

    public Component AddComponent(Component component)
    {
        if (component.Entity != null)
            throw new System.InvalidOperationException(
                $"{component.GetType().Name} is already attached to another entity - attach a new instance");
        component.Entity = this;
        _components.Add(component);
        component.Initialize();
        if (ActiveInHierarchy && component.Enabled)
            component.OnEnable();
        return component;
    }

    public T? GetComponent<T>() where T : Component
    {
        return _components.OfType<T>().FirstOrDefault();
    }

    public IEnumerable<T> GetComponents<T>() where T : Component
    {
        return _components.OfType<T>();
    }

    public bool HasComponent<T>() where T : Component
    {
        return _components.OfType<T>().Any();
    }

    public void RemoveComponent<T>() where T : Component
    {
        var component = GetComponent<T>();
        if (component != null)
        {
            RemoveComponent(component);
        }
    }

    public void RemoveComponent(Component component)
    {
        if (_components.Contains(component))
        {
            component.OnDestroy();
            _components.Remove(component);
        }
    }

    internal void Update(float deltaTime)
    {
        if (!ActiveInHierarchy) return;
        for (int i = 0; i < _components.Count; i++)
        {
            var component = _components[i];
            if (component.Enabled) component.Update(deltaTime);
        }
    }

    internal void FixedTick(float fixedDeltaTime)
    {
        if (!ActiveInHierarchy) return;
        for (int i = 0; i < _components.Count; i++)
        {
            var component = _components[i];
            if (component.Enabled) component.FixedTick(fixedDeltaTime);
        }
    }

    internal void Destroy()
    {
        for (int i = 0; i < _components.Count; i++)
        {
            _components[i].OnDestroy();
        }
        _components.Clear();
    }
}
