using System;
using System.IO;
using PixelCore.Editor.Shell;
using PixelCore.Runtime.Assets;

namespace PixelCore.Editor;

public static class AssetPingSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== AssetPing (ping calculation) self-test ===");

        var dirs = AssetPing.AncestorDirs("Sprites/Environments/Village/Houses/House (1).png");
        Check("* four ancestor folders, top down", dirs.Count == 4
            && dirs[0] == "Sprites" && dirs[1] == "Sprites/Environments"
            && dirs[2] == "Sprites/Environments/Village"
            && dirs[3] == "Sprites/Environments/Village/Houses");
        Check("a file in the root has zero ancestors (the file name does not leak in as a folder)", AssetPing.AncestorDirs("assets.json").Count == 0);
        Check("backslashes and leading/trailing slashes are normalised", AssetPing.AncestorDirs("\\Audio\\AMB\\rain.ogg")
            is { Count: 2 } d2 && d2[0] == "Audio" && d2[1] == "Audio/AMB");
        Check("an empty path gives an empty list (no exception)", AssetPing.AncestorDirs("").Count == 0);

        Check("* an atlas slice resolves to the sibling png (.atlas is not in the explorer)",
            AssetPing.SpriteTarget("Sprites/Characters/Hero/Walk.atlas", null) == "Sprites/Characters/Hero/Walk.png");
        Check("a whole png stays as it is", AssetPing.SpriteTarget(null, "Sprites/Environments/Village/Wall.png")
            == "Sprites/Environments/Village/Wall.png");
        Check("with both present the atlas wins (in slice mode TexturePath is a leftover)",
            AssetPing.SpriteTarget("A/B.atlas", "C/D.png") == "A/B.png");
        Check("no image gives null (ping disabled)", AssetPing.SpriteTarget(null, null) == null);

        var root = Path.Combine(Path.GetTempPath(), "pixelcore_ping_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            using var _ = AssetRegistry.UseTemporary(Path.Combine(root, "assets.json"));
            var id = AssetRegistry.Instance.GetOrCreateId("Audio/SFX/door.ogg");
            Check("* a registered id resolves to a path", AssetPing.FromId(id) == "Audio/SFX/door.ogg");
            Check("an id not in the registry gives null (a missing file is never flashed)", AssetPing.FromId("aaaa0001") == null);
            Check("control: a non-id value (a stale name or path) gives null - it never guesses",
                AssetPing.FromId("Scenes/Village.scene") == null && AssetPing.FromId("Old_Street") == null);
            Check("an empty value gives null", AssetPing.FromId(null) == null && AssetPing.FromId("") == null);
        }
        finally { try { Directory.Delete(root, true); } catch { } }

        Check("alpha is 1 at the moment it lights up", AssetPing.FlashAlpha(10.0, 10.0, 1.2) == 1f);
        Check("halfway through it is 0.5", Math.Abs(AssetPing.FlashAlpha(10.6, 10.0, 1.2) - 0.5f) < 1e-4f);
        Check("it is 0 when finished, and stays 0", AssetPing.FlashAlpha(11.2, 10.0, 1.2) == 0f && AssetPing.FlashAlpha(99, 10.0, 1.2) == 0f);
        Check("before the start, and with a duration of 0, it is 0 (no division accident)", AssetPing.FlashAlpha(9.0, 10.0, 1.2) == 0f && AssetPing.FlashAlpha(10.0, 10.0, 0) == 0f);

        Console.WriteLine($"=== {_pass} passed, {_fail} failed ===");
    }

    private static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name);
        if (ok) _pass++; else _fail++;
    }
}
