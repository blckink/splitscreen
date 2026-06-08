using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SplitPlay.Core.Abstractions;
using SplitPlay.Core.Models;

namespace SplitPlay.Steam;

/// <summary>
/// Resolves artwork for a game. Strategy, in priority order:
///   1. Local images already cached by the Steam client (instant, offline).
///   2. The public Steam CDN as a URL fallback (loaded directly by the UI).
///
/// We never download anything ourselves here; remote fallbacks are returned as
/// https URLs that the WPF image pipeline can fetch and cache on demand. This
/// keeps the provider fast and side-effect free.
/// </summary>
public sealed class SteamArtworkProvider : IGameArtworkProvider
{
    // Public Steam CDN base. The classic /steam/apps/{appid}/ assets are stable.
    private const string CdnBase = "https://cdn.cloudflare.steamstatic.com/steam/apps";

    private readonly string? _libraryCacheDir;

    public SteamArtworkProvider()
    {
        string? steamPath = SteamLocator.FindSteamPath();
        _libraryCacheDir = steamPath is null
            ? null
            : Path.Combine(steamPath, "appcache", "librarycache");
    }

    public Task<GameArtwork> ResolveAsync(SteamGame game, CancellationToken cancellationToken = default)
    {
        var artwork = new GameArtwork
        {
            LibraryCapsulePath = ResolveOne(game.AppId, "library_600x900", ".jpg"),
            HeaderPath = ResolveOne(game.AppId, "header", ".jpg"),
            HeroPath = ResolveOne(game.AppId, "library_hero", ".jpg"),
            LogoPath = ResolveOne(game.AppId, "logo", ".png")
        };

        return Task.FromResult(artwork);
    }

    /// <summary>
    /// Returns a local file path if the asset exists in the Steam cache (handling
    /// both the legacy flat naming and the newer per-appid subfolder), otherwise
    /// the CDN URL.
    /// </summary>
    private string ResolveOne(uint appId, string assetName, string extension)
    {
        if (_libraryCacheDir is not null)
        {
            // Legacy flat layout: librarycache/{appid}_{asset}.jpg
            string flat = Path.Combine(_libraryCacheDir, $"{appId}_{assetName}{extension}");
            if (File.Exists(flat))
            {
                return flat;
            }

            // Newer layout: librarycache/{appid}/{asset}.jpg
            string nested = Path.Combine(_libraryCacheDir, appId.ToString(), $"{assetName}{extension}");
            if (File.Exists(nested))
            {
                return nested;
            }
        }

        return $"{CdnBase}/{appId}/{assetName}{extension}";
    }
}
