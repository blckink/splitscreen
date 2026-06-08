namespace SplitPlay.Core.Models;

/// <summary>
/// Holds the set of artwork sources for a game. Each property is either a local
/// file path (preferred, pulled straight from the Steam cache) or a remote URL
/// fallback. Consumers should treat null/empty as "no art available" and fall
/// back to a placeholder.
/// </summary>
public sealed class GameArtwork
{
    /// <summary>An empty artwork instance used as a safe default.</summary>
    public static readonly GameArtwork Empty = new();

    /// <summary>
    /// Vertical "library capsule" (600x900-ish). This is the primary image used
    /// for the game tiles in the grid.
    /// </summary>
    public string? LibraryCapsulePath { get; init; }

    /// <summary>Wide header/capsule image (460x215), good for detail headers.</summary>
    public string? HeaderPath { get; init; }

    /// <summary>Full-width hero/background image used behind the detail view.</summary>
    public string? HeroPath { get; init; }

    /// <summary>Transparent game logo, overlaid on the hero where available.</summary>
    public string? LogoPath { get; init; }

    /// <summary>True if at least one usable artwork source is present.</summary>
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(LibraryCapsulePath) ||
        !string.IsNullOrWhiteSpace(HeaderPath) ||
        !string.IsNullOrWhiteSpace(HeroPath);
}
