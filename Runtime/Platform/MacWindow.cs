using System;
using System.Runtime.InteropServices;

namespace PixelCore.Runtime.Platform;

public static class MacWindow
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSend(IntPtr receiver, IntPtr sel, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSendStr(IntPtr receiver, IntPtr sel, string arg);

    public static void ApplyDarkTitlebar(IntPtr sdlWindow)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            uint props = SDL3.SDL.SDL_GetWindowProperties(sdlWindow);
            IntPtr nsWindow = SDL3.SDL.SDL_GetPointerProperty(
                props, SDL3.SDL.SDL_PROP_WINDOW_COCOA_WINDOW_POINTER, IntPtr.Zero);
            if (nsWindow == IntPtr.Zero) return;

            IntPtr name = MsgSendStr(objc_getClass("NSString"),
                sel_registerName("stringWithUTF8String:"), "NSAppearanceNameDarkAqua");
            IntPtr appearance = MsgSend(objc_getClass("NSAppearance"),
                sel_registerName("appearanceNamed:"), name);
            if (appearance != IntPtr.Zero)
                MsgSend(nsWindow, sel_registerName("setAppearance:"), appearance);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MacWindow] dark titlebar skip: {ex.Message}");
        }
    }
}
