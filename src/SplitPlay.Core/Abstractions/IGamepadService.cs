using System;
using System.Collections.Generic;
using SplitPlay.Core.Models;

namespace SplitPlay.Core.Abstractions;

/// <summary>
/// Reports the controllers currently connected to the PC and notifies when that
/// set changes (plugged in / unplugged). The MVP backs this with XInput.
/// </summary>
public interface IGamepadService : IDisposable
{
    /// <summary>Returns a snapshot of the currently connected controllers.</summary>
    IReadOnlyList<GamepadInfo> GetConnectedGamepads();

    /// <summary>
    /// Raised when the set of connected controllers changes. The UI uses this to
    /// keep the controller-assignment view live without polling itself.
    /// </summary>
    event EventHandler? GamepadsChanged;

    /// <summary>Begins monitoring for connect/disconnect events.</summary>
    void StartMonitoring();

    /// <summary>Stops monitoring.</summary>
    void StopMonitoring();

    /// <summary>Reads the live input state of a controller (for the test UI).</summary>
    GamepadState ReadState(int userIndex);

    /// <summary>
    /// Sets the rumble motors of a controller. Values are 0.0-1.0; the left motor
    /// is the low-frequency (heavy) one, the right the high-frequency (light) one.
    /// Used by the "test / identify" feature so the user can feel which pad is which.
    /// </summary>
    void SetVibration(int userIndex, double leftMotor, double rightMotor);
}
