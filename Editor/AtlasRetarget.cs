using System;
using System.Collections.Generic;
using System.IO;
using PixelCore.Runtime.Animation;

namespace PixelCore.Editor;

internal static class AtlasRetarget
{
    internal static List<string> FindClipsUsing(string contentRoot, string atlasId, string? exceptPath)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(atlasId) || !Directory.Exists(contentRoot)) return result;

        string? except = string.IsNullOrEmpty(exceptPath) ? null : Path.GetFullPath(exceptPath);

        foreach (var f in Directory.GetFiles(contentRoot, "*.anim", SearchOption.AllDirectories))
        {
            if (except != null && string.Equals(Path.GetFullPath(f), except, StringComparison.Ordinal))
                continue;
            var data = AnimationData.Load(f);
            if (data != null && data.AtlasId == atlasId) result.Add(f);
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    internal static int Retarget(IReadOnlyList<string> files, string newAtlasId, string newAtlasPath)
    {
        int changed = 0;
        foreach (var f in files)
        {
            var data = AnimationData.Load(f);
            if (data == null)
            {
                Console.WriteLine($"[AtlasRetarget] ⚠ skipped (read failed): {f}");
                continue;
            }

            if (data.AtlasId == newAtlasId && data.AtlasPath == newAtlasPath) continue;

            data.AtlasId = newAtlasId;
            data.AtlasPath = newAtlasPath;
            try
            {
                data.Save(f);
                changed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AtlasRetarget] ⚠ write failed: {f} - {ex.Message}");
            }
        }
        return changed;
    }
}
