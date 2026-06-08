using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SplitPlay.Launch.Native;

namespace SplitPlay.Launch;

/// <summary>
/// Waits for and finds the main window of a launched process. Games often clear
/// <see cref="Process.MainWindowHandle"/> for a while after start (splash screens,
/// launchers, late window creation), so this polls and also scans all top-level
/// windows owned by the process as a fallback.
/// </summary>
public sealed class GameWindowLocator
{
    /// <summary>
    /// Polls until the process has a usable, visible top-level window, or the
    /// timeout elapses.
    /// </summary>
    /// <returns>The window handle, or <see cref="IntPtr.Zero"/> if none appeared.</returns>
    public async Task<IntPtr> WaitForMainWindowAsync(
        Process process, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                // A process that exits immediately usually means it handed off to
                // an already-running instance (single-instance game).
                return IntPtr.Zero;
            }

            process.Refresh();
            IntPtr handle = process.MainWindowHandle;
            if (handle != IntPtr.Zero && User32.IsWindowVisible(handle))
            {
                return handle;
            }

            // Fallback: a visible top-level window owned by this process id.
            handle = FindVisibleTopLevelWindow((uint)process.Id);
            if (handle != IntPtr.Zero)
            {
                return handle;
            }

            await Task.Delay(250, cancellationToken);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Returns the first visible, titled top-level window owned by the given
    /// process id, or <see cref="IntPtr.Zero"/> if there is none.
    /// </summary>
    public static IntPtr FindVisibleTopLevelWindow(uint processId)
    {
        IntPtr found = IntPtr.Zero;

        User32.EnumWindows((hWnd, _) =>
        {
            User32.GetWindowThreadProcessId(hWnd, out uint windowProcessId);
            if (windowProcessId != processId || !User32.IsWindowVisible(hWnd))
            {
                return true; // Keep enumerating.
            }

            // Prefer windows that actually have a title (skips invisible helpers).
            if (User32.GetWindowTextLength(hWnd) == 0)
            {
                return true;
            }

            found = hWnd;
            return false; // Stop: we found one.
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>Reads a window's title text (diagnostic helper).</summary>
    public static string GetWindowTitle(IntPtr hWnd)
    {
        int length = User32.GetWindowTextLength(hWnd);
        if (length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(length + 1);
        User32.GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
