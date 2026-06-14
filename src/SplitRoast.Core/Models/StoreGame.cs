namespace SplitRoast.Core.Models;

/// <summary>
/// A game discovered via the public Steam store search (the "Discover" page),
/// as opposed to a <see cref="SteamGame"/> which is installed locally. This is a
/// lightweight, read-only description built from a single search result row; it is
/// never launched directly (the user is sent to its Steam store page instead).
/// </summary>
public sealed class StoreGame
{
    /// <summary>Steam application id.</summary>
    public required uint AppId { get; init; }

    /// <summary>Display name as listed on the store.</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable release date as Steam renders it (e.g. "9 Jul, 2013"), if any.</summary>
    public string? ReleaseDate { get; init; }

    /// <summary>Short review summary (e.g. "Very Positive"), if Steam listed one.</summary>
    public string? ReviewSummary { get; init; }

    /// <summary>Formatted price (e.g. "Free", "$19.99"), if available.</summary>
    public string? PriceText { get; init; }

    /// <summary>
    /// Vertical library capsule (600x900) on the public Steam CDN, used as the tile
    /// cover so Discover tiles match the installed-library grid. Populated by the
    /// provider. Some (usually tiny) titles have no vertical capsule - see
    /// <see cref="HeaderUrl"/> and <see cref="CapsuleUrl"/> for fallbacks.
    /// </summary>
    public required string CoverUrl { get; init; }

    /// <summary>Wide header image (460x215) - fallback cover when no vertical capsule exists.</summary>
    public string? HeaderUrl { get; init; }

    /// <summary>The small store capsule from the search row itself (always present) - last-resort cover.</summary>
    public string? CapsuleUrl { get; init; }

    /// <summary>The store page on the web (browser fallback).</summary>
    public string StorePageUrl => $"https://store.steampowered.com/app/{AppId}";

    /// <summary>The store page opened inside the Steam client (preferred).</summary>
    public string SteamStoreUrl => $"steam://store/{AppId}";
}
