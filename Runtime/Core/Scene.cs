using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using PixelCore.Runtime.Components;

namespace PixelCore.Runtime.Core;

public enum SceneKind
{
    Level,
    Prefab
}

public class Scene
{
    public string Name { get; set; }

    public SceneKind Kind { get; set; } = SceneKind.Level;

    public int NextSerializedId { get; set; }

    public Microsoft.Xna.Framework.Color BackColor { get; set; }
        = Microsoft.Xna.Framework.Color.Black;

    public bool LightingEnabled { get; set; }

    public Microsoft.Xna.Framework.Color AmbientLight { get; set; }
        = new Microsoft.Xna.Framework.Color(110, 110, 130);

    public Microsoft.Xna.Framework.Color? AmbientRuntime { get; private set; }

    public Microsoft.Xna.Framework.Color EffectiveAmbient => AmbientRuntime ?? AmbientLight;

    private Microsoft.Xna.Framework.Color _ambientFrom, _ambientTo;
    private float _ambientT, _ambientDuration;

    public void SetAmbient(Microsoft.Xna.Framework.Color target, float fadeSeconds = 0f)
    {
        if (Exterior)
        {
            System.Console.Error.WriteLine(
                $"[Lighting] ✘ SetAmbient has no effect in exterior scene '{Name}' - the clock owns the " +
                "ambient there (Scene.Exterior). Turn off the exterior flag in the scene settings to use an interior profile.");
            return;
        }

        if (fadeSeconds <= 0f)
        {
            _ambientDuration = 0f;
            AmbientRuntime = target;
            return;
        }

        _ambientFrom = EffectiveAmbient;
        _ambientTo = target;
        _ambientT = 0f;
        _ambientDuration = fadeSeconds;
        AmbientRuntime = _ambientFrom;
    }

    public void UpdateAmbient(float dt)
    {
        if (_ambientDuration <= 0f) return;
        _ambientT += dt / _ambientDuration;
        if (_ambientT >= 1f)
        {
            AmbientRuntime = _ambientTo;
            _ambientDuration = 0f;
            return;
        }
        AmbientRuntime = Microsoft.Xna.Framework.Color.Lerp(_ambientFrom, _ambientTo, _ambientT);
    }

    public void ResetAmbient()
    {
        AmbientRuntime = null;
        _ambientDuration = 0f;
    }

    public bool Exterior { get; set; }

    public float ViewZoom { get; set; } = 1f;

    public bool CameraFixed { get; set; }

    public Microsoft.Xna.Framework.Vector2 CameraFixedPos { get; set; }

    public bool CameraBoundsEnabled { get; set; }

    public Microsoft.Xna.Framework.Rectangle CameraBounds { get; set; }

    public bool CameraDampingEnabled { get; set; }

    public Microsoft.Xna.Framework.Vector2 CameraDamping { get; set; } = new(2.3f, 2.3f);

    public List<Audio.AmbientTrack> Ambients { get; } = new();

    public string? DefaultSurfaceId { get; set; }

    public string? SkyId { get; set; }

    public Rendering.SkyProfile? Sky { get; set; }

    public bool ApplySkyPreset(string? id)
    {
        SkyId = string.IsNullOrEmpty(id) ? null : id;
        var asset = Rendering.SkyPresetCache.Get(SkyId);
        Sky = asset?.Sky.ToProfile();
        return Sky != null;
    }

    public void ClearSky()
    {
        SkyId = null;
        Sky = null;
    }

    public Rendering.PostProfile Post { get; set; } = new();

    public List<Rendering.PostPreset> PostPresets { get; } = new();

    public string? ActivePostKey { get; set; }

    private Rendering.PostProfile? _postBlendFrom;
    private Rendering.PostProfile? _postBlendTo;
    private float _postBlendT, _postBlendDuration;

    public Rendering.PostProfile? PostBlendFrom => _postBlendFrom;
    public Rendering.PostProfile? PostBlendTo => _postBlendTo;
    public float PostBlendT => _postBlendT;

    public Rendering.PostPreset? FindPostPreset(string key) =>
        PostPresets.FirstOrDefault(p => p.Key == key);

    public bool ApplyPostPreset(string key)
    {
        var preset = FindPostPreset(key);
        if (preset == null) return false;
        _postBlendFrom = _postBlendTo = null;
        Post = preset.Profile.Clone();
        ActivePostKey = key;
        return true;
    }

    public bool BlendPostPreset(string key, float duration = 0.7f)
    {
        var preset = FindPostPreset(key);
        if (preset == null) return false;
        if (duration <= 0f) return ApplyPostPreset(key);
        _postBlendFrom = Post.Clone();
        _postBlendTo = preset.Profile;
        _postBlendT = 0f;
        _postBlendDuration = duration;
        ActivePostKey = key;
        return true;
    }

    public void ClearPost(float duration = 0f)
    {
        ActivePostKey = null;
        if (duration <= 0f)
        {
            _postBlendFrom = _postBlendTo = null;
            Post = new Rendering.PostProfile();
            return;
        }
        _postBlendFrom = Post.Clone();
        _postBlendTo = new Rendering.PostProfile();
        _postBlendT = 0f;
        _postBlendDuration = duration;
    }

    public void UpdatePostBlend(float dt)
    {
        if (_postBlendTo == null || _postBlendFrom == null) return;
        _postBlendT += dt / _postBlendDuration;
        if (_postBlendT >= 1f)
        {
            Post = _postBlendTo.Clone();
            _postBlendFrom = _postBlendTo = null;
            return;
        }
        Post = Rendering.PostProfile.Lerp(_postBlendFrom, _postBlendTo, _postBlendT);
    }

    private List<Entity> _entities = new();
    private List<Entity> _pendingAdd = new();
    private List<Entity> _pendingRemove = new();
    private readonly List<IRenderable> _renderBuffer = new();
    private readonly List<IRenderable> _sortedBuffer = new();

    private readonly List<GroupRenderable> _groupPool = new();
    private int _groupsUsed;

    public IReadOnlyList<Entity> Entities => _entities;

    public Scene(string name = "Scene")
    {
        Name = name;
    }

    public Entity CreateEntity(string name = "Entity") => CreateEntityCore(name, 0);

    internal Entity CreateEntityWithId(string name, int reuseId)
    {
        if (reuseId > 0 && FindEntityById(reuseId) is { } live)
            System.Console.Error.WriteLine($"[Scene] duplicate runtime Id: {reuseId} - '{live.Name}' is still alive but '{name}' was restored with the same number");
        return CreateEntityCore(name, reuseId);
    }

    private Entity CreateEntityCore(string name, int reuseId)
    {
        var entity = reuseId > 0 ? new Entity(name, reuseId) : new Entity(name);
        entity.Scene = this;
        entity.AddComponent<Transform>();
        _pendingAdd.Add(entity);
        return entity;
    }

    public void DestroyEntity(Entity entity)
    {
        if (_entities.Contains(entity) || _pendingAdd.Contains(entity))
        {
            if (!_pendingRemove.Contains(entity))
                _pendingRemove.Add(entity);
            foreach (var child in entity.Children)
                DestroyEntity(child);
        }
    }

    public const string PlayerName = "Player";

    public Entity? FindPlayer() => FindEntity(PlayerName);

    public Entity? FindEntity(string name)
    {
        return _entities.FirstOrDefault(e => e.Name == name);
    }

    public Entity? FindEntityById(int id)
    {
        if (id <= 0) return null;

        Entity? hit = null;
        foreach (var e in _entities)
            if (e.Id == id) { hit = e; break; }
        if (hit == null)
            foreach (var e in _pendingAdd)
                if (e.Id == id) { hit = e; break; }

        if (hit != null && _pendingRemove.Count > 0 && _pendingRemove.Contains(hit)) return null;
        return hit;
    }

    public IReadOnlyList<Entity> FindEntities(string name)
    {
        var found = new List<Entity>();
        foreach (var e in _entities)
            if (e.Name == name) found.Add(e);
        return found;
    }

    public IEnumerable<Entity> FindEntitiesWithComponent<T>() where T : Component
    {
        return _entities.Where(e => e.HasComponent<T>());
    }

    public void Clear()
    {
        foreach (var entity in _entities)
        {
            entity.Destroy();
        }
        _entities.Clear();
        _pendingAdd.Clear();
        _pendingRemove.Clear();
    }

    public int GetSiblingIndex(Entity e)
    {
        if (!e.IsRoot) return e.GetSiblingIndex();
        int idx = 0;
        foreach (var x in _entities)
        {
            if (x == e) return idx;
            if (x.IsRoot) idx++;
        }
        return -1;
    }

    public void ReorderSibling(Entity e, Entity? newParent, int index)
    {
        FlushPendingAdds();

        bool sameParent = e.Parent == newParent;
        int oldIndex = GetSiblingIndex(e);

        if (!sameParent)
        {
            e.SetParent(newParent);
            if (e.Parent != newParent) return;
        }
        else if (oldIndex >= 0 && oldIndex < index)
        {
            index--;
        }

        if (newParent != null)
        {
            e.SetSiblingIndex(index);
            return;
        }

        _entities.Remove(e);
        int seen = 0, insertAt = _entities.Count;
        for (int i = 0; i < _entities.Count; i++)
        {
            if (!_entities[i].IsRoot) continue;
            if (seen == index) { insertAt = i; break; }
            seen++;
        }
        _entities.Insert(insertAt, e);
    }

    public void FlushPendingAdds()
    {
        foreach (var entity in _pendingAdd)
        {
            _entities.Add(entity);
        }
        _pendingAdd.Clear();
    }

    internal void FlushPending()
    {
        FlushPendingAdds();

        for (int i = 0; i < _pendingRemove.Count; i++)
        {
            var entity = _pendingRemove[i];
            entity.SetParent(null);
            entity.Destroy();
            _entities.Remove(entity);
            _pendingAdd.Remove(entity);
        }
        _pendingRemove.Clear();
    }

    internal void Update(float deltaTime)
    {
        FlushPending();

        for (int i = 0; i < _entities.Count; i++)
        {
            _entities[i].Update(deltaTime);
        }
    }

    internal void FixedTick(float fixedDeltaTime)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            _entities[i].FixedTick(fixedDeltaTime);
        }
    }

    internal void Draw(SpriteBatch spriteBatch,
        int minLayer = int.MinValue, int maxLayerExclusive = int.MaxValue,
        IRenderable? split = null, bool belowSplit = true)
    {
        foreach (var renderable in BuildRenderOrder(minLayer, maxLayerExclusive, split, belowSplit))
        {
            if (Classify(renderable) != RenderBucket.WorldAlpha) continue;
            renderable.Render(spriteBatch);
        }
    }

    internal bool DrawAdditive(SpriteBatch spriteBatch,
        int minLayer = int.MinValue, int maxLayerExclusive = int.MaxValue,
        IRenderable? split = null, bool belowSplit = true)
    {
        _additiveBuffer.Clear();
        foreach (var r in BuildRenderOrder(minLayer, maxLayerExclusive, split, belowSplit))
            if (Classify(r) == RenderBucket.WorldAdditive) _additiveBuffer.Add(r);

        if (_additiveBuffer.Count == 0) return false;
        for (int i = 0; i < _additiveBuffer.Count; i++) _additiveBuffer[i].Render(spriteBatch);
        return true;
    }

    internal bool HasAdditive(int minLayer = int.MinValue, int maxLayerExclusive = int.MaxValue,
        IRenderable? split = null, bool belowSplit = true)
    {
        foreach (var r in BuildRenderOrder(minLayer, maxLayerExclusive, split, belowSplit))
            if (IsAdditiveRenderable(r)) return true;
        return false;
    }

    internal static bool IsAdditiveRenderable(IRenderable renderable)
        => renderable is IAdditive { IsAdditive: true };

    internal static bool IsAtmosphereRenderable(IRenderable renderable)
        => renderable is IAtmosphere { InAtmosphere: true };

    public enum RenderBucket { WorldAlpha, WorldAdditive, AtmosphereAlpha, AtmosphereAdditive }

    public static RenderBucket Classify(IRenderable renderable)
    {
        bool atmosphere = IsAtmosphereRenderable(renderable);
        bool additive = IsAdditiveRenderable(renderable);
        return (atmosphere, additive) switch
        {
            (false, false) => RenderBucket.WorldAlpha,
            (false, true) => RenderBucket.WorldAdditive,
            (true, false) => RenderBucket.AtmosphereAlpha,
            (true, true) => RenderBucket.AtmosphereAdditive,
        };
    }

    private readonly List<IRenderable> _atmosphereBuffer = new();

    internal bool DrawAtmosphere(SpriteBatch spriteBatch, bool additive,
        int minLayer = int.MinValue, int maxLayerExclusive = int.MaxValue)
    {
        _atmosphereBuffer.Clear();
        foreach (var r in BuildRenderOrder(minLayer, maxLayerExclusive))
            if (Classify(r) == (additive ? RenderBucket.AtmosphereAdditive
                                          : RenderBucket.AtmosphereAlpha))
                _atmosphereBuffer.Add(r);

        if (_atmosphereBuffer.Count == 0) return false;
        for (int i = 0; i < _atmosphereBuffer.Count; i++) _atmosphereBuffer[i].Render(spriteBatch);
        return true;
    }

    public bool HasAtmosphere()
    {
        foreach (var e in _entities)
        {
            if (!e.ActiveInHierarchy) continue;
            foreach (var c in e.Components)
            {
                if (!c.Enabled) continue;
                if (c is IRenderable r && IsAtmosphereRenderable(r)) return true;
                if (c is IRenderableSource source)
                {
                    var contributed = source.Renderables;
                    for (int i = 0; i < contributed.Count; i++)
                        if (IsAtmosphereRenderable(contributed[i])) return true;
                }
            }
        }
        return false;
    }

    private readonly List<IRenderable> _additiveBuffer = new();

    internal List<IRenderable> BuildRenderOrder(
        int minLayer = int.MinValue, int maxLayerExclusive = int.MaxValue,
        IRenderable? split = null, bool belowSplit = true)
    {
        int splitLayer = split?.RenderLayer ?? 0;
        float splitY = split?.SortY ?? 0f;

        _renderBuffer.Clear();
        _groupsUsed = 0;

        void TryAdd(IRenderable renderable)
        {
            if (renderable.RenderLayer < minLayer || renderable.RenderLayer >= maxLayerExclusive) return;
            if (split != null)
            {
                if (ReferenceEquals(renderable, split)) return;
                bool below = renderable.RenderLayer < splitLayer ||
                             (renderable.RenderLayer == splitLayer && renderable.SortY <= splitY);
                if (below != belowSplit) return;
            }
            _renderBuffer.Add(renderable);
        }

        void Collect(Entity entity)
        {
            if (!entity.ActiveInHierarchy) return;

            var group = entity.GetComponent<SortingGroup>();
            if (group is { Enabled: true } && !(split != null && SubtreeHas(entity, split)))
            {
                var folded = RentGroup(group);
                FoldInto(entity, folded);
                if (folded.Count > 0)
                {
                    folded.SortMembers();
                    TryAdd(folded);
                }
                return;
            }

            foreach (var component in entity.Components)
            {
                if (!component.Enabled) continue;
                if (component is IRenderable renderable) TryAdd(renderable);
                else if (component is IRenderableSource source)
                {
                    var contributed = source.Renderables;
                    for (int i = 0; i < contributed.Count; i++) TryAdd(contributed[i]);
                }
            }
            foreach (var child in entity.Children)
                Collect(child);
        }
        foreach (var entity in _entities)
            if (entity.IsRoot) Collect(entity);

        _sortedBuffer.Clear();
        _sortedBuffer.AddRange(_renderBuffer
            .OrderBy(r => r.RenderLayer)
            .ThenBy(r => r.SortY));
        return _sortedBuffer;
    }

    internal static IReadOnlyList<IRenderable>? GroupMembersOf(IRenderable renderable)
        => (renderable as GroupRenderable)?.Members;

    internal List<IRenderable> BuildDrawList()
    {
        var order = BuildRenderOrder();
        _flatBuffer.Clear();
        for (int i = 0; i < order.Count; i++) Flatten(order[i]);
        return _flatBuffer;
    }

    private void Flatten(IRenderable renderable)
    {
        var members = GroupMembersOf(renderable);
        if (members == null) { _flatBuffer.Add(renderable); return; }
        for (int i = 0; i < members.Count; i++) Flatten(members[i]);
    }

    private readonly List<IRenderable> _flatBuffer = new();

    private void FoldInto(Entity entity, GroupRenderable target)
    {
        if (!entity.ActiveInHierarchy) return;

        foreach (var component in entity.Components)
        {
            if (!component.Enabled) continue;
            if (component is IRenderable renderable)
                target.Add(renderable, renderable.SortOffset);
            else if (component is IRenderableSource source)
            {
                var contributed = source.Renderables;
                for (int i = 0; i < contributed.Count; i++)
                    target.Add(contributed[i], contributed[i].SortOffset);
            }
        }

        foreach (var child in entity.Children)
        {
            var nestedGroup = child.GetComponent<SortingGroup>();
            if (nestedGroup is { Enabled: true } && child.ActiveInHierarchy)
            {
                var nested = RentGroup(nestedGroup);
                FoldInto(child, nested);
                if (nested.Count > 0)
                {
                    nested.SortMembers();
                    target.Add(nested, nestedGroup.SortOffset);
                }
            }
            else FoldInto(child, target);
        }
    }

    private static bool SubtreeHas(Entity entity, IRenderable target)
    {
        foreach (var component in entity.Components)
            if (ReferenceEquals(component, target)) return true;
        foreach (var child in entity.Children)
            if (SubtreeHas(child, target)) return true;
        return false;
    }

    private GroupRenderable RentGroup(SortingGroup group)
    {
        if (_groupsUsed == _groupPool.Count) _groupPool.Add(new GroupRenderable());
        var g = _groupPool[_groupsUsed++];
        g.Reset(group);
        return g;
    }

    private sealed class GroupRenderable : IRenderable
    {
        private struct Member { public IRenderable R; public float Order; public int Seq; }

        private readonly List<Member> _members = new();
        private SortingGroup _group = null!;

        public void Reset(SortingGroup group) { _group = group; _members.Clear(); _ordered.Clear(); }
        public int Count => _members.Count;

        public void Add(IRenderable r, float order)
            => _members.Add(new Member { R = r, Order = order, Seq = _members.Count });

        private static readonly System.Comparison<Member> ByOrder = (a, b) =>
            a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Seq.CompareTo(b.Seq);

        public void SortMembers()
        {
            _members.Sort(ByOrder);
            _ordered.Clear();
            for (int i = 0; i < _members.Count; i++) _ordered.Add(_members[i].R);
        }

        private readonly List<IRenderable> _ordered = new();
        public IReadOnlyList<IRenderable> Members => _ordered;

        public int RenderLayer => _group.RenderLayer;
        public float SortY => _group.SortY;
        public float SortOffset => _group.SortOffset;

        public void Render(SpriteBatch spriteBatch)
        {
            for (int i = 0; i < _ordered.Count; i++)
                _ordered[i].Render(spriteBatch);
        }
    }
}
