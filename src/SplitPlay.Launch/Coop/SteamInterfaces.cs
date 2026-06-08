using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SplitPlay.Launch.Coop;

/// <summary>
/// Generates the <c>steam_interfaces.txt</c> file the Goldberg/gbe_fork emulator
/// needs to expose the exact Steamworks interface versions a game uses (e.g.
/// <c>SteamNetworkingSockets012</c>, <c>SteamUser023</c>). Without it the emulator
/// may fail to provide some interfaces - notably the networking ones - which makes
/// online/relay-based games throw on connect. We reproduce what Goldberg's own
/// "generate_interfaces_file" tool does: scan the original steam_api DLL for the
/// known interface-version strings. This is generic across games.
/// </summary>
public static class SteamInterfaces
{
    // Interface-name prefixes Steamworks uses; the DLL contains the full versioned
    // strings (prefix + version) which we extract verbatim.
    private static readonly string[] Prefixes =
    {
        "SteamClient",
        "SteamGameServerStats",
        "SteamGameServer",
        "SteamUser",
        "SteamFriends",
        "SteamUtils",
        "SteamMatchMakingServers",
        "SteamMatchMaking",
        "STEAMUSERSTATS_INTERFACE_VERSION",
        "STEAMAPPS_INTERFACE_VERSION",
        "SteamNetworkingSockets",
        "SteamNetworkingUtils",
        "SteamNetworkingMessages",
        "SteamNetworking",
        "STEAMREMOTESTORAGE_INTERFACE_VERSION",
        "STEAMSCREENSHOTS_INTERFACE_VERSION",
        "STEAMHTTP_INTERFACE_VERSION",
        "STEAMUNIFIEDMESSAGES_INTERFACE_VERSION",
        "STEAMCONTROLLER_INTERFACE",
        "SteamController",
        "STEAMUGC_INTERFACE_VERSION",
        "STEAMAPPLIST_INTERFACE_VERSION",
        "STEAMMUSICREMOTE_INTERFACE_VERSION",
        "STEAMMUSIC_INTERFACE_VERSION",
        "STEAMHTMLSURFACE_INTERFACE_VERSION_",
        "STEAMINVENTORY_INTERFACE_V",
        "SteamInventory",
        "STEAMVIDEO_INTERFACE_V",
        "SteamVideo",
        "STEAMPARENTALSETTINGS_INTERFACE_VERSION",
        "SteamGameSearch",
        "SteamParties",
        "SteamRemotePlay",
        "SteamInput",
    };

    /// <summary>
    /// Scans <paramref name="dllPath"/> for interface-version strings and writes
    /// them to <c>steam_interfaces.txt</c> in <paramref name="outputDir"/>.
    /// </summary>
    /// <returns>True if at least one interface was found and written.</returns>
    public static bool Generate(string dllPath, string outputDir)
    {
        try
        {
            if (!File.Exists(dllPath))
            {
                return false;
            }

            byte[] bytes = File.ReadAllBytes(dllPath);
            var found = new SortedSet<string>(StringComparer.Ordinal);

            foreach (string s in ExtractAsciiStrings(bytes, minLength: 8))
            {
                foreach (string prefix in Prefixes)
                {
                    if (s.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        found.Add(s);
                        break;
                    }
                }
            }

            if (found.Count == 0)
            {
                return false;
            }

            Directory.CreateDirectory(outputDir);
            File.WriteAllText(
                Path.Combine(outputDir, "steam_interfaces.txt"),
                string.Join("\n", found) + "\n");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Yields null-terminated printable-ASCII runs of at least the given length.</summary>
    private static IEnumerable<string> ExtractAsciiStrings(byte[] data, int minLength)
    {
        var sb = new StringBuilder();
        foreach (byte b in data)
        {
            if (b >= 0x20 && b < 0x7F)
            {
                sb.Append((char)b);
                continue;
            }

            if (sb.Length >= minLength)
            {
                yield return sb.ToString();
            }
            sb.Clear();
        }

        if (sb.Length >= minLength)
        {
            yield return sb.ToString();
        }
    }
}
