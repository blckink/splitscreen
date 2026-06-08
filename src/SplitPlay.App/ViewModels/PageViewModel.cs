using SplitPlay.App.Mvvm;

namespace SplitPlay.App.ViewModels;

/// <summary>
/// Base class for the top-level pages hosted by the shell (Games, Controls,
/// Settings, Game detail). Gives each page a title the shell can display.
/// </summary>
public abstract class PageViewModel : ObservableObject
{
    /// <summary>Human-readable page title.</summary>
    public abstract string Title { get; }
}
