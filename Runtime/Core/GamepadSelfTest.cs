#if DEBUG
using System;

namespace PixelCore.Runtime.Core;

public static class GamepadSelfTest
{
    private static int _pass, _fail;

    public static void Run()
    {
        Console.WriteLine("=== Gamepad self-test ===");
        _pass = _fail = 0;
        Rumble.SuppressDevice = true;
        try
        {
            TestStickHysteresis();
            TestActionReadsBothInputs();
            TestDisconnectReleases();
            TestRumble();
        }
        finally
        {
            Gamepad.DebugReset();
            Rumble.DebugReset();
            Rumble.SuppressDevice = false;
        }
        Console.WriteLine($"=== Gamepad: {_pass} passed, {_fail} failed ===");
    }

    private static void TestStickHysteresis()
    {
        Gamepad.DebugReset();
        Gamepad.DebugInjected = true;

        Gamepad.DebugTick(); Gamepad.DebugSetAxis(Gamepad.Pad.Left, 0.4f);
        Check("inside the deadzone (0.4) the direction does not turn on", !Gamepad.IsDown(Gamepad.Pad.Left));

        Gamepad.DebugTick(); Gamepad.DebugSetAxis(Gamepad.Pad.Left, 0.6f);
        Check("★ past the threshold it turns on (0.6)", Gamepad.IsDown(Gamepad.Pad.Left));
        Check("★ that frame is the 'pressed' moment (the stamp works on the gamepad too)",
              Gamepad.IsPressed(Gamepad.Pad.Left));

        Gamepad.DebugTick(); Gamepad.DebugSetAxis(Gamepad.Pad.Left, 0.4f);
        Check("★ once on, it stays on when it drops back to 0.4 (no flicker at the boundary)", Gamepad.IsDown(Gamepad.Pad.Left));
        Check("★ while held, the 'pressed' moment does not fire again", !Gamepad.IsPressed(Gamepad.Pad.Left));

        Gamepad.DebugTick(); Gamepad.DebugSetAxis(Gamepad.Pad.Left, 0.3f);
        Check("below the exit threshold (0.3) it turns off", !Gamepad.IsDown(Gamepad.Pad.Left));
        Check("that frame is the 'released' moment", Gamepad.IsReleased(Gamepad.Pad.Left));

        Check("★ control: the two thresholds really differ (if equal, the checks above are vacuous)",
              Gamepad.ExitThreshold < Gamepad.EnterThreshold,
              $"enter={Gamepad.EnterThreshold} exit={Gamepad.ExitThreshold}");

        Gamepad.DebugReset(); Gamepad.DebugInjected = true;
        Gamepad.DebugTick();
        Gamepad.DebugSetAxis(Gamepad.Pad.Left, 0.8f);
        Gamepad.DebugSetAxis(Gamepad.Pad.Up, 0.8f);
        Check("a diagonal turns both directions on (the dominant axis is not picked here)",
              Gamepad.IsDown(Gamepad.Pad.Left) && Gamepad.IsDown(Gamepad.Pad.Up));
    }

    private static void TestActionReadsBothInputs()
    {
        Gamepad.DebugReset();
        Gamepad.DebugInjected = true;

        foreach (var action in Enum.GetValues<GameAction>())
        {
            var pads = InputMap.PadButtonsOf(action);
            Check($"{action}: the table has a gamepad button", pads.Count > 0);
            if (pads.Count == 0) continue;

            Gamepad.DebugTick();
            Gamepad.DebugSet(pads[0], true);
            Check($"★ {action}: fires from the gamepad alone (keyboard not pressed)",
                  InputMap.IsDown(action) && InputMap.IsPressed(action));

            Gamepad.DebugTick();
            Gamepad.DebugSet(pads[0], false);
            Check($"{action}: releasing turns it off", !InputMap.IsDown(action));
        }

        Gamepad.DebugTick();
        bool anyOn = false;
        foreach (var a in Enum.GetValues<GameAction>()) anyOn |= InputMap.IsDown(a);
        Check("★ control: with no input at all, no action is on", !anyOn);
    }

    private static void TestDisconnectReleases()
    {
        Gamepad.DebugReset();
        Gamepad.DebugInjected = true;
        Gamepad.DebugTick();
        Gamepad.DebugSet(Gamepad.Pad.Left, true);
        Check("premise: it is held", Gamepad.IsDown(Gamepad.Pad.Left));

        Gamepad.DebugInjected = false;
        Gamepad.Update();
        Check("★ on disconnect every button is released (nothing stays stuck down)",
              !Gamepad.IsDown(Gamepad.Pad.Left) && !Gamepad.IsConnected);
    }

    private static void TestRumble()
    {
        Rumble.DebugReset();
        Rumble.SuppressDevice = true;

        Rumble.Play(0.5f, 0.2f);
        Check("rumble starts", Rumble.IsActive);

        Rumble.Play(0.8f, 0.1f);
        Rumble.Update(0f);
        Check("★ when they overlap the stronger one wins (not additive)", Math.Abs(Rumble.CurrentStrength - 0.8f) < 0.01f,
              Rumble.CurrentStrength.ToString("0.00"));

        Rumble.Update(0.1f);
        float mid = Rumble.CurrentStrength;
        Check("★ it fades as time passes", mid > 0f && mid < 0.8f, mid.ToString("0.00"));

        Rumble.Update(0.2f);
        Check("★ it stops when finished", !Rumble.IsActive && Rumble.LastApplied == 0f,
              $"active={Rumble.IsActive} applied={Rumble.LastApplied}");

        Rumble.Play(1f, 5f);
        Rumble.Update(0f);
        Check("premise: it is rumbling for a long time", Rumble.IsActive && Rumble.LastApplied > 0f);
        Rumble.StopAll();
        Check("★ StopAll stops it immediately (sends 0 to the device)",
              !Rumble.IsActive && Rumble.LastApplied == 0f);

        Rumble.Play(1f, 5f); Rumble.Update(0f);
        Rumble.Enabled = false;
        Check("★ disabling stops it immediately", !Rumble.IsActive && Rumble.LastApplied == 0f);
        Rumble.Play(1f, 5f);
        Check("★ while disabled, new rumble does not start either", !Rumble.IsActive);
        Rumble.Enabled = true;

        Rumble.StopAll();
        Rumble.LastAppliedProbeReset();
        Rumble.Cancel();
        Check("★ Cancel does not touch the device when already quiet", !Rumble.DeviceTouched);
        Rumble.Play(0.5f, 1f); Rumble.Update(0f);
        Rumble.LastAppliedProbeReset();
        Rumble.Cancel();
        Check("★ Cancel stops it while rumbling", Rumble.DeviceTouched && !Rumble.IsActive);
    }

    private static void Check(string label, bool ok, string? detail = null)
    {
        if (ok) { _pass++; Console.WriteLine($"  [pass] {label}"); }
        else { _fail++; Console.WriteLine($"  [FAIL] {label}{(detail != null ? $" - {detail}" : "")}"); }
    }
}
#endif
