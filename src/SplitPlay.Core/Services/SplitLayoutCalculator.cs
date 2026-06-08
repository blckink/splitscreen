using System;
using System.Collections.Generic;
using SplitPlay.Core.Abstractions;
using SplitPlay.Core.Models;

namespace SplitPlay.Core.Services;

/// <summary>
/// Default <see cref="ISplitLayoutCalculator"/>. Splits the display's full bounds
/// evenly between players. Pure integer math with no rounding gaps: the last
/// region absorbs any remainder so the regions always tile the screen exactly.
/// </summary>
public sealed class SplitLayoutCalculator : ISplitLayoutCalculator
{
    public IReadOnlyList<ScreenRegion> Calculate(
        DisplayInfo display,
        SplitOrientation orientation,
        int playerCount)
    {
        ArgumentNullException.ThrowIfNull(display);
        if (playerCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerCount), playerCount, "At least one player is required.");
        }

        // We split the full monitor bounds (borderless, edge-to-edge) rather than
        // the work area, because split-screen game windows cover the taskbar.
        ScreenRegion area = display.Bounds;
        var regions = new List<ScreenRegion>(playerCount);

        if (orientation == SplitOrientation.Vertical)
        {
            // Columns side by side: divide the width.
            int baseWidth = area.Width / playerCount;
            int x = area.X;
            for (int i = 0; i < playerCount; i++)
            {
                // Last column takes the leftover pixels so we never leave a gap.
                int width = (i == playerCount - 1) ? area.Right - x : baseWidth;
                regions.Add(new ScreenRegion(x, area.Y, width, area.Height));
                x += width;
            }
        }
        else
        {
            // Rows stacked: divide the height.
            int baseHeight = area.Height / playerCount;
            int y = area.Y;
            for (int i = 0; i < playerCount; i++)
            {
                int height = (i == playerCount - 1) ? area.Bottom - y : baseHeight;
                regions.Add(new ScreenRegion(area.X, y, area.Width, height));
                y += height;
            }
        }

        return regions;
    }
}
