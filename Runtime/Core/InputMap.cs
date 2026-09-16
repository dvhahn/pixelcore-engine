using System;
using System.Collections.Generic;

namespace PixelCore.Runtime.Core;

public enum GameAction
{
    MoveLeft,
    MoveRight,
    MoveUp,
    MoveDown,

    Interact,

    Confirm,

    Skip,

    Pause,

    Attack,
}

public static class InputMap
{
    private static readonly (GameAction Action, (int Key, string Label)[] Keys, Gamepad.Pad[] Pad)[] Bindings =
    {
        (GameAction.MoveLeft,  new[] { (Input.SDL_SCANCODE_A, "A"), (Input.SDL_SCANCODE_LEFT,  "←") }, new[] { Gamepad.Pad.Left }),
        (GameAction.MoveRight, new[] { (Input.SDL_SCANCODE_D, "D"), (Input.SDL_SCANCODE_RIGHT, "→") }, new[] { Gamepad.Pad.Right }),
        (GameAction.MoveUp,    new[] { (Input.SDL_SCANCODE_W, "W"), (Input.SDL_SCANCODE_UP,    "↑") }, new[] { Gamepad.Pad.Up }),
        (GameAction.MoveDown,  new[] { (Input.SDL_SCANCODE_S, "S"), (Input.SDL_SCANCODE_DOWN,  "↓") }, new[] { Gamepad.Pad.Down }),
        (GameAction.Interact,  new[] { (Input.SDL_SCANCODE_E, "E") }, new[] { Gamepad.Pad.A }),
        (GameAction.Confirm,   new[] { (Input.SDL_SCANCODE_E, "E") }, new[] { Gamepad.Pad.A }),
        (GameAction.Skip,      new[] { (Input.SDL_SCANCODE_SPACE, "Space") }, new[] { Gamepad.Pad.B }),
        (GameAction.Pause,     new[] { (Input.SDL_SCANCODE_ESCAPE, "Esc") }, new[] { Gamepad.Pad.Start, Gamepad.Pad.B }),
        (GameAction.Attack,    new[] { (Input.SDL_SCANCODE_J, "J"), (Input.SDL_SCANCODE_SPACE, "Space") }, new[] { Gamepad.Pad.X }),
    };

    static InputMap() => Validate();

    public static void Validate()
    {
        var actions = Enum.GetValues<GameAction>();
        if (Bindings.Length != actions.Length)
            throw new InvalidOperationException(
                $"InputMap: {actions.Length} actions but {Bindings.Length} binding rows - a row is missing from the table.");

        for (int i = 0; i < Bindings.Length; i++)
        {
            if (Bindings[i].Action != (GameAction)i)
                throw new InvalidOperationException(
                    $"InputMap: row {i} is {Bindings[i].Action} - out of step with the enum order (was an action inserted in the middle?).");
            if (Bindings[i].Keys.Length == 0)
                throw new InvalidOperationException($"InputMap: the key binding for {Bindings[i].Action} is empty.");
            if (Bindings[i].Pad.Length == 0)
                throw new InvalidOperationException(
                    $"InputMap: the gamepad binding for {Bindings[i].Action} is empty - no button was put in the table.");
        }
    }

    private static (int Key, string Label)[] BindingsOf(GameAction action) => RowOf(action).Keys;

    private static (GameAction Action, (int Key, string Label)[] Keys, Gamepad.Pad[] Pad) RowOf(GameAction action)
    {
        int i = (int)action;
        if ((uint)i >= (uint)Bindings.Length)
            throw new InvalidOperationException(
                $"InputMap: {action} has no row in the table - the action was added without a Bindings row.");
        return Bindings[i];
    }

    public static bool IsDown(GameAction action)
    {
        var row = RowOf(action);
        foreach (var (key, _) in row.Keys)
            if (Input.IsKeyDown(key)) return true;
        foreach (var b in row.Pad)
            if (Gamepad.IsDown(b)) return true;
        return false;
    }

    public static bool IsPressed(GameAction action)
    {
        var row = RowOf(action);
        foreach (var (key, _) in row.Keys)
            if (Input.IsKeyPressed(key)) return true;
        foreach (var b in row.Pad)
            if (Gamepad.IsPressed(b)) return true;
        return false;
    }

    public static string KeyLabel(GameAction action) => BindingsOf(action)[0].Label;

    public static IReadOnlyList<Gamepad.Pad> PadButtonsOf(GameAction action) => RowOf(action).Pad;

    public static IReadOnlyList<int> ScancodesOf(GameAction action)
    {
        var keys = BindingsOf(action);
        var codes = new int[keys.Length];
        for (int i = 0; i < keys.Length; i++) codes[i] = keys[i].Key;
        return codes;
    }
}
