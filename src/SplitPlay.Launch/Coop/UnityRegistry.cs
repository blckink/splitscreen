using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace SplitPlay.Launch.Coop;

/// <summary>
/// Forces a Unity game to boot (and stay) in a windowed mode at an exact size by
/// writing its PlayerPrefs in the registry (HKCU\SOFTWARE\&lt;company&gt;\&lt;product&gt;).
///
/// Unity stores each pref under a name like "Screenmanager Fullscreen mode_h3630240806"
/// where the suffix is a DJB2 hash of the key. We reproduce that exact hash, so we can
/// write the canonical keys deterministically - creating them if the game has not used
/// them yet - instead of guessing from whatever happens to be there.
///
/// Why this matters: Unity applies its saved fullscreen pref a moment after the window
/// appears. If that pref says "fullscreen", Unity resizes its render target to the
/// monitor resolution while our split window stays half-width, so the full-res image is
/// squashed into the window. Pinning the pref to windowed at the split resolution makes
/// the render target match the window - no stretch, no fullscreen jump.
/// </summary>
public static class UnityRegistry
{
    // Unity FullScreenMode enum: 0 ExclusiveFullScreen, 1 FullScreenWindow,
    // 2 MaximizedWindow, 3 Windowed.
    private const int WindowedMode = 3;

    /// <summary>DJB2 hash Unity uses for the registry value-name suffix.</summary>
    public static uint Hash(string name)
    {
        uint hash = 5381;
        foreach (char c in name)
        {
            hash = (hash * 33) ^ c;
        }
        return hash;
    }

    /// <summary>The full registry value name Unity would use for a pref key.</summary>
    private static string ValueName(string key) => $"{key}_h{Hash(key)}";

    public static bool TryApplyWindowed(
        string installDir, string gameName, int width, int height, int x, int y)
    {
        try
        {
            List<string> candidates = BuildProductCandidates(installDir, gameName)
                .Select(Normalize)
                .Where(c => c.Length > 0)
                .ToList();

            using RegistryKey? software = Registry.CurrentUser.OpenSubKey("SOFTWARE");
            if (software is null)
            {
                return false;
            }

            bool applied = false;

            foreach (string company in software.GetSubKeyNames())
            {
                using RegistryKey? companyKey = software.OpenSubKey(company);
                if (companyKey is null)
                {
                    continue;
                }

                foreach (string product in companyKey.GetSubKeyNames())
                {
                    if (!candidates.Contains(Normalize(product)))
                    {
                        continue;
                    }

                    using RegistryKey? prefs = companyKey.OpenSubKey(product, writable: true);
                    if (prefs is null)
                    {
                        continue;
                    }

                    ApplyValues(prefs, width, height, x, y);
                    applied = true;
                }
            }

            return applied;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Registry not accessible: fall back to command-line args + window clamp.
            return false;
        }
    }

    private static void ApplyValues(RegistryKey prefs, int width, int height, int x, int y)
    {
        // Enumerate the keys the game ACTUALLY has and set every resolution/window
        // pref to our split values. Crucially this catches a game's own custom keys
        // (e.g. Roots of Pacha's "resolution_width"/"windowMode"), which it applies a
        // few seconds after launch and which would otherwise override the standard
        // Screenmanager keys and stretch the image.
        foreach (string name in prefs.GetValueNames())
        {
            // Strip the "_h<hash>" suffix Unity appends, then normalise.
            string baseName = name;
            int h = name.LastIndexOf("_h", System.StringComparison.Ordinal);
            if (h > 0)
            {
                baseName = name[..h];
            }
            string key = baseName.ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty);

            if (key.Contains("isfullscreen"))
            {
                SetInt(prefs, name, 0);
            }
            else if (key.Contains("fullscreenmode") || key.Contains("windowmode"))
            {
                SetInt(prefs, name, WindowedMode);
            }
            else if (key.Contains("usenative"))
            {
                SetInt(prefs, name, 0);
            }
            else if (key.Contains("resolution") && key.Contains("width"))
            {
                SetInt(prefs, name, width);
            }
            else if (key.Contains("resolution") && key.Contains("height"))
            {
                SetInt(prefs, name, height);
            }
            else if (key.Contains("windowpositionx"))
            {
                SetInt(prefs, name, x);
            }
            else if (key.Contains("windowpositiony"))
            {
                SetInt(prefs, name, y);
            }
        }

        // Also ensure the standard keys exist even if the game has not written them
        // yet (created with the correct Unity-hashed names).
        SetInt(prefs, ValueName("Screenmanager Fullscreen mode"), WindowedMode);
        SetInt(prefs, ValueName("Screenmanager Resolution Width"), width);
        SetInt(prefs, ValueName("Screenmanager Resolution Height"), height);
    }

    private static List<string> BuildProductCandidates(string installDir, string gameName)
    {
        var candidates = new List<string> { gameName };

        try
        {
            foreach (string dataDir in Directory.EnumerateDirectories(installDir, "*_Data"))
            {
                string name = Path.GetFileName(dataDir);
                if (name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(name[..^"_Data".Length]);
                }
            }
        }
        catch (IOException)
        {
            // Ignore.
        }

        return candidates;
    }

    /// <summary>Lower-cases and strips non-alphanumerics so "Roots of Pacha" == "RootsOfPacha".</summary>
    private static string Normalize(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>
    /// Writes an int under a full registry value name, preserving the existing
    /// registry kind (DWord/QWord) when the value is already present.
    /// </summary>
    private static void SetInt(RegistryKey prefs, string name, int value)
    {
        try
        {
            RegistryValueKind kind = RegistryValueKind.DWord;
            if (prefs.GetValue(name) is not null)
            {
                kind = prefs.GetValueKind(name);
            }

            if (kind == RegistryValueKind.QWord)
            {
                prefs.SetValue(name, (long)value, RegistryValueKind.QWord);
            }
            else
            {
                prefs.SetValue(name, value, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Skip values we cannot write.
        }
    }
}
