using System.IO;

namespace SplitPlay.Launch.Coop;

/// <summary>
/// Turns a mirrored instance into a self-contained, Steam-free copy by dropping in
/// the gbe_fork/Goldberg emulator. It replaces the game's steam_api DLL(s), writes
/// steam_appid.txt, generates steam_interfaces.txt from the original DLL, and
/// creates a per-instance steam_settings folder.
///
/// The configuration mirrors what Nucleus Co-op's Goldberg integration does for
/// online/relay games (verified against its source + the Roots of Pacha handler):
///   - a DISTINCT, stable SteamID64 per instance, so the game sees two different
///     players AND keeps each player's savegame apart (many games save under a
///     per-SteamID folder);
///   - disable_lan_only, so the emulator reports as fully online and the game lets
///     an online/relay (e.g. Photon) co-op session start;
///   - steam_interfaces.txt, so the emulator exposes the exact interface versions
///     the game uses - including the networking ones, without which connecting can
///     throw.
/// Classic Goldberg LAN settings (shared port + localhost broadcast) are also
/// written so games that DO use Steam's own LAN/P2P find each other on one PC.
/// </summary>
public static class SteamEmulator
{
    /// <summary>Shared discovery port for games that use Steam's own LAN/P2P.</summary>
    private const int ListenPort = 47584;

    /// <summary>Base of the individual SteamID64 range (STEAM_0:0:1 = ...728 + 1).</summary>
    private const long SteamId64Base = 76561197960265728L;

    /// <summary>
    /// Applies the emulator to a prepared instance directory.
    /// </summary>
    /// <returns>True if at least one steam_api DLL was replaced.</returns>
    public static bool Apply(string instanceDir, CoopRecipe recipe, int playerIndex)
    {
        bool applied = false;

        if (recipe.SteamApi64RelPath is not null && GoldbergLocator.Api64 is not null)
        {
            string dest = Path.Combine(instanceDir, recipe.SteamApi64RelPath);
            // Generate interfaces from the original (real Steam) DLL, never the
            // instance copy - that may already be a Goldberg DLL from a prior run.
            SteamInterfaces.Generate(
                Path.Combine(recipe.SourceInstallDir, recipe.SteamApi64RelPath),
                Path.GetDirectoryName(dest)!);
            InstanceMirror.ReplaceWithCopy(dest, GoldbergLocator.Api64);
            WriteSteamSettings(Path.GetDirectoryName(dest)!, recipe.AppId, playerIndex);
            applied = true;
        }

        if (recipe.SteamApi32RelPath is not null && GoldbergLocator.Api32 is not null)
        {
            string dest = Path.Combine(instanceDir, recipe.SteamApi32RelPath);
            SteamInterfaces.Generate(
                Path.Combine(recipe.SourceInstallDir, recipe.SteamApi32RelPath),
                Path.GetDirectoryName(dest)!);
            InstanceMirror.ReplaceWithCopy(dest, GoldbergLocator.Api32);
            WriteSteamSettings(Path.GetDirectoryName(dest)!, recipe.AppId, playerIndex);
            applied = true;
        }

        // The appid file is also looked for next to the executable.
        string exeDir = Path.GetDirectoryName(Path.Combine(instanceDir, recipe.ExeRelativePath))!;
        WriteTextFile(Path.Combine(exeDir, "steam_appid.txt"), recipe.AppId.ToString());

        return applied;
    }

    private static void WriteSteamSettings(string apiDir, uint appId, int playerIndex)
    {
        WriteTextFile(Path.Combine(apiDir, "steam_appid.txt"), appId.ToString());

        // disable_lan_only lives next to the DLL (this is where the RoP handler puts
        // it); presence of the file enables it.
        WriteTextFile(Path.Combine(apiDir, "disable_lan_only.txt"), string.Empty);

        string settings = Path.Combine(apiDir, "steam_settings");
        Directory.CreateDirectory(settings);

        string accountName = $"Player{playerIndex + 1}";
        // Distinct, STABLE id per player: stable so the per-SteamID save folder is
        // the same every session (savegame persistence), distinct so the two
        // instances are two different players.
        long steamId = SteamId64Base + 1 + playerIndex;

        // Classic Goldberg text files (gbe_fork keeps reading these for compat).
        WriteTextFile(Path.Combine(settings, "account_name.txt"), accountName);
        WriteTextFile(Path.Combine(settings, "force_account_name.txt"), accountName);
        WriteTextFile(Path.Combine(settings, "user_steam_id.txt"), steamId.ToString());
        WriteTextFile(Path.Combine(settings, "language.txt"), "english");
        WriteTextFile(Path.Combine(settings, "disable_overlay.txt"), string.Empty);
        WriteTextFile(Path.Combine(settings, "disable_lan_only.txt"), string.Empty);

        // Steam's own LAN/P2P discovery (ignored by games that use an external
        // service like Photon, needed by those that use Steam networking).
        WriteTextFile(Path.Combine(settings, "listen_port.txt"), ListenPort.ToString());
        WriteTextFile(Path.Combine(settings, "custom_broadcasts.txt"), "127.0.0.1");

        // gbe_fork ini layout (newer versions).
        WriteTextFile(Path.Combine(settings, "configs.main.ini"),
            "[main::connectivity]\n" +
            $"listen_port={ListenPort}\n" +
            "disable_networking=0\n" +
            "disable_lan_only=1\n" +
            "new_app_ticket=1\n" +
            "gc_token=1\n" +
            "share_leaderboards_over_network=1\n" +
            "\n[main::general]\n" +
            "enable_account_avatar=0\n");
        WriteTextFile(Path.Combine(settings, "configs.user.ini"),
            "[user::general]\n" +
            $"account_name={accountName}\n" +
            $"account_steamid={steamId}\n" +
            "language=english\n");
    }

    private static void WriteTextFile(string path, string content)
    {
        string? dir = Path.GetDirectoryName(path);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, content);
    }
}
