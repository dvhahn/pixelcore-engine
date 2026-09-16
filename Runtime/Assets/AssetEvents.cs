using System;

namespace PixelCore.Runtime.Assets;

public enum AssetChangeKind
{
    Saved,
    Renamed,
    Moved,
    Deleted,
    Imported,
    Rescanned,
}

public readonly record struct AssetChange(AssetChangeKind Kind, string? Id, string? OldPath, string? NewPath)
{
    public bool Touches(params string[] extensions)
    {
        foreach (var p in new[] { OldPath, NewPath })
        {
            if (string.IsNullOrEmpty(p)) continue;
            foreach (var ext in extensions)
                if (p.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public bool IsWholesale =>
        Kind == AssetChangeKind.Rescanned
        || (string.IsNullOrEmpty(System.IO.Path.GetExtension(NewPath ?? OldPath ?? "")));
}

public static class AssetEvents
{
    public static event Action<AssetChange>? Changed;

    public static void Raise(AssetChange change) => Changed?.Invoke(change);

    public static void RaiseSaved(string path, string? id = null)
        => Raise(new AssetChange(AssetChangeKind.Saved, id, null, path));

    public static void RaiseRenamed(string oldPath, string newPath, string? id = null)
        => Raise(new AssetChange(AssetChangeKind.Renamed, id, oldPath, newPath));

    public static void RaiseMoved(string oldPath, string newPath)
        => Raise(new AssetChange(AssetChangeKind.Moved, null, oldPath, newPath));

    public static void RaiseDeleted(string path)
        => Raise(new AssetChange(AssetChangeKind.Deleted, null, path, null));

    public static void RaiseImported(string path)
        => Raise(new AssetChange(AssetChangeKind.Imported, null, null, path));

    public static void RaiseRescanned()
        => Raise(new AssetChange(AssetChangeKind.Rescanned, null, null, null));
}
