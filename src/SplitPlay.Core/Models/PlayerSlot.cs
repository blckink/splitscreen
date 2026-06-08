namespace SplitPlay.Core.Models;

/// <summary>
/// One player position in a split-screen session. For the MVP there are always
/// exactly two slots (index 0 and 1). A slot ties a screen region to the
/// controller that should drive that window.
/// </summary>
public sealed class PlayerSlot
{
    /// <summary>Zero-based player index (0 == Player 1).</summary>
    public required int Index { get; init; }

    /// <summary>User-facing label, e.g. "Player 1".</summary>
    public string DisplayName => $"Player {Index + 1}";

    /// <summary>
    /// XInput user index of the controller assigned to this slot, or null if no
    /// controller has been assigned yet.
    /// </summary>
    public int? AssignedControllerIndex { get; set; }
}
