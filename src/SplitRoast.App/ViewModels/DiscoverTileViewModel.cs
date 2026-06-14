using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SplitRoast.App.Imaging;
using SplitRoast.App.Mvvm;
using SplitRoast.Core.Models;

namespace SplitRoast.App.ViewModels;

/// <summary>How positive a game's Steam review summary is, for the caption dot colour.</summary>
public enum ReviewTone
{
    Neutral,
    Positive,
    Mixed,
    Negative
}

/// <summary>
/// View model for a single tile on the Discover grid. Wraps a <see cref="StoreGame"/>
/// (a store-search result, not an installed game) and exposes what the tile needs:
/// a cover, a co-op badge, and review / price metadata.
///
/// The cover is loaded from the Steam CDN. A few (usually tiny) titles have no
/// vertical capsule, so we try a chain - library_600x900 -> header -> store capsule -
/// advancing to the next source whenever the image pipeline reports a download or
/// decode failure, and only showing the placeholder once every source is exhausted.
/// </summary>
public sealed class DiscoverTileViewModel : ObservableObject
{
    // Tiles render around ~190px wide; decode a little larger for high-DPI crispness.
    private const int CoverDecodeWidth = 240;

    private readonly Queue<string> _coverCandidates = new();
    private ImageSource? _cover;

    public DiscoverTileViewModel(StoreGame game, string coopLabel, CoopSuitability suitability)
    {
        Game = game;
        CoopLabel = coopLabel;
        Suitability = suitability;
        ReviewTone = Classify(game.ReviewSummary);

        EnqueueCandidate(game.CoverUrl);
        EnqueueCandidate(game.HeaderUrl);
        EnqueueCandidate(game.CapsuleUrl);
        LoadNextCover();
    }

    /// <summary>The underlying store result this tile represents.</summary>
    public StoreGame Game { get; }

    public string Title => Game.Name;

    /// <summary>Cover capsule. Null only once every fallback source has failed.</summary>
    public ImageSource? Cover
    {
        get => _cover;
        private set
        {
            if (SetProperty(ref _cover, value))
            {
                OnPropertyChanged(nameof(HasCover));
            }
        }
    }

    public bool HasCover => _cover is not null;

    /// <summary>Compact co-op label for the corner badge (e.g. "Online co-op").</summary>
    public string CoopLabel { get; }

    /// <summary>Drives the badge colour via the shared suitability brush converter.</summary>
    public CoopSuitability Suitability { get; }

    public string? PriceText => Game.PriceText;

    public bool HasPrice => !string.IsNullOrEmpty(Game.PriceText);

    public string? ReviewSummary => Game.ReviewSummary;

    public bool HasReview => !string.IsNullOrEmpty(Game.ReviewSummary);

    /// <summary>Sentiment of the review summary, for the coloured caption dot.</summary>
    public ReviewTone ReviewTone { get; }

    public string? ReleaseDate => Game.ReleaseDate;

    public bool HasReleaseDate => !string.IsNullOrEmpty(Game.ReleaseDate);

    private void EnqueueCandidate(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            _coverCandidates.Enqueue(url);
        }
    }

    private void LoadNextCover()
    {
        while (_coverCandidates.Count > 0)
        {
            string url = _coverCandidates.Dequeue();
            ImageSource? source = ImageLoader.TryLoad(url, CoverDecodeWidth);
            if (source is null)
            {
                continue;
            }

            // Remote images load on demand and are not frozen yet; watch for a failed
            // download/decode so we can fall back to the next source.
            if (source is BitmapImage { IsDownloading: true } bitmap)
            {
                bitmap.DownloadFailed += OnCoverFailed;
                bitmap.DecodeFailed += OnCoverFailed;
            }

            Cover = source;
            return;
        }

        Cover = null;
    }

    private void OnCoverFailed(object? sender, ExceptionEventArgs e)
    {
        if (sender is BitmapImage bitmap)
        {
            bitmap.DownloadFailed -= OnCoverFailed;
            bitmap.DecodeFailed -= OnCoverFailed;
        }

        LoadNextCover();
    }

    private static ReviewTone Classify(string? summary)
    {
        if (string.IsNullOrEmpty(summary))
        {
            return ReviewTone.Neutral;
        }

        if (summary.Contains("Mixed", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewTone.Mixed;
        }

        if (summary.Contains("Negative", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewTone.Negative;
        }

        if (summary.Contains("Positive", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewTone.Positive;
        }

        return ReviewTone.Neutral;
    }
}
