using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using SplitPlay.App.Mvvm;
using SplitPlay.App.Services;
using SplitPlay.Core.Abstractions;
using SplitPlay.Core.Models;

namespace SplitPlay.App.ViewModels;

/// <summary>
/// The default page: the responsive grid of installed Steam games. Handles
/// scanning, lazy artwork loading, live search filtering and selecting a game.
/// </summary>
public sealed class GamesViewModel : PageViewModel
{
    private readonly ISteamLibraryScanner _scanner;
    private readonly IGameArtworkProvider _artworkProvider;
    private readonly IShellNavigator _navigator;

    // Full, unfiltered set so search can filter without re-scanning.
    private readonly List<GameTileViewModel> _allTiles = new();

    private string _searchText = string.Empty;
    private bool _isLoading;
    private bool _hasLoaded;

    public GamesViewModel(
        ISteamLibraryScanner scanner,
        IGameArtworkProvider artworkProvider,
        IShellNavigator navigator)
    {
        _scanner = scanner;
        _artworkProvider = artworkProvider;
        _navigator = navigator;

        SelectGameCommand = new RelayCommand<GameTileViewModel>(OnSelectGame);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
    }

    public override string Title => "Games";

    /// <summary>The filtered tiles bound to the grid.</summary>
    public ObservableCollection<GameTileViewModel> Games { get; } = new();

    public RelayCommand<GameTileViewModel> SelectGameCommand { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Live search text; updating it re-filters the grid.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }
    }

    /// <summary>True when a finished load produced no games (shows an empty state).</summary>
    public bool ShowEmptyState => _hasLoaded && !IsLoading && _allTiles.Count == 0;

    /// <summary>
    /// Scans the library once. Subsequent navigations reuse the loaded list; call
    /// <see cref="RefreshCommand"/> to force a re-scan.
    /// </summary>
    public async Task EnsureLoadedAsync()
    {
        if (!_hasLoaded && !IsLoading)
        {
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            IReadOnlyList<SteamGame> games = await _scanner.ScanAsync();

            _allTiles.Clear();
            foreach (SteamGame game in games)
            {
                _allTiles.Add(new GameTileViewModel(game));
            }

            _hasLoaded = true;
            ApplyFilter();

            // Resolve artwork after the grid is populated so tiles appear instantly
            // and fill in their covers progressively.
            await LoadArtworkAsync();
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowEmptyState));
        }
    }

    private async Task LoadArtworkAsync()
    {
        foreach (GameTileViewModel tile in _allTiles)
        {
            GameArtwork artwork = await _artworkProvider.ResolveAsync(tile.Game);
            tile.ApplyArtwork(artwork);
        }
    }

    /// <summary>Rebuilds <see cref="Games"/> from the search text.</summary>
    private void ApplyFilter()
    {
        IEnumerable<GameTileViewModel> filtered = string.IsNullOrWhiteSpace(_searchText)
            ? _allTiles
            : _allTiles.Where(t =>
                t.Title.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase));

        Games.Clear();
        foreach (GameTileViewModel tile in filtered)
        {
            Games.Add(tile);
        }
    }

    private void OnSelectGame(GameTileViewModel? tile)
    {
        if (tile is not null)
        {
            _navigator.NavigateToGameDetail(tile.Game);
        }
    }
}
