using System.Collections.Generic;
using SplitPlay.Core.Models;

namespace SplitPlay.Core.Abstractions;

/// <summary>
/// Pure geometry helper: turns a chosen display + orientation + player count into
/// the exact screen regions each game window should occupy. No UI or OS calls,
/// which makes it trivially unit-testable.
/// </summary>
public interface ISplitLayoutCalculator
{
    /// <summary>
    /// Computes one <see cref="ScreenRegion"/> per player, in player order.
    /// </summary>
    /// <param name="display">The monitor to split.</param>
    /// <param name="orientation">Vertical (left/right) or horizontal (top/bottom).</param>
    /// <param name="playerCount">Number of players (MVP supports 2).</param>
    IReadOnlyList<ScreenRegion> Calculate(
        DisplayInfo display,
        SplitOrientation orientation,
        int playerCount);
}
