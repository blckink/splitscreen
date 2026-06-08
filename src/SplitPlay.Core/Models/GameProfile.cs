namespace SplitPlay.Core.Models;

/// <summary>
/// Per-game user configuration that SplitPlay persists between sessions.
/// This is intentionally separate from <see cref="SteamGame"/> (which is
/// re-discovered on every launch) so settings survive re-scans and updates.
///
/// The design goal versus Nucleus Co-op: instead of shipping per-game "handler"
/// script files that users must download, every option a game needs is captured
/// here and edited through the UI. Sensible values are auto-detected on first
/// run and can then be tweaked.
/// </summary>
public sealed class GameProfile
{
    /// <summary>Steam app id this profile belongs to.</summary>
    public required uint AppId { get; init; }

    /// <summary>How the screen is divided. Defaults to a vertical (left/right) split.</summary>
    public SplitOrientation Orientation { get; set; } = SplitOrientation.Vertical;

    /// <summary>
    /// Index of the display to use for the split session. Null means "use the
    /// primary monitor". (Multi-monitor spanning is a future enhancement.)
    /// </summary>
    public int? TargetDisplayIndex { get; set; }

    /// <summary>
    /// Controller assignment per player slot. Index 0 -> Player 1, index 1 ->
    /// Player 2. Each value is an XInput user index, or null if unassigned.
    /// </summary>
    public int?[] ControllerAssignments { get; set; } = new int?[2];

    /// <summary>
    /// Future-facing instance strategy (which trick is used to run two copies).
    /// Stored now so the UI and persistence are ready; the launch engine treats
    /// anything beyond <see cref="InstanceStrategy.Auto"/> as not-yet-implemented.
    /// </summary>
    public InstanceStrategy InstanceStrategy { get; set; } = InstanceStrategy.Auto;

    /// <summary>
    /// Optional override for the executable to launch, relative to the install
    /// dir. Auto-detected when null. Lets advanced users fix edge cases without
    /// editing files on disk.
    /// </summary>
    public string? ExecutableOverride { get; set; }

    /// <summary>
    /// When true, the launch engine opens neutral placeholder test windows instead
    /// of the real game. Useful to verify the split layout, display choice and
    /// window placement safely without starting the game.
    /// </summary>
    public bool UseTestWindows { get; set; }

    /// <summary>
    /// When true (default), each game instance is launched through the XInput proxy
    /// so it only sees its assigned controller. Can be turned off for the rare game
    /// that misbehaves with the proxy.
    /// </summary>
    public bool IsolateControllers { get; set; } = true;

    /// <summary>Creates a fresh profile with default settings for an app id.</summary>
    public static GameProfile CreateDefault(uint appId) => new() { AppId = appId };
}
