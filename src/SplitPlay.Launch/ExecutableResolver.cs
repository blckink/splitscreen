using System;
using System.IO;
using System.Linq;

namespace SplitPlay.Launch;

/// <summary>
/// Picks which executable to launch for a game. Honors an explicit override, then
/// falls back to a heuristic over the install directory. This is intentionally
/// simple for the MVP; a later version can read Steam's launch configuration for
/// perfect accuracy.
/// </summary>
public static class ExecutableResolver
{
    // Helper executables that are almost never the game itself.
    private static readonly string[] Blacklist =
    {
        "unins", "uninstall", "redist", "vcredist", "vc_redist", "dxsetup", "directx",
        "dotnet", "setup", "crashreport", "crashhandler", "crashpad", "report",
        "config", "settings", "launcher_old", "cleanup", "touchup", "easyanticheat",
        "battleye", "be_service", "activation", "diagnostic"
    };

    /// <summary>
    /// Resolves the absolute path of the executable to launch, or null if nothing
    /// suitable was found.
    /// </summary>
    /// <param name="installDir">The game's install directory.</param>
    /// <param name="gameName">The Steam display name (used for matching).</param>
    /// <param name="overridePath">Optional path relative to install dir, or absolute.</param>
    public static string? Resolve(string installDir, string gameName, string? overridePath)
    {
        // 1. Explicit override wins, whether absolute or relative to the install dir.
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            string candidate = Path.IsPathRooted(overridePath)
                ? overridePath
                : Path.Combine(installDir, overridePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        if (!Directory.Exists(installDir))
        {
            return null;
        }

        // 2. Collect candidate executables (limit depth to keep it fast).
        var executables = EnumerateExecutables(installDir)
            .Where(path => !IsBlacklisted(Path.GetFileNameWithoutExtension(path)))
            .ToList();

        if (executables.Count == 0)
        {
            return null;
        }

        // 3. Prefer an exe whose name resembles the game or the install folder.
        string normalizedGame = Normalize(gameName);
        string normalizedDir = Normalize(new DirectoryInfo(installDir).Name);

        string? byName = executables.FirstOrDefault(path =>
        {
            string name = Normalize(Path.GetFileNameWithoutExtension(path));
            return name.Length > 2 &&
                   (normalizedGame.Contains(name) || name.Contains(normalizedDir) ||
                    normalizedDir.Contains(name));
        });

        if (byName is not null)
        {
            return byName;
        }

        // 4. Otherwise the largest executable is the most likely game binary.
        return executables
            .OrderByDescending(path => new FileInfo(path).Length)
            .First();
    }

    private static System.Collections.Generic.IEnumerable<string> EnumerateExecutables(string root)
    {
        // Top two directory levels cover the vast majority of game layouts without
        // walking huge trees.
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 2,
            IgnoreInaccessible = true
        };

        try
        {
            return Directory.EnumerateFiles(root, "*.exe", options);
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsBlacklisted(string fileName)
    {
        string lower = fileName.ToLowerInvariant();
        return Blacklist.Any(token => lower.Contains(token));
    }

    /// <summary>Lowercases and strips non-alphanumerics for fuzzy name matching.</summary>
    private static string Normalize(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
