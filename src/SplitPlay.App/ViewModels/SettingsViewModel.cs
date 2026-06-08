using SplitPlay.Core.Models;

namespace SplitPlay.App.ViewModels;

/// <summary>
/// Application-wide settings. Intentionally light for the MVP - it establishes the
/// page and a couple of real, useful options. More settings (themes, emulator
/// management, per-instance tweaks) plug in here over time.
/// </summary>
public sealed class SettingsViewModel : PageViewModel
{
    private SplitOrientation _defaultOrientation = SplitOrientation.Vertical;

    public override string Title => "Settings";

    /// <summary>Application version shown in the About area.</summary>
    public string AppVersion =>
        typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    /// <summary>Default split orientation suggested for newly opened games.</summary>
    public SplitOrientation DefaultOrientation
    {
        get => _defaultOrientation;
        set => SetProperty(ref _defaultOrientation, value);
    }
}
