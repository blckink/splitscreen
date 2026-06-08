using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SplitPlay.Core.Models;

namespace SplitPlay.Core.Abstractions;

/// <summary>
/// Discovers installed Steam games on the local machine. Implementations locate
/// the Steam install, enumerate every library folder and parse the app
/// manifests. Artwork resolution is intentionally a separate concern
/// (see <see cref="IGameArtworkProvider"/>) to keep scanning fast.
/// </summary>
public interface ISteamLibraryScanner
{
    /// <summary>
    /// Scans all Steam libraries and returns the installed games.
    /// </summary>
    /// <returns>The discovered games, ordered by name. Empty if Steam is not found.</returns>
    Task<IReadOnlyList<SteamGame>> ScanAsync(CancellationToken cancellationToken = default);
}
