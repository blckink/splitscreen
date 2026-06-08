using System;

namespace SplitPlay.Core.Models;

/// <summary>
/// XInput digital button flags. Values match the native XINPUT_GAMEPAD button
/// bitmask so a raw <c>wButtons</c> value can be cast directly to this enum.
/// </summary>
[Flags]
public enum GamepadButtons : ushort
{
    None = 0x0000,
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000
}

/// <summary>
/// A snapshot of a controller's inputs at a moment in time. Used by the controller
/// test UI and the test windows to visualise live input. Triggers are 0-255 and
/// sticks are -32768..32767, matching XInput.
/// </summary>
public readonly record struct GamepadState(
    bool IsConnected,
    GamepadButtons Buttons,
    byte LeftTrigger,
    byte RightTrigger,
    short LeftStickX,
    short LeftStickY,
    short RightStickX,
    short RightStickY)
{
    /// <summary>A disconnected, all-zero state.</summary>
    public static readonly GamepadState Disconnected = new(
        false, GamepadButtons.None, 0, 0, 0, 0, 0, 0);

    // A generous dead-zone so resting sticks/triggers don't register as "input".
    private const short StickDeadzone = 12000;
    private const byte TriggerThreshold = 40;

    /// <summary>True if the user is actively pressing/moving anything right now.</summary>
    public bool HasAnyInput =>
        Buttons != GamepadButtons.None ||
        LeftTrigger > TriggerThreshold || RightTrigger > TriggerThreshold ||
        Math.Abs((int)LeftStickX) > StickDeadzone || Math.Abs((int)LeftStickY) > StickDeadzone ||
        Math.Abs((int)RightStickX) > StickDeadzone || Math.Abs((int)RightStickY) > StickDeadzone;

    /// <summary>True if a specific button is currently held.</summary>
    public bool IsPressed(GamepadButtons button) => (Buttons & button) == button;
}
