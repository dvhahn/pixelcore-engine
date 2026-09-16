using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using PixelCore.Runtime.Components;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Serialization;

public static class InstanceOverrides
{
    private const string TypeKey = "type";

    private static JsonObject? ToNode(ComponentData d)
    {
        try { return JsonSerializer.SerializeToNode(d, SceneJsonContext.Default.ComponentData)?.AsObject(); }
        catch (System.Exception ex)
        {
            System.Console.WriteLine($"[InstanceOverrides] serialization failed ({d.GetType().Name}): {ex.Message}");
            return null;
        }
    }

    private static ComponentData? FromNode(JsonObject node)
    {
        try { return node.Deserialize(SceneJsonContext.Default.ComponentData); }
        catch (System.Exception ex)
        {
            System.Console.WriteLine($"[InstanceOverrides] deserialization failed: {ex.Message}");
            return null;
        }
    }

    private static string? TypeOf(JsonObject node) =>
        node.TryGetPropertyValue(TypeKey, out var t) ? t?.GetValue<string>() : null;

    private const string OrdinalKey = "#";

    private static string Key(string type, int ordinal) => ordinal == 0 ? type : type + "#" + ordinal;

    private static string? KeyOf(JsonObject node)
    {
        var t = TypeOf(node);
        if (t == null) return null;
        int ord = node.TryGetPropertyValue(OrdinalKey, out var o) && o != null ? o.GetValue<int>() : 0;
        return Key(t, ord);
    }

    private static string TypePart(string key)
    {
        int i = key.IndexOf('#');
        return i < 0 ? key : key.Substring(0, i);
    }

    private static Dictionary<string, JsonObject> IndexByKey(IEnumerable<ComponentData> comps)
    {
        var byKey = new Dictionary<string, JsonObject>();
        var seen = new Dictionary<string, int>();
        foreach (var cd in comps)
        {
            var node = ToNode(cd);
            var t = node != null ? TypeOf(node) : null;
            if (node == null || t == null) continue;
            if (cd.LegacyKeys is { } legacy)
                foreach (var k in legacy) node.Remove(k);

            seen.TryGetValue(t, out int n);
            seen[t] = n + 1;
            byKey[Key(t, n)] = node;
        }
        return byKey;
    }

    private static readonly HashSet<SceneInstance> _computing = new();

    internal static bool IsComputing(SceneInstance inst) => _computing.Contains(inst);

    public static List<InstanceOverrideData>? Compute(SceneInstance inst)
    {
        var path = inst.ScenePath;
        if (string.IsNullOrEmpty(path)) return inst.Overrides;
        var baseData = SceneSerializer.LoadFromFile(path);
        if (baseData == null) return inst.Overrides;

        var hostPos = inst.Entity?.GetComponent<Transform>()?.Position ?? Vector2.Zero;

        if (!_computing.Add(inst)) return inst.Overrides;
        try
        {
        var liveById = new Dictionary<int, Entity>();
        if (inst.RootMerged && inst.Entity != null && inst.SourceIds.TryGetValue(inst.Entity, out var rootId))
            liveById[rootId] = inst.Entity;
        foreach (var child in inst.InstancedEntities)
            if (child.Components.Count > 0 && inst.SourceIds.TryGetValue(child, out var id))
                liveById[id] = child;

        var result = new List<InstanceOverrideData>();
        foreach (var ed in baseData.Entities)
        {
            var ov = new InstanceOverrideData { EntityId = ed.Id };

            if (!liveById.TryGetValue(ed.Id, out var live))
            {
                ov.Deleted = true;
                result.Add(ov);
                continue;
            }

            if (live.Name != ed.Name) ov.Name = live.Name;
            if (live.Active != ed.Active) ov.Active = live.Active;

            var liveDatas = ComponentDataRegistry.CaptureAll(live, forPersist: true);

            bool isMergedHost = ReferenceEquals(live, inst.Entity) && inst.RootMerged;
            if (isMergedHost)
                liveDatas = liveDatas.FindAll(d => d is not TransformData and not SceneInstanceData);
            foreach (var cd in liveDatas)
                if (cd is TransformData td) { td.X -= hostPos.X; td.Y -= hostPos.Y; }

            var baseByKey = IndexByKey(ed.Components);

            var liveKeys = new HashSet<string>();
            var liveSeen = new Dictionary<string, int>();
            foreach (var cd in liveDatas)
            {
                var liveNode = ToNode(cd);
                var type = liveNode != null ? TypeOf(liveNode) : null;
                if (liveNode == null || type == null) continue;
                liveSeen.TryGetValue(type, out int n);
                liveSeen[type] = n + 1;
                var key = Key(type, n);
                liveKeys.Add(key);

                if (baseByKey.TryGetValue(key, out var baseNode))
                {
                    var sparse = Diff(baseNode, liveNode);
                    if (sparse != null) { Stamp(sparse, n); (ov.Components ??= new()).Add(sparse); }
                }
                else
                {
                    Stamp(liveNode, n);
                    (ov.Components ??= new()).Add(liveNode);
                }
            }

            foreach (var key in baseByKey.Keys)
            {
                if (liveKeys.Contains(key)) continue;
                var t = TypePart(key);

                if (isMergedHost && (t == "transform" || t == "sceneInstance")) continue;

                if (CodeOwnedBody.Owns(live) && CodeOwnedBody.IsPart(t))
                {
                    Console.WriteLine($"[Prefab] transitional: {t} on '{live.Name}' was not recorded as a delete override " +
                                      "(a code-owned body - this path disappears once ownership moves)");
                    continue;
                }

                (ov.Removed ??= new()).Add(key);
            }

            if (!ov.IsEmpty) result.Add(ov);
        }

        return result.Count > 0 ? result : null;
        }
        finally { _computing.Remove(inst); }
    }

    private static void Stamp(JsonObject node, int ordinal)
    {
        if (ordinal != 0) node[OrdinalKey] = ordinal;
    }

    private static JsonObject? Diff(JsonObject baseNode, JsonObject liveNode)
    {
        JsonObject? sparse = null;
        var keys = new List<string>();
        foreach (var kv in baseNode) if (kv.Key != TypeKey) keys.Add(kv.Key);
        foreach (var kv in liveNode) if (kv.Key != TypeKey && !baseNode.ContainsKey(kv.Key)) keys.Add(kv.Key);

        foreach (var key in keys)
        {
            baseNode.TryGetPropertyValue(key, out var b);
            liveNode.TryGetPropertyValue(key, out var l);
            if (JsonNode.DeepEquals(b, l)) continue;
            sparse ??= new JsonObject { [TypeKey] = TypeOf(baseNode) };
            sparse[key] = l?.DeepClone();
        }
        return sparse;
    }

    public static List<ComponentData> BuildEffective(EntityData ed, InstanceOverrideData? ov)
    {
        if (ov == null || (ov.Components == null && ov.Removed == null))
            return ed.Components;

        var sparseByKey = new Dictionary<string, JsonObject>();
        if (ov.Components != null)
            foreach (var node in ov.Components)
            {
                var k = KeyOf(node);
                if (k != null) sparseByKey[k] = node;
            }
        var removed = ov.Removed != null ? new HashSet<string>(ov.Removed) : null;

        var result = new List<ComponentData>();
        var baseKeys = new HashSet<string>();
        var seen = new Dictionary<string, int>();
        foreach (var bd in ed.Components)
        {
            var baseNode = ToNode(bd);
            var type = baseNode != null ? TypeOf(baseNode) : null;
            if (baseNode == null || type == null) { result.Add(bd); continue; }
            seen.TryGetValue(type, out int n);
            seen[type] = n + 1;
            var key = Key(type, n);
            baseKeys.Add(key);

            if (removed != null && removed.Contains(key)) continue;

            if (sparseByKey.TryGetValue(key, out var sparse))
            {
                foreach (var kv in sparse)
                {
                    if (kv.Key == TypeKey || kv.Key == OrdinalKey) continue;
                    baseNode[kv.Key] = kv.Value?.DeepClone();
                }
                result.Add(FromNode(baseNode) ?? bd);
            }
            else
            {
                result.Add(bd);
            }
        }

        foreach (var (k, node) in sparseByKey)
            if (!baseKeys.Contains(k))
            {
                var clone = (JsonObject)node.DeepClone();
                clone.Remove(OrdinalKey);
                var added = FromNode(clone);
                if (added != null) result.Add(added);
            }

        return result;
    }
}
