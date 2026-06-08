using System;
using System.IO;
using System.Windows;

namespace SplitPlay.App.Diagnostics;

/// <summary>
/// Central place for surfacing unexpected errors: writes them to
/// %AppData%/SplitPlay/crash.log and shows them in a dialog, so the app never
/// just vanishes without a trace. Used by the global handlers in <c>App</c> and
/// by guarded call sites (e.g. opening a game).
/// </summary>
public static class CrashReporter
{
    /// <summary>Logs an exception to a file and shows it to the user.</summary>
    public static void Report(Exception? exception, string source)
    {
        if (exception is null)
        {
            return;
        }

        string? logPath = null;
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SplitPlay");
            Directory.CreateDirectory(dir);
            logPath = Path.Combine(dir, "crash.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:O}] ({source})\n{exception}\n\n");
        }
        catch
        {
            // Logging is best-effort; never let it throw.
        }

        try
        {
            string detail = logPath is null ? string.Empty : $"\n\nDetails written to:\n{logPath}";
            MessageBox.Show(
                $"{exception.GetType().Name}: {exception.Message}{detail}",
                "SplitPlay - unexpected error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // If even the dialog fails there is nothing more we can do.
        }
    }
}
