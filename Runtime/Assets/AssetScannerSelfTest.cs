using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Runtime.Assets;

#if DEBUG

public static class AssetScannerSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== AssetScanner self-test (scene ids, reconciliation, orphan cleanup) ===");

        {
            string tmpReg = Path.Combine(Path.GetTempPath(),
                "pixelcore-normpath-" + Guid.NewGuid().ToString("N"), "assets.json");
            using var _iso = AssetRegistry.UseTemporary(tmpReg);
            var reg = AssetRegistry.Instance;

            string relWithPrefix = reg.GetOrCreateId("Content/Prefabs/Desk.scene");
            Check("* the Content/ prefix is stripped - the registry's convention is relative to Content",
                  reg.GetPath(relWithPrefix) == "Prefabs/Desk.scene",
                  reg.GetPath(relWithPrefix));

            string rel = reg.GetOrCreateId("Prefabs/Desk.scene");
            Check("* paths with and without the prefix join the same id (no double issuing)",
                  rel == relWithPrefix, $"{relWithPrefix} vs {rel}");
            Check("   control: so there is one entry (with two, which one a scene points to would diverge)",
                  reg.Entries.Count == 1, reg.Entries.Count.ToString());

            string abs = Path.Combine(Environment.CurrentDirectory, "Content", "Prefabs", "Desk.scene");
            string fromAbs = reg.GetOrCreateId(abs);
            Check("* an absolute path (what the editor really passes) also joins the same id",
                  fromAbs == relWithPrefix, $"{reg.GetPath(fromAbs)}");
        }

        {
            using var t = new TempContent();
            t.WriteScene("Scenes/Room.scene", id: null);
            t.Registry.Register("aaaa1111", "Scenes/Room.scene");

            t.Scan();

            Check("old scene: inherits the registry id rather than issuing a new one", t.ReadSceneId("Scenes/Room.scene") == "aaaa1111");
            Check("old scene: still one entry after inheriting", t.Registry.Entries.Count == 1);
        }

        {
            using var t = new TempContent();
            t.WriteScene("Scenes/Fresh.scene", id: null);

            t.Scan();

            var id = t.ReadSceneId("Scenes/Fresh.scene");
            Check("new scene: an id was issued", !string.IsNullOrEmpty(id));
            Check("new scene: registered under that id", t.Registry.GetPath(id!) == "Scenes/Fresh.scene");
        }

        {
            using var t = new TempContent();
            t.WriteScene("Scenes/Old.scene", id: "bbbb2222");
            t.Registry.Register("bbbb2222", "Scenes/Old.scene");

            t.Move("Scenes/Old.scene", "Prefabs/New.scene");
            t.Scan();

            Check("move: the id survives, so references hold", t.ReadSceneId("Prefabs/New.scene") == "bbbb2222");
            Check("move: the registry path follows to the new location", t.Registry.GetPath("bbbb2222") == "Prefabs/New.scene");
            Check("move: no extra entry (no duplicate ids)", t.Registry.Entries.Count == 1);
        }

        {
            using var t = new TempContent();
            t.WriteScene("Scenes/Keep.scene", id: "cccc3333");
            t.Registry.Register("cccc3333", "Scenes/Keep.scene");
            t.Registry.Register("dddd4444", "Scenes/Deleted.scene");

            t.Scan();

            Check("orphan cleanup: a vanished scene entry is removed", t.Registry.GetPath("dddd4444") == null);
            Check("orphan cleanup: a live scene is kept", t.Registry.GetPath("cccc3333") == "Scenes/Keep.scene");
        }

        foreach (var (originalName, copyName) in new[] { ("A_orig", "Z_copy"), ("Z_orig", "A_copy") })
        {
            using var t = new TempContent();
            t.WriteScene($"Scenes/{originalName}.scene", id: "eeee5555");
            t.WriteScene($"Scenes/{copyName}.scene", id: "eeee5555");
            t.Registry.Register("eeee5555", $"Scenes/{originalName}.scene");

            t.Scan();

            var orig = t.ReadSceneId($"Scenes/{originalName}.scene");
            var copy = t.ReadSceneId($"Scenes/{copyName}.scene");
            Check($"copy ({originalName} to {copyName}): the original keeps its id", orig == "eeee5555");
            Check($"copy ({originalName} to {copyName}): the copy gets a new id", copy != "eeee5555" && !string.IsNullOrEmpty(copy));
            Check($"copy ({originalName} to {copyName}): the registry points at the original too",
                t.Registry.GetPath("eeee5555") == $"Scenes/{originalName}.scene"
                && t.Registry.GetPath(copy!) == $"Scenes/{copyName}.scene");
        }

        {
            using var t = new TempContent();
            t.WriteScene("Scenes/Gone.scene", id: "77778888");
            t.Registry.Register("77778888", "Scenes/Elsewhere.scene");

            t.Scan();

            Check("move against copy: with no file at the old path it is treated as a move",
                t.ReadSceneId("Scenes/Gone.scene") == "77778888"
                && t.Registry.GetPath("77778888") == "Scenes/Gone.scene");
        }

        {
            using var t = new TempContent();
            t.WriteScene("Scenes/Legacy.scene", id: null, mutate: d => d.Name = "Legacy Room");
            t.StripKeys("Scenes/Legacy.scene",
                "id",
                "viewZoom", "cameraFixed", "cameraFixedX", "cameraFixedY", "exterior", "post");

            var before = t.ReadRaw("Scenes/Legacy.scene");
            t.Scan();
            var after = t.ReadRaw("Scenes/Legacy.scene");

            var withoutId = string.Join('\n',
                after.Split('\n').Where(l => !l.TrimStart().StartsWith("\"id\":")));

            Check("promotion: exactly one line was added, the id",
                after.Split('\n').Length == before.Split('\n').Length + 1);
            Check("promotion: absent fields are not materialized at their defaults (no scene rewrite)",
                !after.Contains("viewZoom") && !after.Contains("cameraFixed") && !after.Contains("\"post\""));
            Check("promotion: the rest of the content is byte-identical", withoutId == before);
            Check("promotion: the id sits immediately after the schema version (the header slot)",
                after.IndexOf("\"id\"", StringComparison.Ordinal) > after.IndexOf("schemaVersion", StringComparison.Ordinal)
                && after.IndexOf("\"id\"", StringComparison.Ordinal) < after.IndexOf("\"name\"", StringComparison.Ordinal));
        }

        {
            using var t = new TempContent();
            t.WriteScene("Scenes/HouseCopy.scene", id: null, mutate: d =>
            {
                d.Name = "House Copy";
                d.Entities.Add(new EntityData { Id = 1, Name = "Anchor_Bed" });
            });

            t.StripKeys("Scenes/HouseCopy.scene", "id");

            var before = t.ReadRaw("Scenes/HouseCopy.scene");
            Check("before promotion: non-ASCII is unescaped (the SaveToFile path)", before.Contains("Anchor_Bed"));

            t.Scan();

            var after = t.ReadRaw("Scenes/HouseCopy.scene");
            Check("after promotion: non-ASCII is still unescaped", after.Contains("Anchor_Bed") && after.Contains("House Copy"));
            Check("after promotion: no unicode escapes", !after.Contains("\\u"));
            Check("after promotion: exactly one added line, so the contract holds for non-ASCII scenes too",
                after.Split('\n').Length == before.Split('\n').Length + 1);
        }

        {
            using var t = new TempContent();
            t.WriteScene("Scenes/Host.scene", id: "ffff6666");

            SceneSerializer.SaveToFile(new SceneData { Name = "Host" }, t.Full("Scenes/Host.scene"));

            Check("save: the id stamped in the file is preserved, so an editor save does not break references",
                t.ReadSceneId("Scenes/Host.scene") == "ffff6666");

            SceneSerializer.SaveToFile(new SceneData { Name = "Copy" }, t.Full("Scenes/Copy.scene"));
            Check("save as: it does not inherit the original id",
                string.IsNullOrEmpty(t.ReadSceneId("Scenes/Copy.scene")));
        }

        {
            using var t = new TempContent();
            var ids = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 200; i++)
            {
                var id = t.Registry.NewId();
                ids.Add(id);
                t.Registry.Register(id, $"Scenes/Gen{i}.scene");
            }
            Check("issue: eight characters long", ids.All(id => id.Length == AssetRegistry.IdLength));
            Check("issue: all 200 are unique", ids.Count == 200);

            var occupied = ids.First();
            int calls = 0;
            var rigged = t.Registry.NewId(() =>
            {
                calls++;
                return calls <= 2 ? occupied : "freeeeee";
            });
            Check("issue: an id already in the registry is skipped and redrawn", rigged == "freeeeee");
            Check("issue: redraws once per taken candidate (the third after two)", calls == 3);
        }

        {
            var px = TextureLoader.BuildPlaceholderPixels(TextureLoader.PlaceholderSize);
            int n = TextureLoader.PlaceholderSize;
            var magenta = new Microsoft.Xna.Framework.Color(255, 0, 255);

            Check("placeholder: pixel count equals size squared", px.Length == n * n);
            Check("placeholder: the top left is magenta", px[0] == magenta);
            Check("placeholder: a checker, so adjacent cells differ",
                px[0] != px[n / 4] && px[0] != px[(n / 4) * n]);
            Check("placeholder: diagonal cells match (the checker rule)",
                px[0] == px[(n / 4) * n + n / 4]);
            Check("placeholder: only two colours", px.Distinct().Count() == 2);

            var loader = TextureLoader.Instance;
            var path = "Sprites/__selftest_missing__.png";
            Check("missing log: a path seen for the first time is reported", loader.NoteMissing(path));
            Check("missing log: quiet from the second time on", !loader.NoteMissing(path));
            Check("missing log: a different path counts separately", loader.NoteMissing(path + "2"));

            Check("latch release: recovery forgets it", loader.ForgetMissing(path));
            Check("latch release: a second disappearance complains again", loader.NoteMissing(path));
            Check("latch release: a path never noted returns false", !loader.ForgetMissing(path + "3"));
        }

        {
            using var t = new TempContent();
            t.WriteAtlas("Sprites/New/Sheet.atlas", "aaaa7777");
            t.Registry.Register("aaaa7777", "Sprites/New/Sheet.atlas");
            t.WriteAnim("Animations/Walk.anim", id: "bbbb7777", atlasId: "aaaa7777",
                        atlasPath: "Sprites/OldFolder/Sheet.png");

            t.Scan();

            var anim = Animation.AnimationData.Load(t.Full("Animations/Walk.anim"));
            Check("* with a live AtlasId, AtlasPath is corrected to the current path",
                  anim?.AtlasPath == "Sprites/New/Sheet.png", anim?.AtlasPath);
            Check("the convention is the png path, not the .atlas (the editor derives one from the other)",
                  anim?.AtlasPath?.EndsWith(".png") == true, anim?.AtlasPath);
        }
        {
            using var t = new TempContent();
            t.WriteAnim("Animations/Orphan.anim", id: "cccc7777", atlasId: "missingId",
                        atlasPath: "Sprites/OldFolder/Sheet.png");

            t.Scan();

            var anim = Animation.AnimationData.Load(t.Full("Animations/Orphan.anim"));
            Check("* an unresolvable id keeps the old path, since erasing it destroys the clue",
                  anim?.AtlasPath == "Sprites/OldFolder/Sheet.png", anim?.AtlasPath);
        }

        {
            using var t = new TempContent();
            var reg = t.Registry;
            reg.Register("cccc3333", "c.png");
            reg.Register("aaaa1111", "a.png");
            reg.Register("eeee5555", "e.png");
            reg.Remove("aaaa1111");
            reg.Register("bbbb2222", "b.png");
            reg.Save();

            var order = ReadKeyOrder(t.Full("assets.json"));
            Check($"premise: three entries were written ({order.Count})", order.Count == 3);
            Check("* assets.json is written in key order, even after a remove and re-add",
                  order.SequenceEqual(order.OrderBy(k => k, StringComparer.Ordinal)),
                  string.Join(",", order));
        }

        {
            var real = Path.Combine(ContentPaths.Root, "assets.json");
            Check("premise: the real assets.json exists", File.Exists(real));
            if (File.Exists(real))
            {
                var order = ReadKeyOrder(real);
                Check($"* the committed assets.json is in key order too ({order.Count})",
                      order.SequenceEqual(order.OrderBy(k => k, StringComparer.Ordinal)));
            }
        }

        {
            Check("premise: the self-test working directory is the project root (the two checks below rely on it)",
                  Directory.Exists("Runtime") && Directory.Exists("Editor"));

            Check($"* ContentPaths.Root ('{ContentPaths.Root}') really reaches Content",
                  Directory.Exists(Path.Combine(ContentPaths.Root, "Scenes")));

            const string needle = "/User" + "s/";
            var offenders = new System.Collections.Generic.List<string>();
            foreach (var dir in new[] { "Runtime", "Editor", "Gameplay" })
                foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                {
                    var lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                        if (lines[i].Contains(needle)) offenders.Add($"{file}:{i + 1}");
                }
            foreach (var file in Directory.GetFiles(".", "*.cs").OrderBy(f => f, StringComparer.Ordinal))
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (lines[i].Contains(needle)) offenders.Add($"{file}:{i + 1}");
            }
            Check($"* no home-directory absolute path in the source ({offenders.Count})",
                  offenders.Count == 0, string.Join(" / ", offenders));
        }

        TestTextureLoaderHeadless();

        Console.WriteLine($"=== AssetScanner: {_pass} passed, {_fail} failed ===");
    }

    private sealed class TempContent : IDisposable
    {
        private readonly string _root;
        private readonly IDisposable _scope;

        public TempContent()
        {
            _root = Path.Combine(Path.GetTempPath(), "pixelcore_scan_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_root);
            _scope = AssetRegistry.UseTemporary(Path.Combine(_root, "assets.json"));
        }

        public AssetRegistry Registry => AssetRegistry.Instance;
        public string Full(string rel) => Path.Combine(_root, rel);

        public void WriteScene(string rel, string? id, Action<SceneData>? mutate = null)
        {
            var full = Full(rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            var data = new SceneData { Name = Path.GetFileNameWithoutExtension(rel) };
            if (id != null) data.Id = id;
            mutate?.Invoke(data);
            SceneSerializer.SaveToFile(data, full);
        }

        public void WriteAtlas(string rel, string id)
        {
            var full = Full(rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, $"{{\"Id\": \"{id}\", \"TexturePath\": \"Sheet.png\", \"Slices\": []}}");
        }

        public void WriteAnim(string rel, string id, string atlasId, string atlasPath)
        {
            var full = Full(rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full,
                $"{{\"Id\": \"{id}\", \"Name\": \"{Path.GetFileNameWithoutExtension(rel)}\", " +
                $"\"AtlasId\": \"{atlasId}\", \"AtlasPath\": \"{atlasPath}\", \"Frames\": [], \"Loop\": true}}");
        }

        public void Move(string fromRel, string toRel)
        {
            var to = Full(toRel);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Move(Full(fromRel), to);
        }

        public string? ReadSceneId(string rel) => SceneSerializer.LoadFromFile(Full(rel))?.Id;

        public string ReadRaw(string rel) => File.ReadAllText(Full(rel));

        public void StripKeys(string rel, params string[] keys)
        {
            var full = Full(rel);
            var root = (System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(full)) as System.Text.Json.Nodes.JsonObject)!;
            foreach (var k in keys) root.Remove(k);
            File.WriteAllText(full, System.Text.Json.JsonSerializer.Serialize(root, SceneSerializer.NodeTypeInfo));
        }

        public void Scan() => AssetScanner.Scan(_root, promote: true);

        public void Dispose()
        {
            _scope.Dispose();
            try { Directory.Delete(_root, true); } catch {  }
        }
    }

    private static string Normalize(string json) =>
        string.Concat(json.Where(c => !char.IsWhiteSpace(c)));

    private static System.Collections.Generic.List<string> ReadKeyOrder(string path)
    {
        var keys = new System.Collections.Generic.List<string>();
        foreach (var line in File.ReadAllLines(path))
        {
            var t = line.TrimStart();
            if (!t.StartsWith("\"")) continue;
            int end = t.IndexOf('"', 1);
            if (end < 0) continue;
            if (line.Length - t.Length != 2) continue;
            keys.Add(t.Substring(1, end - 1));
        }
        return keys;
    }

    private static void TestTextureLoaderHeadless()
    {
        bool saved = TextureLoader.Headless;
        var prev = Console.Out;
        try
        {
            TextureLoader.Headless = true;
            ResetWarnLatch();
            var quiet = new StringWriter();
            Console.SetOut(quiet);
            for (int i = 0; i < 5; i++) TextureLoader.Instance.Load($"missingFile_{i}.png");
            Console.SetOut(prev);
            Check("1. declaring headless produces no initialization warning", quiet.ToString().Length == 0,
                quiet.ToString());

            TextureLoader.Headless = false;
            ResetWarnLatch();
            var loud = new StringWriter();
            Console.SetOut(loud);
            for (int i = 0; i < 5; i++) TextureLoader.Instance.Load($"missingFile_{i}.png");
            Console.SetOut(prev);
            int lines = loud.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            Check("* 1. control: without the declaration the warning appears", lines >= 1, $"{lines} line(s)");
            Check("* 1. the latch: five calls complain once, so the console does not lock up", lines == 1,
                $"{lines} line(s)");
        }
        finally
        {
            Console.SetOut(prev);
            TextureLoader.Headless = saved;
            ResetWarnLatch();
        }
    }

    private static void ResetWarnLatch()
    {
        typeof(TextureLoader)
            .GetField("_warnedUninitialized", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, false);
    }

    private static void Check(string name, bool ok, string? detail = null)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name + (!ok && !string.IsNullOrEmpty(detail) ? $" — {detail}" : ""));
        if (ok) _pass++; else _fail++;
    }
}

#endif
