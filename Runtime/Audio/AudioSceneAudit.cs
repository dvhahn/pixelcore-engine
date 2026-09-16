using System;
using System.Collections.Generic;
using System.IO;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Runtime.Audio;

public static class AudioSceneAudit
{
    private const string PlayerName = Core.Scene.PlayerName;

    public static void Run(string scenePath)
    {
        if (!File.Exists(scenePath))
        {
            Console.WriteLine($"[AUDIT] scene not found: {scenePath}");
            return;
        }

        var data = SceneSerializer.LoadFromFile(scenePath);
        if (data == null) { Console.WriteLine($"[AUDIT] failed to parse scene: {scenePath}"); return; }

        var am = AudioManager.Instance;
        Console.WriteLine($"=== audio wiring audit: {data.Name} ({Path.GetFileName(scenePath)}) ===");

        var beds = data.Ambients;
        Console.WriteLine($"\n[beds] {(beds?.Count ?? 0)} ambient layers - no position, the same level anywhere in the room");
        if (beds != null)
            foreach (var b in beds)
            {
                var p = Assets.AssetRegistry.Instance.GetPath(b.SoundId);
                Console.WriteLine($"   {Mark(am, p)} {Label(b.SoundId, p),-40} volume {b.Volume:0.00}");
            }

        var player = FindPos(data, PlayerName);
        if (player == null)
            Console.WriteLine($"\n⚠ no '{PlayerName}' entity, so distance calculation is skipped");
        else
            Console.WriteLine($"\n[point sources] reference = {PlayerName} ({player.Value.X:0.#}, {player.Value.Y:0.#}) - the listener follows the player");

        int found = 0;
        foreach (var e in data.Entities)
        {
            SoundEmitterData? se = null;
            TransformData? tr = null;
            foreach (var c in e.Components)
            {
                if (c is SoundEmitterData s) se = s;
                else if (c is TransformData t) tr = t;
            }
            if (se == null) continue;
            found++;

            var sePath = Assets.AssetRegistry.Instance.GetPath(se.SoundId);
            string ok = Mark(am, sePath);
            if (tr == null) { Console.WriteLine($"   {ok} {e.Name,-18} ⚠ no Transform - the component stops itself every frame"); continue; }

            Console.Write($"   {ok} {e.Name,-18} {Label(se.SoundId, sePath),-40} ({tr.X:0.#}, {tr.Y:0.#})  "
                        + $"radius {se.Radius:0}  volume {se.Volume:0.00}  [{se.Bus}]");

            if (player == null) { Console.WriteLine(); continue; }

            float dx = tr.X - player.Value.X, dy = tr.Y - player.Value.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            float att = Components.SoundEmitter.Attenuation(dist, se.Radius);
            float gain = att * Math.Clamp(se.Volume, 0f, 1f);

            string verdict = dist >= se.Radius
                ? "x out of range - it never sounds at all"
                : gain < 0.05f
                    ? "~ it sounds, but is barely audible"
                    : "✓";
            Console.WriteLine($"\n        distance {dist:0.#}px -> attenuation {att:0.00} x volume = {gain:0.000}  {verdict}");

            var file = ResolveFile(am, sePath);
            if (file != null && AudioFileInfo.IsStereo(file))
                Console.WriteLine($"        ⚠ stereo file - a point source has to be mono "
                                + "(re-export it as mono)");
        }

        if (found == 0) Console.WriteLine("   (there is no SoundEmitter at all)");

        float bedTotal = 0f;
        if (beds != null) foreach (var b in beds) bedTotal += b.Volume;
        if (bedTotal > 0f && found > 0)
            Console.WriteLine($"\n[note] bed total {bedTotal:0.00} - a point source gain far below this "
                            + "is buried even while playing. Compare with the volumes in the debug overlay tracked list.");

        Console.WriteLine();
    }

    private static string Label(string? soundId, string? path)
        => string.IsNullOrEmpty(soundId) ? "(empty)"
         : string.IsNullOrEmpty(path) ? $"✘not-in-registry {soundId}"
         : path;

    private static string? ResolveFile(AudioManager am, string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var ogg = am.ResolveOggPath(path);
        if (ogg != null) return ogg;

        var full = Path.Combine(am.ContentPath, path);
        if (!full.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) full += ".wav";
        return File.Exists(full) ? full : null;
    }

    private static string Mark(AudioManager am, string? path) => ResolveFile(am, path) != null ? "●" : "✗";

    private static (float X, float Y)? FindPos(SceneData data, string name)
    {
        foreach (var e in data.Entities)
        {
            if (e.Name != name) continue;
            foreach (var c in e.Components)
                if (c is TransformData t) return (t.X, t.Y);
        }
        return null;
    }
}
