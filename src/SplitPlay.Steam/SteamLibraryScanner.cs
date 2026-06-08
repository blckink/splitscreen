using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SplitPlay.Core.Abstractions;
using SplitPlay.Core.Models;

namespace SplitPlay.Steam;

/// <summary>
/// Default <see cref="ISteamLibraryScanner"/>. Locates Steam, walks every library
/// folder and parses each app manifest. Runs the (IO-bound) work on a background
/// thread so the UI thread is never blocked.
/// </summary>
public sealed class SteamLibraryScanner : ISteamLibraryScanner
{
    public Task<IReadOnlyList<SteamGame>> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(cancellationToken), cancellationToken);

    private static IReadOnlyList<SteamGame> Scan(CancellationToken cancellationToken)
    {
        string? steamPath = SteamLocator.FindSteamPath();
        if (steamPath is null)
        {
            return Array.Empty<SteamGame>();
        }

        var games = new List<SteamGame>();

        foreach (string library in LibraryFoldersParser.GetLibraryPaths(steamPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps))
            {
                continue;
            }

            foreach (string manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // One bad manifest must never abort the whole scan.
                try
                {
                    SteamGame? game = AppManifestParser.TryParse(manifest, library);
                    if (game is not null)
                    {
                        games.Add(game);
                    }
                }
                catch (IOException)
                {
                    // File locked/unreadable: skip it.
                }
            }
        }

        // De-duplicate (a game can appear in more than one library after moves)
        // and present alphabetically for a tidy grid.
        return games
            .GroupBy(g => g.AppId)
            .Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
