using System;
using System.IO;

namespace SplitPlay.Launch.Coop;

/// <summary>
/// Locates the bundled Steam emulator (gbe_fork / Goldberg) that the app ships in
/// its Redist folder. The CI build downloads a pinned gbe_fork release and places
/// the DLLs here, so the user gets co-op out of the box without sourcing anything.
/// Layout: &lt;app&gt;/Redist/Goldberg/x64/steam_api64.dll and x86/steam_api.dll.
/// </summary>
public static class GoldbergLocator
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "Redist", "Goldberg");

    /// <summary>Path to the 64-bit emulator DLL, or null if not bundled.</summary>
    public static string? Api64 => Existing(Path.Combine(Root, "x64", "steam_api64.dll"));

    /// <summary>Path to the 32-bit emulator DLL, or null if not bundled.</summary>
    public static string? Api32 => Existing(Path.Combine(Root, "x86", "steam_api.dll"));

    /// <summary>
    /// True if the emulator DLLs the recipe actually needs are present.
    /// </summary>
    public static bool CanServe(CoopRecipe recipe)
    {
        if (recipe.SteamApi64RelPath is not null && Api64 is null)
        {
            return false;
        }

        if (recipe.SteamApi32RelPath is not null && Api32 is null)
        {
            return false;
        }

        return recipe.UsesSteam;
    }

    private static string? Existing(string path) => File.Exists(path) ? path : null;
}
