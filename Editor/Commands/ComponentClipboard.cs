using System;
using PixelCore.Runtime.Core;
using PixelCore.Runtime.Serialization;

namespace PixelCore.Editor.Commands;

public static class ComponentClipboard
{
    public static Type? Type { get; private set; }

    public static ComponentData? Data { get; private set; }

    public static bool HasValue => Data != null;

    public static string Label => Type?.Name ?? "";

    public static bool CanCopy(Component c) => ComponentDataRegistry.CreateFor(c) != null;

    public static bool Copy(Component c)
    {
        var data = ComponentDataRegistry.CreateFor(c);
        if (data == null) return false;

        data.CaptureInstance(c);
        Data = data;
        Type = c.GetType();
        return true;
    }

    public static bool CanPasteValues(Component c) => HasValue && Type == c.GetType();
}
