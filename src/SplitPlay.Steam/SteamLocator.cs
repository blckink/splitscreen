using System;
using System.IO;
using Microsoft.Win32;

namespace SplitPlay.Steam;

/// <summary>
/// Locates the Steam installation directory on this machine. Tries the registry
/// first (the authoritative source), then a couple of well-known fallback paths.
/// </summary>
public static class SteamLocator
{
    /// <summary>
    /// Returns the absolute path to the Steam install directory, or null if Steam
    /// could not be located.
    /// </summary>
    public static string? FindSteamPath()
    {
        string? fromRegistry = ReadSteamPathFromRegistry();
        if (IsValidSteamDir(fromRegistry))
        {
            return fromRegistry;
        }

        foreach (string candidate in GetFallbackCandidates())
        {
            if (IsValidSteamDir(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ReadSteamPathFromRegistry()
    {
        // Per-user install path (most reliable), then the machine-wide key.
        string? path = Registry.CurrentUser
            .OpenSubKey(@"Software\Valve\Steam")?
            .GetValue("SteamPath") as string;

        if (string.IsNullOrEmpty(path))
        {
            path = Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")?
                .GetValue("InstallPath") as string;
        }

        // The registry stores forward slashes; normalize for the file system.
        return string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path);
    }

    private static string[] GetFallbackCandidates()
    {
        string programFilesX86 =
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string programFiles =
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        return new[]
        {
            Path.Combine(programFilesX86, "Steam"),
            Path.Combine(programFiles, "Steam"),
            @"C:\Steam"
        };
    }

    /// <summary>A directory is a valid Steam root if it contains a steamapps folder.</summary>
    private static bool IsValidSteamDir(string? path) =>
        !string.IsNullOrEmpty(path) &&
        Directory.Exists(Path.Combine(path, "steamapps"));
}
