namespace SplitPlay.Core.Models;

/// <summary>
/// Describes a physical monitor connected to the PC. Provided by the
/// platform layer (the WPF app) and consumed by the layout calculator.
/// </summary>
public sealed class DisplayInfo
{
    /// <summary>The OS device name (e.g. "\\.\DISPLAY1").</summary>
    public required string DeviceName { get; init; }

    /// <summary>Full monitor bounds in desktop pixels (including taskbar area).</summary>
    public required ScreenRegion Bounds { get; init; }

    /// <summary>Usable work area (excludes the taskbar).</summary>
    public required ScreenRegion WorkingArea { get; init; }

    /// <summary>True if this is the primary monitor.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>DPI scale factor (1.0 == 100%, 1.5 == 150%, ...).</summary>
    public double Scale { get; init; } = 1.0;

    /// <summary>A friendly label for UI display, e.g. "Display 1 (1920x1080)".</summary>
    public string DisplayLabel =>
        $"{(IsPrimary ? "Primary" : "Display")} {Bounds.Width}x{Bounds.Height}";
}
