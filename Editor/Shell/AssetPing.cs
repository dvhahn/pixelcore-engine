using System;
using System.Collections.Generic;
using System.IO;
using PixelCore.Runtime.Assets;

namespace PixelCore.Editor.Shell;

public static class AssetPing
{
    public static List<string> AncestorDirs(string relPath)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(relPath)) return result;
        var norm = relPath.Replace('\\', '/').Trim('/');
        int idx = -1;
        while ((idx = norm.IndexOf('/', idx + 1)) >= 0)
            result.Add(norm.Substring(0, idx));
        return result;
    }

    public static string? SpriteTarget(string? atlasPath, string? texturePath)
    {
        if (!string.IsNullOrEmpty(atlasPath)) return Path.ChangeExtension(atlasPath, ".png");
        return string.IsNullOrEmpty(texturePath) ? null : texturePath;
    }

    public static string? FromId(string? id)
    {
        if (string.IsNullOrEmpty(id) || !AssetRegistry.LooksLikeId(id)) return null;
        return AssetRegistry.Instance.GetPath(id);
    }

    public static float FlashAlpha(double now, double startedAt, double duration = 1.2)
    {
        if (duration <= 0) return 0f;
        double t = (now - startedAt) / duration;
        if (t < 0 || t >= 1) return 0f;
        double a = 1.0 - t;
        return a <= 1e-6 ? 0f : (float)a;
    }
}
