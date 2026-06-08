namespace SplitPlay.Core.Models;

/// <summary>
/// A controller currently connected to the PC, as reported by the input module.
/// MVP targets XInput pads (Xbox-style), which expose a fixed user index 0-3.
/// </summary>
public sealed class GamepadInfo
{
    /// <summary>
    /// XInput user index (0-3). This is the value we will route to the correct
    /// game instance so that one pad only ever drives one window.
    /// </summary>
    public required int UserIndex { get; init; }

    /// <summary>Whether the pad is currently connected.</summary>
    public required bool IsConnected { get; init; }

    /// <summary>A friendly, user-facing label, e.g. "Controller 1".</summary>
    public string DisplayName => $"Controller {UserIndex + 1}";

    /// <summary>
    /// Optional sub-type/description (e.g. "Gamepad", "Wheel"). Populated when
    /// the backend can determine it; otherwise null.
    /// </summary>
    public string? SubType { get; init; }
}
