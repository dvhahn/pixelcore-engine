using System;
using Microsoft.Xna.Framework.Input;

namespace PixelCore.Runtime.Core;

public static class Gamepad
{
    public const float EnterThreshold = 0.5f;

    public const float ExitThreshold = 0.35f;

    public enum Pad
    {
        Left, Right, Up, Down,
        A, B, X, Y,
        Start, Back,
        LeftShoulder, RightShoulder,
    }

    private const int Count = 12;
    private static readonly bool[] _prev = new bool[Count];
    private static readonly bool[] _curr = new bool[Count];

    public static bool IsConnected { get; private set; }

    public static void Update()
    {
        Array.Copy(_curr, _prev, Count);

#if DEBUG
        if (DebugInjected) { IsConnected = true; return; }
#endif
        var s = GamePad.GetState(Microsoft.Xna.Framework.PlayerIndex.One);
        IsConnected = s.IsConnected;
        if (!IsConnected) { Array.Clear(_curr, 0, Count); return; }

        var stick = s.ThumbSticks.Left;
        SetAxis(Pad.Left,  -stick.X); SetAxis(Pad.Right, stick.X);
        SetAxis(Pad.Down,  -stick.Y); SetAxis(Pad.Up,    stick.Y);
        if (s.DPad.Left  == ButtonState.Pressed) _curr[(int)Pad.Left]  = true;
        if (s.DPad.Right == ButtonState.Pressed) _curr[(int)Pad.Right] = true;
        if (s.DPad.Up    == ButtonState.Pressed) _curr[(int)Pad.Up]    = true;
        if (s.DPad.Down  == ButtonState.Pressed) _curr[(int)Pad.Down]  = true;

        _curr[(int)Pad.A] = s.Buttons.A == ButtonState.Pressed;
        _curr[(int)Pad.B] = s.Buttons.B == ButtonState.Pressed;
        _curr[(int)Pad.X] = s.Buttons.X == ButtonState.Pressed;
        _curr[(int)Pad.Y] = s.Buttons.Y == ButtonState.Pressed;
        _curr[(int)Pad.Start] = s.Buttons.Start == ButtonState.Pressed;
        _curr[(int)Pad.Back]  = s.Buttons.Back  == ButtonState.Pressed;
        _curr[(int)Pad.LeftShoulder]  = s.Buttons.LeftShoulder  == ButtonState.Pressed;
        _curr[(int)Pad.RightShoulder] = s.Buttons.RightShoulder == ButtonState.Pressed;
    }

    private static void SetAxis(Pad dir, float v)
    {
        int i = (int)dir;
        _curr[i] = _prev[i] ? v > ExitThreshold : v > EnterThreshold;
    }

    public static bool IsDown(Pad b) => _curr[(int)b];
    public static bool IsPressed(Pad b) => _curr[(int)b] && !_prev[(int)b];
    public static bool IsReleased(Pad b) => !_curr[(int)b] && _prev[(int)b];

#if DEBUG

    public static bool DebugInjected;

    public static void DebugTick() => Array.Copy(_curr, _prev, Count);

    public static void DebugSet(Pad b, bool down) => _curr[(int)b] = down;

    public static void DebugSetAxis(Pad dir, float v) => SetAxis(dir, v);

    public static void DebugReset()
    {
        Array.Clear(_curr, 0, Count); Array.Clear(_prev, 0, Count);
        DebugInjected = false; IsConnected = false;
    }
#endif
}
