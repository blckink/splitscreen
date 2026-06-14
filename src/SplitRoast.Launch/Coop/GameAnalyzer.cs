using System;
using System.IO;
using System.Linq;

namespace SplitRoast.Launch.Coop;

/// <summary>
/// Inspects a game's install folder to auto-derive a <see cref="CoopRecipe"/>:
/// the engine, the executable and the Steam API DLLs. This is what lets the tool
/// support many games automatically instead of needing a hand-written handler.
/// </summary>
public static class GameAnalyzer
{
    public static CoopRecipe Analyze(uint appId, string installDir, string exePath)
    {
        EngineType engine = DetectEngine(installDir);
        (string? api64, string? api32) = FindSteamApi(installDir);
        bool hasSteamDrm = SteamStubDetector.IsSteamDrmProtected(exePath);
        OnlineSdk sdks = DetectOnlineSdks(installDir, api64, api32);

        return new CoopRecipe
        {
            AppId = appId,
            SourceInstallDir = installDir,
            SourceExePath = exePath,
            Engine = engine,
            SteamApi64RelPath = api64,
            SteamApi32RelPath = api32,
            DetectedSdks = sdks,
            HasSteamDrm = hasSteamDrm
        };
    }

    private static EngineType DetectEngine(string installDir)
    {
        // Unity: ships UnityPlayer.dll and/or a "<Game>_Data" folder with the
        // engine's globalgamemanagers asset.
        if (File.Exists(Path.Combine(installDir, "UnityPlayer.dll")))
        {
            return EngineType.Unity;
        }

        try
        {
            foreach (string dir in Directory.EnumerateDirectories(installDir, "*_Data"))
            {
                if (File.Exists(Path.Combine(dir, "globalgamemanagers")) ||
                    File.Exists(Path.Combine(dir, "data.unity3d")))
                {
                    return EngineType.Unity;
                }
            }

            // Unreal: a Binaries/Win64 layout under an Engine or game module.
            if (Directory.EnumerateDirectories(installDir, "Binaries", SearchOption.AllDirectories).Any())
            {
                return EngineType.Unreal;
            }
        }
        catch (IOException)
        {
            // Ignore unreadable folders during detection.
        }

        return EngineType.Unknown;
    }

    private static (string? Api64, string? Api32) FindSteamApi(string installDir)
    {
        string? api64 = FindFirstRelative(installDir, "steam_api64.dll");
        string? api32 = FindFirstRelative(installDir, "steam_api.dll");
        return (api64, api32);
    }

    /// <summary>
    /// Detects which online/multiplayer SDKs the game ships, from the well-known
    /// runtime DLL names each one drops next to the game. This is the signal that
    /// tells the engine which network emulator a second instance needs - no per-game
    /// handler required.
    /// </summary>
    private static OnlineSdk DetectOnlineSdks(string installDir, string? steamApi64, string? steamApi32)
    {
        OnlineSdk sdks = OnlineSdk.None;

        if (steamApi64 is not null || steamApi32 is not null)
        {
            sdks |= OnlineSdk.Steam;
        }

        // Epic Online Services ships EOSSDK-Win64-Shipping.dll / EOSSDK-Win32-Shipping.dll.
        if (ContainsAnyFile(installDir, "EOSSDK-Win64-Shipping.dll", "EOSSDK-Win32-Shipping.dll"))
        {
            sdks |= OnlineSdk.Epic;
        }

        // GOG Galaxy ships Galaxy64.dll / Galaxy.dll (the peer/identity SDK).
        if (ContainsAnyFile(installDir, "Galaxy64.dll", "Galaxy.dll"))
        {
            sdks |= OnlineSdk.Galaxy;
        }

        return sdks;
    }

    private static bool ContainsAnyFile(string root, params string[] fileNames)
    {
        foreach (string name in fileNames)
        {
            if (FindFirstRelative(root, name) is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindFirstRelative(string root, string fileName)
    {
        try
        {
            string? hit = Directory
                .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
                .FirstOrDefault();
            return hit is null ? null : Path.GetRelativePath(root, hit);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
