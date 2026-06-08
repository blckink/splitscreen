namespace SplitPlay.Core.Models;

/// <summary>
/// An axis-aligned rectangle in desktop (device-pixel) coordinates that a single
/// game window should occupy. Kept as a small, framework-agnostic value type so
/// it can flow from the pure layout calculator (Core) all the way to the Win32
/// window manager (Launch) without any UI dependency.
/// </summary>
/// <param name="X">Left edge, absolute desktop X coordinate.</param>
/// <param name="Y">Top edge, absolute desktop Y coordinate.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct ScreenRegion(int X, int Y, int Width, int Height)
{
    /// <summary>Right edge (exclusive).</summary>
    public int Right => X + Width;

    /// <summary>Bottom edge (exclusive).</summary>
    public int Bottom => Y + Height;

    public override string ToString() => $"[{X},{Y} {Width}x{Height}]";
}
