using System;
using System.Runtime.InteropServices;

namespace SplitPlay.Launch.Native;

/// <summary>Kernel32 calls used for fast, admin-free file mirroring (hard links).</summary>
internal static class Kernel32
{
    /// <summary>
    /// Creates a hard link (a second directory entry pointing at the same file
    /// content). Works without administrator rights as long as link and target are
    /// on the same NTFS volume, and uses no extra disk space.
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreateHardLinkW(
        string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}
