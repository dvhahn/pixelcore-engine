using System;
using System.Runtime.InteropServices;

namespace PixelCore.Runtime.Core;

public static class Input
{
    public const int SDL_SCANCODE_A = 4;
    public const int SDL_SCANCODE_D = 7;
    public const int SDL_SCANCODE_W = 26;
    public const int SDL_SCANCODE_S = 22;
    public const int SDL_SCANCODE_E = 8;
    public const int SDL_SCANCODE_SPACE = 44;
    public const int SDL_SCANCODE_LEFT = 80;
    public const int SDL_SCANCODE_RIGHT = 79;
    public const int SDL_SCANCODE_UP = 82;
    public const int SDL_SCANCODE_DOWN = 81;
    public const int SDL_SCANCODE_ESCAPE = 41;
    public const int SDL_SCANCODE_RETURN = 40;
    public const int SDL_SCANCODE_KP_ENTER = 88;
    public const int SDL_SCANCODE_LALT = 226;
    public const int SDL_SCANCODE_RALT = 230;
    public const int SDL_SCANCODE_Z = 29;
    public const int SDL_SCANCODE_Y = 28;
    public const int SDL_SCANCODE_O = 18;
    public const int SDL_SCANCODE_N = 17;
    public const int SDL_SCANCODE_P = 19;
    public const int SDL_SCANCODE_J = 13;
    public const int SDL_SCANCODE_C = 6;
    public const int SDL_SCANCODE_V = 25;
    public const int SDL_SCANCODE_F = 9;
    public const int SDL_SCANCODE_F2 = 59;
    public const int SDL_SCANCODE_F5 = 62;
    public const int SDL_SCANCODE_F8 = 65;
    public const int SDL_SCANCODE_DELETE = 76;
    public const int SDL_SCANCODE_BACKSPACE = 42;
    public const int SDL_SCANCODE_LCTRL = 224;
    public const int SDL_SCANCODE_RCTRL = 228;
    public const int SDL_SCANCODE_LSHIFT = 225;
    public const int SDL_SCANCODE_RSHIFT = 229;
    public const int SDL_SCANCODE_LGUI = 227;
    public const int SDL_SCANCODE_RGUI = 231;

    private static IntPtr SDL_GetKeyboardState(out int numkeys)
        => SDL3.SDL.SDL_GetKeyboardState(out numkeys);

    private static IntPtr _keyboardState;
    private static int _numKeys;
    private static byte[] _prevState = new byte[512];
    private static byte[] _currState = new byte[512];

    public static void Update()
    {
        Array.Copy(_currState, _prevState, _currState.Length);

        _keyboardState = SDL_GetKeyboardState(out _numKeys);

        if (_keyboardState != IntPtr.Zero && _numKeys > 0)
        {
            int copyLen = Math.Min(_numKeys, _currState.Length);
            Marshal.Copy(_keyboardState, _currState, 0, copyLen);
        }
    }

#if DEBUG

    public static void DebugTick() => Array.Copy(_currState, _prevState, _currState.Length);

    public static void DebugSetKey(int scancode, bool down)
    {
        if (scancode < 0 || scancode >= _currState.Length) return;
        _currState[scancode] = (byte)(down ? 1 : 0);
    }
#endif

    public static bool IsKeyDown(int scancode)
    {
        if (scancode < 0 || scancode >= _currState.Length) return false;
        return _currState[scancode] != 0;
    }

    public static bool IsKeyPressed(int scancode)
    {
        if (scancode < 0 || scancode >= _currState.Length) return false;
        return _currState[scancode] != 0 && _prevState[scancode] == 0;
    }

    public static bool IsKeyReleased(int scancode)
    {
        if (scancode < 0 || scancode >= _currState.Length) return false;
        return _currState[scancode] == 0 && _prevState[scancode] != 0;
    }
}
