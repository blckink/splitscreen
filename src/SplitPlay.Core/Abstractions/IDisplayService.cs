using System.Collections.Generic;
using SplitPlay.Core.Models;

namespace SplitPlay.Core.Abstractions;

/// <summary>
/// Enumerates the monitors connected to the PC. Implemented by the platform
/// layer; consumed by the layout calculator and the detail view.
/// </summary>
public interface IDisplayService
{
    /// <summary>Returns all connected displays, primary first.</summary>
    IReadOnlyList<DisplayInfo> GetDisplays();

    /// <summary>Returns the primary display.</summary>
    DisplayInfo GetPrimaryDisplay();
}
