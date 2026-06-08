using SplitPlay.Core.Models;

namespace SplitPlay.App.Services;

/// <summary>
/// Navigation contract for the main shell. View models depend on this rather than
/// on the concrete <c>MainViewModel</c>, which keeps them decoupled and testable.
/// </summary>
public interface IShellNavigator
{
    /// <summary>Shows the games grid.</summary>
    void NavigateToGames();

    /// <summary>Shows the detail/configuration view for a specific game.</summary>
    void NavigateToGameDetail(SteamGame game);

    /// <summary>Shows the controllers overview.</summary>
    void NavigateToControls();

    /// <summary>Shows the application settings.</summary>
    void NavigateToSettings();
}
