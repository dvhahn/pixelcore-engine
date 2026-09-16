using System;
using System.Runtime.InteropServices;
using System.Threading;
using SDL3;

namespace PixelCore.Editor;

public static unsafe class WindowChrome
{
    public static bool Unified { get; private set; }

    public static bool IsFullscreen => Volatile.Read(ref _fullscreen) == 1;

    private static IntPtr _sdlWindow;
    private static IntPtr _nsWindow;

    private static int _fullscreen;
    private static int _appliedFullscreen = -1;
    private static IntPtr _toolbar;
    private static bool _healLogged;
    private static SDL.SDL_EventFilter? _fsWatch;

    private const float DragThreshold = 4f;
    private static bool _pending;
    private static float _pressMouseX, _pressMouseY;
    private static bool _swallowBand;
    private static bool _sdlButtonStuck;

    private static bool OsLeftUp()
        => (SDL.SDL_GetGlobalMouseState(out _, out _) & SDL.SDL_MouseButtonFlags.SDL_BUTTON_LMASK) == 0;

    public static bool ShouldForceMouseUp()
    {
        if (!Unified || !_sdlButtonStuck) return false;
        if (!OsLeftUp()) { _sdlButtonStuck = false; return false; }
        return true;
    }

    public static void Install(IntPtr sdlWindow)
    {
        if (Unified || !OperatingSystem.IsMacOS()) return;

        uint props = SDL.SDL_GetWindowProperties(sdlWindow);
        IntPtr nsWindow = SDL.SDL_GetPointerProperty(
            props, SDL.SDL_PROP_WINDOW_COCOA_WINDOW_POINTER, IntPtr.Zero);
        if (nsWindow == IntPtr.Zero)
        {
            Console.WriteLine("[WindowChrome] no NSWindow handle - skipping the unified title bar");
            return;
        }

        _sdlWindow = sdlWindow;
        _nsWindow = nsWindow;

        ApplyUnifiedChrome();

        _toolbar = MsgSendRetPtr(MsgSendRetPtr(ObjcGetClass("NSToolbar"), "alloc"), "init");

        bool fs = (SDL.SDL_GetWindowFlags(sdlWindow) & SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) != 0;
        Volatile.Write(ref _fullscreen, fs ? 1 : 0);
        _appliedFullscreen = fs ? 1 : 0;
        if (!fs) AttachToolbar();

        _fsWatch = WatchFullscreen;
        SDL.SDL_AddEventWatch(_fsWatch, IntPtr.Zero);

        Unified = true;
        Console.WriteLine("[WindowChrome] unified title bar active (transparent title bar, native drag, system-setting double-click)");
    }

    public static void Pump()
    {
        if (!Unified || _nsWindow == IntPtr.Zero) return;

        int want = Volatile.Read(ref _fullscreen);
        if (want != _appliedFullscreen)
        {
            _appliedFullscreen = want;
            if (want == 1)
                MsgSendPtr(_nsWindow, "setToolbar:", IntPtr.Zero);
            else
            {
                ApplyUnifiedChrome();
                AttachToolbar();
            }
        }

        if (_appliedFullscreen == 0)
        {
            nuint style = MsgSendRetULong(_nsWindow, "styleMask");
            if ((style & FullSizeContentView) == 0)
            {
                ApplyUnifiedChrome();
                if (!_healLogged)
                {
                    _healLogged = true;
                    Console.WriteLine("[WindowChrome] FullSizeContentView lost after returning - reapplying");
                }
            }
        }
    }

    private const nuint FullSizeContentView = 1u << 15;

    private static void ApplyUnifiedChrome()
    {
        if (_nsWindow == IntPtr.Zero) return;
        MsgSendBool(_nsWindow, "setTitlebarAppearsTransparent:", true);
        nuint style = MsgSendRetULong(_nsWindow, "styleMask");
        MsgSendULong(_nsWindow, "setStyleMask:", style | FullSizeContentView);
        MsgSendLong(_nsWindow, "setTitleVisibility:", 1);
    }

    private static void AttachToolbar()
    {
        if (_toolbar == IntPtr.Zero) return;
        MsgSendPtr(_nsWindow, "setToolbar:", _toolbar);
        MsgSendLong(_nsWindow, "setToolbarStyle:", 4);
        MsgSendLong(_nsWindow, "setTitlebarSeparatorStyle:", 1);
    }

    private static bool WatchFullscreen(IntPtr userdata, SDL.SDL_Event* evt)
    {
        if (evt->type == (uint)SDL.SDL_EventType.SDL_EVENT_WINDOW_ENTER_FULLSCREEN)
            Volatile.Write(ref _fullscreen, 1);
        else if (evt->type == (uint)SDL.SDL_EventType.SDL_EVENT_WINDOW_LEAVE_FULLSCREEN)
            Volatile.Write(ref _fullscreen, 0);
        return true;
    }

    public static void TitleDoubleClick()
    {
        if (!Unified || _sdlWindow == IntPtr.Zero) return;
        switch (AppleActionOnDoubleClick())
        {
            case "Minimize": SDL.SDL_MinimizeWindow(_sdlWindow); break;
            case "None": break;
            default: ToggleZoom(); break;
        }
    }

    public static void ToggleZoom()
    {
        if ((SDL.SDL_GetWindowFlags(_sdlWindow) & SDL.SDL_WindowFlags.SDL_WINDOW_MAXIMIZED) != 0)
            SDL.SDL_RestoreWindow(_sdlWindow);
        else
            SDL.SDL_MaximizeWindow(_sdlWindow);
    }

    public static bool UpdateTitleDrag(bool inBand, bool doubleClicked, bool pressed, bool down)
    {
        if (!Unified || _nsWindow == IntPtr.Zero) return false;
        if (IsFullscreen) { _pending = false; return false; }

        if (_swallowBand)
        {
            _pending = false;
            if (OsLeftUp()) _swallowBand = false;
            return false;
        }

        if (inBand && doubleClicked) { _pending = false; TitleDoubleClick(); return false; }

        if (inBand && pressed)
        {
            SDL.SDL_GetGlobalMouseState(out _pressMouseX, out _pressMouseY);
            _pending = true;
        }

        if (_pending && down)
        {
            SDL.SDL_GetGlobalMouseState(out float gx, out float gy);
            if (Math.Abs(gx - _pressMouseX) + Math.Abs(gy - _pressMouseY) > DragThreshold)
            {
                _pending = false;
                StartNativeDrag();
                _sdlButtonStuck = true;
                _swallowBand = true;
                return true;
            }
        }

        if (!down) _pending = false;
        return false;
    }

    private static void StartNativeDrag()
    {
        var loc = MsgSendRetPoint(_nsWindow, "mouseLocationOutsideOfEventStream");
        nint winNum = MsgSendRetNInt(_nsWindow, "windowNumber");
        double uptime = MsgSendRetDouble(MsgSendRetPtr(ObjcGetClass("NSProcessInfo"), "processInfo"), "systemUptime");
        IntPtr evt = objc_msgSend_nsevent(ObjcGetClass("NSEvent"),
            SelRegisterName("mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:"),
            1, loc.x, loc.y, 0, uptime, winNum, IntPtr.Zero, 0, 1, 1.0f);
        if (evt != IntPtr.Zero)
            MsgSendPtr(_nsWindow, "performWindowDragWithEvent:", evt);
    }

    private static string AppleActionOnDoubleClick()
    {
        IntPtr defaults = MsgSendRetPtr(ObjcGetClass("NSUserDefaults"), "standardUserDefaults");
        IntPtr cKey = Marshal.StringToHGlobalAnsi("AppleActionOnDoubleClick");
        IntPtr key = MsgSendPtrArgRetPtr(ObjcGetClass("NSString"), "stringWithUTF8String:", cKey);
        IntPtr val = MsgSendPtrArgRetPtr(defaults, "stringForKey:", key);
        Marshal.FreeHGlobal(cKey);
        if (val == IntPtr.Zero) return "Maximize";
        return Marshal.PtrToStringUTF8(MsgSendRetPtr(val, "UTF8String")) ?? "Maximize";
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjcGetClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ret_ptr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_ptr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_bool(IntPtr receiver, IntPtr selector, bool arg);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nuint objc_msgSend_ret_nuint(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_nuint(IntPtr receiver, IntPtr selector, nuint arg);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_nint(IntPtr receiver, IntPtr selector, nint arg);

    [StructLayout(LayoutKind.Sequential)]
    private struct NSPoint { public double x; public double y; }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern NSPoint objc_msgSend_ret_point(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern double objc_msgSend_ret_double(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_ret_nint(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ptrarg_ret_ptr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_nsevent(IntPtr receiver, IntPtr selector,
        nuint type, double locX, double locY, nuint modifierFlags, double timestamp,
        nint windowNumber, IntPtr context, nint eventNumber, nint clickCount, float pressure);

    private static void MsgSendBool(IntPtr r, string sel, bool v) => objc_msgSend_bool(r, SelRegisterName(sel), v);
    private static IntPtr MsgSendRetPtr(IntPtr r, string sel) => objc_msgSend_ret_ptr(r, SelRegisterName(sel));
    private static void MsgSendPtr(IntPtr r, string sel, IntPtr v) => objc_msgSend_ptr(r, SelRegisterName(sel), v);
    private static nuint MsgSendRetULong(IntPtr r, string sel) => objc_msgSend_ret_nuint(r, SelRegisterName(sel));
    private static void MsgSendULong(IntPtr r, string sel, nuint v) => objc_msgSend_nuint(r, SelRegisterName(sel), v);
    private static void MsgSendLong(IntPtr r, string sel, nint v) => objc_msgSend_nint(r, SelRegisterName(sel), v);
    private static NSPoint MsgSendRetPoint(IntPtr r, string sel) => objc_msgSend_ret_point(r, SelRegisterName(sel));
    private static double MsgSendRetDouble(IntPtr r, string sel) => objc_msgSend_ret_double(r, SelRegisterName(sel));
    private static nint MsgSendRetNInt(IntPtr r, string sel) => objc_msgSend_ret_nint(r, SelRegisterName(sel));
    private static IntPtr MsgSendPtrArgRetPtr(IntPtr r, string sel, IntPtr a) => objc_msgSend_ptrarg_ret_ptr(r, SelRegisterName(sel), a);
}
