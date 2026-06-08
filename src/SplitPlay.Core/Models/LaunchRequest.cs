using System.Collections.Generic;

namespace SplitPlay.Core.Models;

/// <summary>
/// A fully-resolved description of a split-screen session, ready to hand to the
/// launch engine. Everything the engine needs is pre-computed here so the engine
/// itself stays free of UI/profile concerns.
/// </summary>
public sealed class LaunchRequest
{
    /// <summary>The game to launch.</summary>
    public required SteamGame Game { get; init; }

    /// <summary>The profile/settings selected by the user.</summary>
    public required GameProfile Profile { get; init; }

    /// <summary>
    /// One entry per player, in player order. Each pairs a target screen region
    /// with the controller that must exclusively drive that window.
    /// </summary>
    public required IReadOnlyList<PlayerLaunchTarget> Targets { get; init; }
}

/// <summary>
/// The concrete placement + input routing for a single player instance.
/// </summary>
/// <param name="Player">The player slot this target belongs to.</param>
/// <param name="Region">Where the window should be positioned and sized.</param>
/// <param name="ControllerIndex">XInput index that must exclusively drive it.</param>
public readonly record struct PlayerLaunchTarget(
    PlayerSlot Player,
    ScreenRegion Region,
    int ControllerIndex);
