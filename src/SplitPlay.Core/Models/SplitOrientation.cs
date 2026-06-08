namespace SplitPlay.Core.Models;

/// <summary>
/// How the screen is divided between the two players.
/// </summary>
public enum SplitOrientation
{
    /// <summary>
    /// Divided by a vertical line into a left and a right half (two columns).
    /// Usually the best choice on a single wide/landscape monitor.
    /// </summary>
    Vertical,

    /// <summary>
    /// Divided by a horizontal line into a top and a bottom half (two rows).
    /// </summary>
    Horizontal
}
