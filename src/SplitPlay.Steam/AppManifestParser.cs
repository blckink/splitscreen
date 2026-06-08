using System.IO;
using SplitPlay.Core.Models;
using SplitPlay.Steam.Vdf;

namespace SplitPlay.Steam;

/// <summary>
/// Parses a single <c>appmanifest_*.acf</c> file into a <see cref="SteamGame"/>.
/// </summary>
public static class AppManifestParser
{
    /// <summary>
    /// Attempts to read a manifest file. Returns null if the file is malformed or
    /// the game is not actually installed (e.g. only partially downloaded).
    /// </summary>
    /// <param name="manifestPath">Full path to an appmanifest_*.acf file.</param>
    /// <param name="libraryPath">The library root that owns this manifest.</param>
    public static SteamGame? TryParse(string manifestPath, string libraryPath)
    {
        VdfNode root = VdfParser.Parse(File.ReadAllText(manifestPath));
        VdfNode? state = root.GetChild("AppState");
        if (state is null)
        {
            return null;
        }

        string? appIdText = state.GetValue("appid");
        string? name = state.GetValue("name");
        string? installDirName = state.GetValue("installdir");

        if (!uint.TryParse(appIdText, out uint appId) ||
            string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(installDirName))
        {
            return null;
        }

        string installDir =
            Path.Combine(libraryPath, "steamapps", "common", installDirName);

        // Skip entries whose install folder is missing (not really installed).
        if (!Directory.Exists(installDir))
        {
            return null;
        }

        long? size = long.TryParse(state.GetValue("SizeOnDisk"), out long s) ? s : null;

        return new SteamGame
        {
            AppId = appId,
            Name = name,
            InstallDir = installDir,
            LibraryPath = libraryPath,
            SizeOnDiskBytes = size
        };
    }
}
