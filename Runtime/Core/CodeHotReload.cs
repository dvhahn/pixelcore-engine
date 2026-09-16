#if DEBUG
using System;
using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(PixelCore.Runtime.Core.CodeHotReload))]

namespace PixelCore.Runtime.Core;

public static class CodeHotReload
{
    private static volatile bool _pending;

    public static event Action? Applied;

    public static int AppliedCount { get; private set; }

    internal static void ClearCache(Type[]? updatedTypes) { }

    internal static void UpdateApplication(Type[]? updatedTypes) => _pending = true;

    public static void Update()
    {
        if (!_pending) return;
        _pending = false;
        AppliedCount++;
        Console.WriteLine("[HotReload] code reapplied");
        Applied?.Invoke();
    }
}
#endif
