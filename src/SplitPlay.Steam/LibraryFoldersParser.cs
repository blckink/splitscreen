using System.Collections.Generic;
using System.IO;
using SplitPlay.Steam.Vdf;

namespace SplitPlay.Steam;

/// <summary>
/// Reads <c>steamapps/libraryfolders.vdf</c> to discover every Steam library
/// folder on the machine (games can live on multiple drives).
/// </summary>
public static class LibraryFoldersParser
{
    /// <summary>
    /// Returns the absolute paths of all Steam library roots (each contains a
    /// "steamapps" subfolder). Always includes the main Steam path itself.
    /// </summary>
    public static IReadOnlyList<string> GetLibraryPaths(string steamPath)
    {
        var paths = new List<string> { steamPath };

        string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
        {
            return Deduplicate(paths);
        }

        VdfNode root = VdfParser.Parse(File.ReadAllText(vdfPath));

        // The file is wrapped in a top-level "libraryfolders" block whose children
        // are numbered ("0", "1", ...). Each child carries a "path" value.
        VdfNode? folders = root.GetChild("libraryfolders") ?? root;
        foreach (VdfNode entry in folders.Children.Values)
        {
            string? path = entry.GetValue("path");
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }

        return Deduplicate(paths);
    }

    private static IReadOnlyList<string> Deduplicate(List<string> paths)
    {
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (string path in paths)
        {
            string full = Path.GetFullPath(path);
            if (Directory.Exists(full) && seen.Add(full))
            {
                result.Add(full);
            }
        }

        return result;
    }
}
