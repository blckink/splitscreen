namespace SplitPlay.Core.Models;

/// <summary>
/// Represents a single Steam game discovered on the local machine.
/// This is a lightweight, immutable description of what was found on disk;
/// per-game user settings live in <see cref="GameProfile"/> instead.
/// </summary>
public sealed class SteamGame
{
    /// <summary>Steam application id (the numeric "appid" from the manifest).</summary>
    public required uint AppId { get; init; }

    /// <summary>Display name as reported by Steam (e.g. "Overwatch").</summary>
    public required string Name { get; init; }

    /// <summary>Absolute path to the game's install directory.</summary>
    public required string InstallDir { get; init; }

    /// <summary>
    /// The Steam library root that contains this game (the folder that holds
    /// the "steamapps" directory). Useful for resolving artwork and manifests.
    /// </summary>
    public required string LibraryPath { get; init; }

    /// <summary>Size on disk in bytes, if reported by the manifest; otherwise null.</summary>
    public long? SizeOnDiskBytes { get; init; }

    /// <summary>
    /// Resolved artwork for this game. Populated lazily by the artwork provider
    /// so that scanning the library stays fast.
    /// </summary>
    public GameArtwork Artwork { get; set; } = GameArtwork.Empty;

    /// <summary>
    /// The Steam URL that launches the game (steam://rungameid/{appid}).
    /// Provided as a convenience for the launch engine.
    /// </summary>
    public string SteamRunUrl => $"steam://rungameid/{AppId}";
}
