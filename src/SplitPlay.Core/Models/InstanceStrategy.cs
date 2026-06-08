namespace SplitPlay.Core.Models;

/// <summary>
/// Describes how a second running copy of the game is achieved. Captured in the
/// profile so the planned strategies have a stable home; only <see cref="Auto"/>
/// is exercised by the MVP launch stub.
/// </summary>
public enum InstanceStrategy
{
    /// <summary>
    /// Let SplitPlay pick the best available approach automatically based on
    /// what it detects about the game. Default for the MVP.
    /// </summary>
    Auto,

    /// <summary>
    /// One Steam account, the game files are mirrored and a network/lobby
    /// emulator (e.g. Goldberg / Nemirtingas) connects the two copies.
    /// </summary>
    MirroredCopyWithEmulator,

    /// <summary>
    /// Two genuine Steam instances with two real accounts logged in, behaving as
    /// if there were two separate PCs.
    /// </summary>
    DualSteamAccounts
}
