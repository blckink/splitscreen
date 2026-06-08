using System.Threading;
using System.Threading.Tasks;
using SplitPlay.Core.Models;

namespace SplitPlay.Core.Abstractions;

/// <summary>
/// Loads and saves per-game <see cref="GameProfile"/> settings. A profile is
/// created with defaults the first time a game is opened, and persisted whenever
/// the user changes a setting.
/// </summary>
public interface IGameProfileStore
{
    /// <summary>
    /// Returns the saved profile for the app id, or a fresh default profile if
    /// none has been saved yet. Never returns null.
    /// </summary>
    Task<GameProfile> LoadAsync(uint appId, CancellationToken cancellationToken = default);

    /// <summary>Persists the profile.</summary>
    Task SaveAsync(GameProfile profile, CancellationToken cancellationToken = default);
}
