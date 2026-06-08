using System;
using SplitPlay.Core.Models;
using SplitPlay.Launch.Native;

namespace SplitPlay.Launch;

/// <summary>
/// Repositions and resizes a game window into a target <see cref="ScreenRegion"/>
/// and strips its border/title bar so split-screen views sit flush against each
/// other. This is the OS-level half of the launch pipeline; the engine decides
/// <em>which</em> window goes where, this class performs the move.
/// </summary>
public sealed class WindowManager
{
    /// <summary>
    /// Makes the window borderless and moves it to exactly fill the region.
    /// </summary>
    /// <param name="windowHandle">HWND of the target game window.</param>
    /// <param name="region">Where the window should end up, in desktop pixels.</param>
    /// <returns>True on success, false if the handle was not a valid window.</returns>
    public bool PlaceBorderless(IntPtr windowHandle, ScreenRegion region)
    {
        if (windowHandle == IntPtr.Zero || !User32.IsWindow(windowHandle))
        {
            return false;
        }

        // Restore first so a maximized/minimized window can actually be moved and
        // sized to the target region.
        User32.ShowWindow(windowHandle, User32.SW_RESTORE);

        StripBorders(windowHandle);

        return User32.SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            region.X, region.Y, region.Width, region.Height,
            User32.SWP_NOZORDER | User32.SWP_FRAMECHANGED | User32.SWP_SHOWWINDOW);
    }

    /// <summary>Brings a window to the foreground (used to give a pad's window focus).</summary>
    public void BringToFront(IntPtr windowHandle)
    {
        if (windowHandle != IntPtr.Zero && User32.IsWindow(windowHandle))
        {
            User32.SetForegroundWindow(windowHandle);
        }
    }

    /// <summary>Removes caption, frame and border styles to go borderless.</summary>
    private static void StripBorders(IntPtr hWnd)
    {
        long style = User32.GetWindowLongPtr(hWnd, User32.GWL_STYLE).ToInt64();
        style &= ~(User32.WS_CAPTION | User32.WS_THICKFRAME |
                   User32.WS_BORDER | User32.WS_DLGFRAME);
        User32.SetWindowLongPtr(hWnd, User32.GWL_STYLE, new IntPtr(style));

        long exStyle = User32.GetWindowLongPtr(hWnd, User32.GWL_EXSTYLE).ToInt64();
        exStyle &= ~(User32.WS_EX_WINDOWEDGE | User32.WS_EX_CLIENTEDGE);
        User32.SetWindowLongPtr(hWnd, User32.GWL_EXSTYLE, new IntPtr(exStyle));
    }
}
