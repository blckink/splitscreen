using System;
using System.IO;
using SplitPlay.Launch.Native;

namespace SplitPlay.Launch.Coop;

/// <summary>
/// Creates a per-player mirror of the game folder so each instance can have its
/// own Steam emulator config, controller proxy and window settings. Files are
/// hard-linked (instant, admin-free, ~no extra disk) rather than copied. The
/// mirror lives next to the game on the same volume so hard links are always
/// possible. Re-launches reuse the existing mirror.
/// </summary>
public static class InstanceMirror
{
    /// <summary>Returns the root folder that holds all instances for a game.</summary>
    public static string GetInstancesRoot(string libraryPath, uint appId) =>
        Path.Combine(libraryPath, "SplitPlay_Instances", appId.ToString());

    /// <summary>Returns the folder for one player's instance.</summary>
    public static string GetInstanceDir(string libraryPath, uint appId, int playerIndex) =>
        Path.Combine(GetInstancesRoot(libraryPath, appId), $"p{playerIndex + 1}");

    /// <summary>
    /// Mirrors <paramref name="sourceDir"/> into <paramref name="instanceDir"/> using
    /// hard links (falling back to a copy if a hard link can't be made). Existing
    /// files are left in place so repeat launches are fast.
    /// </summary>
    public static void Mirror(string sourceDir, string instanceDir)
    {
        Directory.CreateDirectory(instanceDir);

        // Recreate the directory structure first.
        foreach (string dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(instanceDir, rel));
        }

        // Link every file.
        foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(sourceDir, file);
            string dest = Path.Combine(instanceDir, rel);

            if (File.Exists(dest))
            {
                continue; // Reuse what is already there.
            }

            if (!Kernel32.CreateHardLinkW(dest, file, IntPtr.Zero))
            {
                // Different volume or other restriction: fall back to a real copy.
                try
                {
                    File.Copy(file, dest, overwrite: true);
                }
                catch (IOException)
                {
                    // Skip files we genuinely cannot mirror; the game usually still runs.
                }
            }
        }
    }

    /// <summary>
    /// Replaces a (possibly hard-linked) file in the instance with an independent
    /// real copy, so writing to it never affects the original game files.
    /// </summary>
    public static void ReplaceWithCopy(string instanceFilePath, string sourceFilePath)
    {
        string? dir = Path.GetDirectoryName(instanceFilePath);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(instanceFilePath))
        {
            File.Delete(instanceFilePath); // Removes the hard link, keeps the original.
        }

        File.Copy(sourceFilePath, instanceFilePath, overwrite: true);
    }
}
