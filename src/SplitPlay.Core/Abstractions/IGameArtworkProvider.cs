using System.Threading;
using System.Threading.Tasks;
using SplitPlay.Core.Models;

namespace SplitPlay.Core.Abstractions;

/// <summary>
/// Resolves cover art for a game. Implementations should prefer local images
/// already cached by the Steam client, and only fall back to remote sources
/// (e.g. the Steam CDN) when nothing is available locally.
/// </summary>
public interface IGameArtworkProvider
{
    /// <summary>
    /// Resolves artwork for the given game. Never throws for "not found"; returns
    /// <see cref="GameArtwork.Empty"/> instead so the UI can show a placeholder.
    /// </summary>
    Task<GameArtwork> ResolveAsync(SteamGame game, CancellationToken cancellationToken = default);
}
