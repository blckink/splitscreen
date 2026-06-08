using System;
using SplitPlay.Core.Models;
using SplitPlay.Input.Native;

namespace SplitPlay.Input;

/// <summary>
/// Public, allocation-free helpers over XInput, shared by the gamepad service and
/// the test windows. Keeping the native calls behind this small surface means
/// there is exactly one place that talks to XInput.
/// </summary>
public static class XInputReader
{
    /// <summary>Number of controller slots XInput exposes (0-3).</summary>
    public const int MaxControllers = XInput.MaxControllers;

    /// <summary>Returns true if a controller is connected at the given index.</summary>
    public static bool IsConnected(int userIndex) =>
        XInput.XInputGetState(userIndex, out _) == XInput.ErrorSuccess;

    /// <summary>Reads the full input state for a controller index.</summary>
    public static GamepadState ReadState(int userIndex)
    {
        if (XInput.XInputGetState(userIndex, out XInput.XINPUT_STATE state) != XInput.ErrorSuccess)
        {
            return GamepadState.Disconnected;
        }

        XInput.XINPUT_GAMEPAD pad = state.Gamepad;
        return new GamepadState(
            IsConnected: true,
            Buttons: (GamepadButtons)pad.wButtons,
            LeftTrigger: pad.bLeftTrigger,
            RightTrigger: pad.bRightTrigger,
            LeftStickX: pad.sThumbLX,
            LeftStickY: pad.sThumbLY,
            RightStickX: pad.sThumbRX,
            RightStickY: pad.sThumbRY);
    }

    /// <summary>
    /// Sets the rumble motors (0.0-1.0 each). Out-of-range values are clamped.
    /// </summary>
    public static void SetVibration(int userIndex, double leftMotor, double rightMotor)
    {
        var vibration = new XInput.XINPUT_VIBRATION
        {
            wLeftMotorSpeed = ToMotorSpeed(leftMotor),
            wRightMotorSpeed = ToMotorSpeed(rightMotor)
        };

        XInput.XInputSetState(userIndex, ref vibration);
    }

    private static ushort ToMotorSpeed(double value)
    {
        double clamped = Math.Clamp(value, 0.0, 1.0);
        return (ushort)(clamped * ushort.MaxValue);
    }
}
