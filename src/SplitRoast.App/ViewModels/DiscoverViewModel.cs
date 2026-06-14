using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SplitRoast.App.Mvvm;
using SplitRoast.Core.Abstractions;
using SplitRoast.Core.Models;

namespace SplitRoast.App.ViewModels;

/// <summary>A co-op category choice in the Discover filter dropdown.</summary>
public sealed record CoopOption(string Label, StoreCoopFilter Value);

/// <summary>A sort-order choice in the Discover filter dropdown.</summary>
public sealed record SortOption(string Label, StoreSortOrder Value);

/// <summary>A Steam tag choice (genre, subgenre or visual style). TagId 0 means "no filter".</summary>
public sealed record TagOption(string Label, int TagId);

/// <summary>
/// The Discover page: searches the public Steam store for co-op games beyond the
/// ones the user has installed. The filters are dropdowns - co-op type, genre, a
/// genre-dependent subgenre, a visual style and the sort order - whose Steam tag ids
/// are combined into one search. All network work is best-effort; an empty result
/// simply shows the empty state.
/// </summary>
public sealed class DiscoverViewModel : PageViewModel
{
    private const int PageSize = 50;

    // Verified Steam tag ids (from store.steampowered.com/tagdata/populartags).
    private static readonly TagOption AnyGenre = new("All genres", 0);
    private static readonly TagOption AnySubgenre = new("Any subgenre", 0);
    private static readonly TagOption AnyVisual = new("Any style", 0);

    // Subgenres offered per genre tag id. Genres not present here (e.g. "All") only
    // offer "Any subgenre".
    private static readonly IReadOnlyDictionary<int, TagOption[]> SubgenresByGenre =
        new Dictionary<int, TagOption[]>
        {
            [19] = new[] // Action
            {
                new TagOption("Shooter", 1774), new TagOption("FPS", 1663),
                new TagOption("Third-Person Shooter", 3814), new TagOption("Hack & Slash", 1646),
                new TagOption("Beat 'em up", 4158), new TagOption("Platformer", 1625),
                new TagOption("Fighting", 1743), new TagOption("Arcade", 1773)
            },
            [21] = new[] // Adventure
            {
                new TagOption("Open World", 1695), new TagOption("Survival", 1662),
                new TagOption("Horror", 1667), new TagOption("Point & Click", 1698),
                new TagOption("Visual Novel", 3799), new TagOption("Puzzle", 1664)
            },
            [122] = new[] // RPG
            {
                new TagOption("Action RPG", 4231), new TagOption("JRPG", 4434),
                new TagOption("Roguelike", 1716), new TagOption("Roguelite", 3959),
                new TagOption("Turn-Based", 1677), new TagOption("Tactical RPG", 21725)
            },
            [9] = new[] // Strategy
            {
                new TagOption("RTS", 1676), new TagOption("Turn-Based Strategy", 1741),
                new TagOption("Turn-Based Tactics", 14139), new TagOption("Tower Defense", 1645),
                new TagOption("Grand Strategy", 4364), new TagOption("City Builder", 4328)
            },
            [599] = new[] // Simulation
            {
                new TagOption("Building", 1643), new TagOption("Management", 12472),
                new TagOption("City Builder", 4328), new TagOption("Survival", 1662)
            },
            [492] = new[] // Indie
            {
                new TagOption("Platformer", 1625), new TagOption("Puzzle", 1664),
                new TagOption("Roguelike", 1716), new TagOption("Visual Novel", 3799),
                new TagOption("Arcade", 1773)
            },
            [597] = new[] // Casual
            {
                new TagOption("Puzzle", 1664), new TagOption("Arcade", 1773),
                new TagOption("Point & Click", 1698)
            },
            [699] = new[] // Racing
            {
                new TagOption("Driving", 1644), new TagOption("Arcade", 1773),
                new TagOption("Open World", 1695)
            },
            [701] = new[] // Sports
            {
                new TagOption("Football (Soccer)", 1254546), new TagOption("Management", 12472),
                new TagOption("Arcade", 1773)
            },
            [128] = new[] // Massively Multiplayer
            {
                new TagOption("Survival", 1662), new TagOption("Open World", 1695)
            }
        };

    private readonly IStoreDiscoveryProvider _provider;

    private CancellationTokenSource? _inflight;
    // Suppresses the reload that selecting a fresh subgenre would otherwise trigger
    // while we rebuild the subgenre list in response to a genre change.
    private bool _suppressReload;

    private string _searchTerm = string.Empty;
    private CoopOption _coop;
    private TagOption _genre;
    private TagOption _subgenre;
    private TagOption _visual;
    private SortOption _sort;

    private bool _isLoading;
    private bool _hasLoaded;
    private bool _hasError;
    private int _total;

    public DiscoverViewModel(IStoreDiscoveryProvider provider)
    {
        _provider = provider;

        CoopOptions = new[]
        {
            new CoopOption("Online co-op", StoreCoopFilter.OnlineCoop),
            new CoopOption("LAN co-op", StoreCoopFilter.LanCoop),
            new CoopOption("Any co-op", StoreCoopFilter.AnyCoop),
            new CoopOption("Split-screen", StoreCoopFilter.SplitScreen)
        };

        GenreOptions = new[]
        {
            AnyGenre,
            new TagOption("Action", 19), new TagOption("Adventure", 21),
            new TagOption("RPG", 122), new TagOption("Strategy", 9),
            new TagOption("Simulation", 599), new TagOption("Indie", 492),
            new TagOption("Casual", 597), new TagOption("Racing", 699),
            new TagOption("Sports", 701), new TagOption("MMO", 128)
        };

        VisualOptions = new[]
        {
            AnyVisual,
            new TagOption("2D", 3871), new TagOption("3D", 4191),
            new TagOption("Pixel Graphics", 3964), new TagOption("Anime", 4085),
            new TagOption("Cartoony", 4195), new TagOption("Realistic", 4175),
            new TagOption("Stylized", 4252), new TagOption("Retro", 4004),
            new TagOption("Voxel", 1732), new TagOption("Hand-drawn", 6815),
            new TagOption("Isometric", 5851), new TagOption("First-Person", 3839),
            new TagOption("Top-Down", 4791), new TagOption("Colorful", 4305),
            new TagOption("Cute", 4726)
        };

        SortOptions = new[]
        {
            new SortOption("Top reviews", StoreSortOrder.TopReviews),
            new SortOption("Newest", StoreSortOrder.Newest),
            new SortOption("Relevance", StoreSortOrder.Relevance),
            new SortOption("Name A–Z", StoreSortOrder.NameAsc)
        };

        _coop = CoopOptions[0];
        _genre = AnyGenre;
        _subgenre = AnySubgenre;
        _visual = AnyVisual;
        _sort = SortOptions[0];

        SubgenreOptions = new ObservableCollection<TagOption> { AnySubgenre };

        SearchCommand = new AsyncRelayCommand(ReloadAsync);
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync, () => CanLoadMore);
        OpenStoreCommand = new RelayCommand<DiscoverTileViewModel>(OpenInStore);
    }

    public override string Title => "Discover";

    /// <summary>The tiles bound to the grid.</summary>
    public ObservableCollection<DiscoverTileViewModel> Results { get; } = new();

    public IReadOnlyList<CoopOption> CoopOptions { get; }
    public IReadOnlyList<TagOption> GenreOptions { get; }
    public ObservableCollection<TagOption> SubgenreOptions { get; }
    public IReadOnlyList<TagOption> VisualOptions { get; }
    public IReadOnlyList<SortOption> SortOptions { get; }

    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand LoadMoreCommand { get; }
    public RelayCommand<DiscoverTileViewModel> OpenStoreCommand { get; }

    /// <summary>Free-text term. Changing it does not search until the user submits.</summary>
    public string SearchTerm
    {
        get => _searchTerm;
        set => SetProperty(ref _searchTerm, value);
    }

    public CoopOption SelectedCoopOption
    {
        get => _coop;
        set
        {
            if (value is not null && SetProperty(ref _coop, value))
            {
                OnPropertyChanged(nameof(CoopLabel));
                TriggerReload();
            }
        }
    }

    public TagOption SelectedGenreOption
    {
        get => _genre;
        set
        {
            if (value is not null && SetProperty(ref _genre, value))
            {
                RebuildSubgenres();
                TriggerReload();
            }
        }
    }

    public TagOption SelectedSubgenreOption
    {
        get => _subgenre;
        set
        {
            if (value is not null && SetProperty(ref _subgenre, value))
            {
                TriggerReload();
            }
        }
    }

    public TagOption SelectedVisualOption
    {
        get => _visual;
        set
        {
            if (value is not null && SetProperty(ref _visual, value))
            {
                TriggerReload();
            }
        }
    }

    public SortOption SelectedSortOption
    {
        get => _sort;
        set
        {
            if (value is not null && SetProperty(ref _sort, value))
            {
                TriggerReload();
            }
        }
    }

    /// <summary>Whether the subgenre dropdown offers more than just "Any".</summary>
    public bool IsSubgenreEnabled => SubgenreOptions.Count > 1;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(ShowLoadingOverlay));
                OnPropertyChanged(nameof(CanLoadMore));
                LoadMoreCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (SetProperty(ref _hasError, value))
            {
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }
    }

    /// <summary>True when a finished search produced no games (and didn't error).</summary>
    public bool ShowEmptyState => _hasLoaded && !IsLoading && !HasError && Results.Count == 0;

    /// <summary>The full-page "searching" overlay, shown only on an initial/refreshed search.</summary>
    public bool ShowLoadingOverlay => IsLoading && Results.Count == 0;

    /// <summary>More results exist than are currently loaded.</summary>
    public bool CanLoadMore => !IsLoading && Results.Count < _total;

    /// <summary>"123 of 4,567 games" summary shown beside the filters.</summary>
    public string ResultSummary => _hasLoaded && Results.Count > 0
        ? $"{Results.Count:N0} of {_total:N0} games"
        : string.Empty;

    /// <summary>Badge label applied to every tile for the active co-op filter.</summary>
    public string CoopLabel => _coop.Value switch
    {
        StoreCoopFilter.OnlineCoop => "Online co-op",
        StoreCoopFilter.LanCoop => "LAN co-op",
        StoreCoopFilter.SplitScreen => "Split-screen",
        _ => "Co-op"
    };

    /// <summary>Loads the first page once, the first time the page is shown.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (!_hasLoaded && !IsLoading)
        {
            await ReloadAsync();
        }
    }

    private void RebuildSubgenres()
    {
        _suppressReload = true;
        SubgenreOptions.Clear();
        SubgenreOptions.Add(AnySubgenre);

        if (SubgenresByGenre.TryGetValue(_genre.TagId, out TagOption[]? subs))
        {
            foreach (TagOption sub in subs)
            {
                SubgenreOptions.Add(sub);
            }
        }

        // Reset the selection to "Any" for the new genre.
        SelectedSubgenreOption = AnySubgenre;
        _suppressReload = false;

        OnPropertyChanged(nameof(IsSubgenreEnabled));
    }

    private void TriggerReload()
    {
        if (!_suppressReload)
        {
            _ = ReloadAsync();
        }
    }

    private async Task ReloadAsync()
    {
        CancellationToken token = BeginRequest();
        IsLoading = true;
        HasError = false;
        Results.Clear();
        OnPropertyChanged(nameof(ResultSummary));

        try
        {
            StoreSearchResult result = await _provider.SearchAsync(BuildQuery(0), token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
            {
                return;
            }

            _total = result.TotalCount;
            AddTiles(result);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request; ignore.
        }
        catch (Exception ex)
        {
            SplitRoast.App.Diagnostics.CrashReporter.Report(ex, "Discover search");
            HasError = true;
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                _hasLoaded = true;
                IsLoading = false;
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(ResultSummary));
                OnPropertyChanged(nameof(CanLoadMore));
                LoadMoreCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private async Task LoadMoreAsync()
    {
        if (!CanLoadMore)
        {
            return;
        }

        CancellationToken token = BeginRequest();
        IsLoading = true;
        try
        {
            StoreSearchResult result = await _provider.SearchAsync(BuildQuery(Results.Count), token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (result.TotalCount > 0)
            {
                _total = result.TotalCount;
            }

            AddTiles(result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SplitRoast.App.Diagnostics.CrashReporter.Report(ex, "Discover load more");
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(ResultSummary));
                OnPropertyChanged(nameof(CanLoadMore));
                LoadMoreCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private StoreSearchQuery BuildQuery(int start)
    {
        var tags = new List<int>(3);
        if (_genre.TagId > 0) tags.Add(_genre.TagId);
        if (_subgenre.TagId > 0) tags.Add(_subgenre.TagId);
        if (_visual.TagId > 0) tags.Add(_visual.TagId);

        return new StoreSearchQuery
        {
            Term = _searchTerm,
            Coop = _coop.Value,
            Tags = tags,
            Sort = _sort.Value,
            Start = start,
            Count = PageSize
        };
    }

    private void AddTiles(StoreSearchResult result)
    {
        CoopSuitability suitability = _coop.Value == StoreCoopFilter.SplitScreen
            ? CoopSuitability.NativeSplitScreen
            : CoopSuitability.Recommended;

        foreach (StoreGame game in result.Games)
        {
            Results.Add(new DiscoverTileViewModel(game, CoopLabel, suitability));
        }
    }

    /// <summary>Cancels any in-flight request and returns a token for the new one.</summary>
    private CancellationToken BeginRequest()
    {
        _inflight?.Cancel();
        _inflight = new CancellationTokenSource();
        return _inflight.Token;
    }

    private void OpenInStore(DiscoverTileViewModel? tile)
    {
        if (tile is null)
        {
            return;
        }

        try
        {
            // Prefer the Steam client's in-app store page; fall back to the browser.
            Process.Start(new ProcessStartInfo(tile.Game.SteamStoreUrl) { UseShellExecute = true });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo(tile.Game.StorePageUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SplitRoast.App.Diagnostics.CrashReporter.Report(ex, "Open store page");
            }
        }
    }
}
