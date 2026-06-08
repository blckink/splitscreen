using System;
using Microsoft.Extensions.DependencyInjection;
using SplitPlay.App.Mvvm;
using SplitPlay.App.Services;
using SplitPlay.Core.Models;

namespace SplitPlay.App.ViewModels;

/// <summary>
/// The shell view model. Owns the top-bar state (active section, search) and the
/// currently hosted page, and acts as the application's <see cref="IShellNavigator"/>.
///
/// Child pages are resolved lazily from the container rather than injected into the
/// constructor. This deliberately breaks the cycle that would otherwise exist
/// (a page needs the navigator, which is this view model), and keeps startup cheap.
/// The persistent pages are singletons, so lazy resolution returns the same
/// instance each time; the game detail page is transient for clean per-game state.
/// </summary>
public sealed class MainViewModel : ObservableObject, IShellNavigator
{
    // Section keys, used to highlight the active top-bar item.
    public const string SectionGames = "Games";
    public const string SectionControls = "Controls";
    public const string SectionSettings = "Settings";

    private readonly IServiceProvider _services;

    private GamesViewModel? _games;
    private ControlsViewModel? _controls;
    private SettingsViewModel? _settings;
    private GameDetailViewModel? _activeDetail;

    private PageViewModel? _currentPage;
    private string _activeSection = SectionGames;

    public MainViewModel(IServiceProvider services)
    {
        _services = services;

        ShowGamesCommand = new RelayCommand(NavigateToGames);
        ShowControlsCommand = new RelayCommand(NavigateToControls);
        ShowSettingsCommand = new RelayCommand(NavigateToSettings);
    }

    public RelayCommand ShowGamesCommand { get; }

    public RelayCommand ShowControlsCommand { get; }

    public RelayCommand ShowSettingsCommand { get; }

    // Lazily-resolved persistent pages (singletons in the container).
    private GamesViewModel Games => _games ??= _services.GetRequiredService<GamesViewModel>();
    private ControlsViewModel Controls => _controls ??= _services.GetRequiredService<ControlsViewModel>();
    private SettingsViewModel Settings => _settings ??= _services.GetRequiredService<SettingsViewModel>();

    /// <summary>The page currently shown in the main content area.</summary>
    public PageViewModel? CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(IsSearchVisible));
            }
        }
    }

    /// <summary>Which top-bar section is highlighted.</summary>
    public string ActiveSection
    {
        get => _activeSection;
        private set
        {
            if (SetProperty(ref _activeSection, value))
            {
                OnPropertyChanged(nameof(IsGamesActive));
                OnPropertyChanged(nameof(IsControlsActive));
                OnPropertyChanged(nameof(IsSettingsActive));
            }
        }
    }

    public bool IsGamesActive => ActiveSection == SectionGames;
    public bool IsControlsActive => ActiveSection == SectionControls;
    public bool IsSettingsActive => ActiveSection == SectionSettings;

    /// <summary>The search box is only meaningful on the games grid itself.</summary>
    public bool IsSearchVisible => _currentPage is GamesViewModel;

    /// <summary>Top-bar search text, proxied to the games grid filter.</summary>
    public string SearchText
    {
        get => Games.SearchText;
        set
        {
            Games.SearchText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Shows the games grid and kicks off the initial scan. Called at startup.</summary>
    public async void Start()
    {
        NavigateToGames();
        await Games.EnsureLoadedAsync();
    }

    // --- IShellNavigator ---

    public void NavigateToGames()
    {
        DetachActiveDetail();
        CurrentPage = Games;
        ActiveSection = SectionGames;
    }

    public async void NavigateToGameDetail(SteamGame game)
    {
        try
        {
            DetachActiveDetail();

            var detail = _services.GetRequiredService<GameDetailViewModel>();
            _activeDetail = detail;
            CurrentPage = detail;
            ActiveSection = SectionGames; // Detail is conceptually under "Games".

            await detail.InitializeAsync(game);
        }
        catch (Exception ex)
        {
            // Never let opening a game take down the app; show what happened and
            // fall back to the games grid.
            Diagnostics.CrashReporter.Report(ex, "Open game detail");
            NavigateToGames();
        }
    }

    public void NavigateToControls()
    {
        DetachActiveDetail();
        CurrentPage = Controls;
        ActiveSection = SectionControls;
    }

    public void NavigateToSettings()
    {
        DetachActiveDetail();
        CurrentPage = Settings;
        ActiveSection = SectionSettings;
    }

    /// <summary>Cleans up the detail page (controller subscription) when leaving it.</summary>
    private void DetachActiveDetail()
    {
        if (_activeDetail is not null)
        {
            _activeDetail.Cleanup();
            _activeDetail = null;
        }
    }
}
