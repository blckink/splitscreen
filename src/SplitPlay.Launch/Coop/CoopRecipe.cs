using System.IO;

namespace SplitPlay.Launch.Coop;

/// <summary>
/// A fully auto-derived description of how to run a game as local co-op: which
/// engine it is, where its executable and Steam API DLLs are, and how to ask it
/// for a borderless window of a given size. Produced by <see cref="GameAnalyzer"/>
/// so no per-game script/handler is needed for the common cases.
/// </summary>
public sealed class CoopRecipe
{
    public required uint AppId { get; init; }

    /// <summary>The game's original install directory.</summary>
    public required string SourceInstallDir { get; init; }

    /// <summary>Absolute path to the executable inside <see cref="SourceInstallDir"/>.</summary>
    public required string SourceExePath { get; init; }

    public required EngineType Engine { get; init; }

    /// <summary>Path of steam_api64.dll relative to the install dir, or null.</summary>
    public string? SteamApi64RelPath { get; init; }

    /// <summary>Path of steam_api.dll relative to the install dir, or null.</summary>
    public string? SteamApi32RelPath { get; init; }

    /// <summary>True if the game ships a Steam API DLL (i.e. it is a Steam game).</summary>
    public bool UsesSteam => SteamApi64RelPath is not null || SteamApi32RelPath is not null;

    /// <summary>The executable path relative to the install dir.</summary>
    public string ExeRelativePath => Path.GetRelativePath(SourceInstallDir, SourceExePath);

    /// <summary>
    /// Builds the command-line arguments that make the game open as a borderless
    /// window of the given size. We then position the window ourselves, so no
    /// per-game window-position registry tweaks are required.
    /// </summary>
    public string BuildWindowArgs(int width, int height) => Engine switch
    {
        // Unity standalone player switches (stable across Unity games).
        EngineType.Unity =>
            $"-screen-fullscreen 0 -popupwindow -screen-width {width} -screen-height {height}",
        _ => string.Empty
    };
}
