using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using PixelCore.Runtime.Animation;
using PixelCore.Runtime.Serialization;
using PixelCore.Runtime.Core;

namespace PixelCore.Runtime.Assets;

public static class AssetScanner
{
    public static void Scan(string contentRoot, bool promote)
    {
        if (!Directory.Exists(contentRoot)) return;

        var reg = AssetRegistry.Instance;
        var seen = new HashSet<string>();
        int promoted = 0, moved = 0;

        foreach (var file in EnumerateSorted(contentRoot, "*.atlas"))
        {
            var atlas = SpriteAtlas.Load(file);
            if (atlas == null) continue;

            var rel = ToRelative(contentRoot, file);
            var (id, changed, _) = Reconcile(atlas.Id, rel, contentRoot, seen, reg, ref moved);
            atlas.Id = id;

            if (changed && promote)
            {
                atlas.Save(file);
                promoted++;
            }
        }

        PostAssetCache.Clear();
        foreach (var file in EnumerateSorted(contentRoot, "*.post"))
        {
            var post = PostAsset.Load(file);
            if (post == null) continue;

            var rel = ToRelative(contentRoot, file);
            var (id, changed, _) = Reconcile(post.Id, rel, contentRoot, seen, reg, ref moved);
            post.Id = id;

            if (changed && promote)
            {
                post.Save(file);
                promoted++;
            }
            PostAssetCache.Put(post);
        }

        Audio.SurfaceLibrary.Clear();
        foreach (var file in EnumerateSorted(contentRoot, "*.surface"))
        {
            var surface = Audio.SurfaceAsset.Load(file);
            if (surface == null) continue;

            var rel = ToRelative(contentRoot, file);
            var (id, changed, _) = Reconcile(surface.Id, rel, contentRoot, seen, reg, ref moved);
            surface.Id = id;

            if (changed && promote)
            {
                surface.Save(file);
                promoted++;
            }
            Audio.SurfaceLibrary.Put(surface);
        }

        Particles.ParticlePresetCache.Clear();
        foreach (var file in EnumerateSorted(contentRoot, "*.particle"))
        {
            var particle = Particles.ParticleAsset.Load(file);
            if (particle == null) continue;

            var rel = ToRelative(contentRoot, file);
            var (id, changed, _) = Reconcile(particle.Id, rel, contentRoot, seen, reg, ref moved);
            particle.Id = id;

            if (changed && promote)
            {
                particle.Save(file);
                promoted++;
            }
            Particles.ParticlePresetCache.Put(particle);
        }

        Rendering.SkyPresetCache.Clear();
        foreach (var file in EnumerateSorted(contentRoot, "*.sky"))
        {
            var sky = Rendering.SkyAsset.Load(file);
            if (sky == null) continue;

            var rel = ToRelative(contentRoot, file);
            var (id, changed, _) = Reconcile(sky.Id, rel, contentRoot, seen, reg, ref moved);
            sky.Id = id;

            if (changed && promote)
            {
                sky.Save(file);
                promoted++;
            }
            Rendering.SkyPresetCache.Put(sky);
        }

        foreach (var file in EnumerateSorted(contentRoot, "*.anim"))
        {
            var anim = AnimationData.Load(file);
            if (anim == null) continue;

            var rel = ToRelative(contentRoot, file);
            var (id, changed, _) = Reconcile(anim.Id, rel, contentRoot, seen, reg, ref moved);
            anim.Id = id;
            bool dirty = changed;

            if (string.IsNullOrEmpty(anim.AtlasId) && !string.IsNullOrEmpty(anim.AtlasPath))
            {
                var atlasRel = Path.ChangeExtension(anim.AtlasPath, ".atlas").Replace('\\', '/');
                var atlasId = reg.GetId(atlasRel);
                if (atlasId != null)
                {
                    anim.AtlasId = atlasId;
                    dirty = true;
                }
            }
            if (!string.IsNullOrEmpty(anim.AtlasId))
            {
                var live = reg.GetPath(anim.AtlasId);
                if (!string.IsNullOrEmpty(live))
                {
                    var want = Path.ChangeExtension(live, ".png").Replace('\\', '/');
                    if (anim.AtlasPath != want) { anim.AtlasPath = want; dirty = true; }
                }
            }

            if (dirty && promote)
            {
                anim.Save(file);
                promoted++;
            }
        }

        foreach (var file in EnumerateSorted(contentRoot, "*.scene"))
        {
            JsonObject? root;
            try { root = JsonNode.Parse(File.ReadAllText(file)) as JsonObject; }
            catch { continue; }
            if (root == null) continue;

            var rel = ToRelative(contentRoot, file);
            var current = root["id"]?.GetValue<string>() ?? "";
            var (id, changed, _) = Reconcile(current, rel, contentRoot, seen, reg, ref moved);

            if (changed && promote)
            {
                WriteSceneId(file, root, id);
                promoted++;
            }
        }

        if (promote)
        {
            var missingByHash = new Dictionary<string, List<string>>();
            foreach (var (eid, entry) in reg.Entries)
            {
                if (entry.Hash.Length == 0) continue;
                if (File.Exists(Path.Combine(contentRoot, entry.Path))) continue;
                if (!missingByHash.TryGetValue(entry.Hash, out var list))
                    missingByHash[entry.Hash] = list = new List<string>();
                list.Add(eid);
            }

            foreach (var file in EnumerateSorted(contentRoot, "*.*"))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (Array.IndexOf(ForeignExtensions, ext) < 0) continue;

                var rel = ToRelative(contentRoot, file);
                var hash = HashFile(file);
                var knownId = reg.GetId(rel);

                if (knownId != null)
                {
                    reg.SetHash(knownId, hash);
                    continue;
                }

                if (missingByHash.TryGetValue(hash, out var candidates) && candidates.Count > 0)
                {
                    var name = Path.GetFileName(rel);
                    int pick = candidates.FindIndex(c => Path.GetFileName(reg.GetPath(c) ?? "") == name);
                    if (pick < 0) pick = 0;
                    var mid = candidates[pick];
                    Console.WriteLine($"[AssetScan] move detected by hash: {reg.GetPath(mid)} → {rel}");
                    reg.UpdatePath(mid, rel);
                    candidates.RemoveAt(pick);
                    moved++;
                }
                else
                {
                    var nid = reg.GetOrCreateId(rel);
                    reg.SetHash(nid, hash);
                }
            }

            var toRemove = new List<string>();
            foreach (var (eid, entry) in reg.Entries)
            {
                var ext = Path.GetExtension(entry.Path).ToLowerInvariant();
                if (ext != ".atlas" && ext != ".anim" && ext != ".post"
                    && ext != ".surface" && ext != ".scene" && ext != ".particle" && ext != ".sky") continue;
                if (seen.Contains(eid)) continue;
                if (!File.Exists(Path.Combine(contentRoot, entry.Path)))
                    toRemove.Add(eid);
            }
            foreach (var eid in toRemove) reg.Remove(eid);

            if (!reg.Save())
                Console.Error.WriteLine("[AssetScanner] could not record the scan result in the registry - the same promotion and reconciliation will run again on the next boot");
        }

        if (promoted > 0 || moved > 0)
            Console.WriteLine($"[AssetScan] promoted {promoted}, moved {moved}");

        AssetEvents.RaiseRescanned();
    }

    private static void WriteSceneId(string file, JsonObject root, string id)
    {
        var rebuilt = new JsonObject();
        if (root.TryGetPropertyValue("schemaVersion", out var schema))
            rebuilt["schemaVersion"] = schema?.DeepClone();
        rebuilt["id"] = id;
        foreach (var (key, value) in root)
            if (key != "schemaVersion" && key != "id")
                rebuilt[key] = value?.DeepClone();

        AtomicFile.WriteAllText(file, JsonSerializer.Serialize(rebuilt, SceneSerializer.NodeTypeInfo));
    }

    private static IEnumerable<string> EnumerateSorted(string root, string pattern)
        => Directory.GetFiles(root, pattern, SearchOption.AllDirectories)
                    .OrderBy(f => f, StringComparer.Ordinal);

    private static readonly string[] ForeignExtensions =
        { ".png", ".jpg", ".jpeg", ".wav", ".ogg", ".mp3", ".ttf" };

    internal static string HashFile(string path)
    {
        using var sha = System.Security.Cryptography.SHA1.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static (string id, bool changed, string? movedFrom) Reconcile(
        string currentId, string relPath, string contentRoot, HashSet<string> seen, AssetRegistry reg, ref int moved)
    {
        var id = currentId;
        bool changed = false;
        string? movedFrom = null;

        if (string.IsNullOrEmpty(id))
        {
            id = reg.GetId(relPath) ?? reg.NewId();
            changed = true;
        }

        if (!seen.Add(id))
        {
            Console.WriteLine($"[AssetScan] duplicate id, reissuing: {relPath}");
            id = reg.NewId();
            seen.Add(id);
            changed = true;
        }

        var known = reg.GetPath(id);
        if (known == null)
        {
            reg.Register(id, relPath);
        }
        else if (known != relPath)
        {
            if (File.Exists(Path.Combine(contentRoot, known)))
            {
                Console.WriteLine($"[AssetScan] copy detected, issuing a new id: {relPath} (keeping the original {known})");
                seen.Remove(id);
                id = reg.NewId();
                seen.Add(id);
                changed = true;
                reg.Register(id, relPath);
            }
            else
            {
                Console.WriteLine($"[AssetScan] move detected: {known} → {relPath}");
                reg.UpdatePath(id, relPath);
                movedFrom = known;
                moved++;
            }
        }

        return (id, changed, movedFrom);
    }

    private static string ToRelative(string root, string file)
    {
        var full = Path.GetFullPath(file).Replace('\\', '/');
        var rootFull = Path.GetFullPath(root).Replace('\\', '/').TrimEnd('/');
        return full.StartsWith(rootFull) ? full.Substring(rootFull.Length + 1) : full;
    }
}
