using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SplitPlay.Launch.InputIsolation;

/// <summary>
/// Installs and removes the per-instance XInput proxy. When a session starts it
/// drops the proxy DLL into the game's folder under the three xinput names (after
/// backing up any originals); each game instance is then launched with the
/// <see cref="EnvVarName"/> environment variable telling the proxy which physical
/// controller to expose as index 0. On teardown - or, if that fails because the
/// game is still running, on the next app start - everything is restored exactly.
/// </summary>
public sealed class InputIsolationManager
{
    /// <summary>Environment variable read by the proxy to pick the physical pad.</summary>
    public const string EnvVarName = "SPLITPLAY_XINPUT_INDEX";

    /// <summary>Environment variable: path to the live "which slot to follow" file.</summary>
    public const string PadFileEnvVar = "SPLITPLAY_PAD_FILE";

    /// <summary>Name of the per-instance live pad-slot file (next to the game exe).</summary>
    public const string PadFileName = "splitplay_pad.txt";

    private const string BackupSuffix = ".splitplay-bak";

    // The xinput module names a game might load. We shadow all of them.
    private static readonly string[] XInputNames =
    {
        "xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll"
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _statePath;
    private readonly string? _proxyX64;
    private readonly string? _proxyX86;
    private IsolationState _state;

    public InputIsolationManager()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(appData, "SplitPlay");
        Directory.CreateDirectory(dir);
        _statePath = Path.Combine(dir, "isolation-state.json");

        string baseDir = AppContext.BaseDirectory;
        _proxyX64 = ResolveProxy(Path.Combine(baseDir, "XInputProxy", "x64"));
        _proxyX86 = ResolveProxy(Path.Combine(baseDir, "XInputProxy", "x86"));

        _state = LoadState();

        // Clean up anything left behind by a previous (possibly crashed) session.
        RestoreAll();
    }

    /// <summary>True if a proxy DLL is available for the given architecture.</summary>
    public bool IsProxyAvailable(ProcessArchitecture arch) => GetProxyForArch(arch) is not null;

    /// <summary>
    /// Copies the XInput proxy DLLs straight into a directory we fully own (e.g. a
    /// mirrored co-op instance folder), with no backup/restore bookkeeping. Any
    /// existing (hard-linked) xinput DLLs are deleted first so the originals are
    /// never modified. Returns true on success.
    /// </summary>
    public bool DeployProxy(string targetDir, ProcessArchitecture arch)
    {
        string? proxy = GetProxyForArch(arch);
        if (proxy is null || !Directory.Exists(targetDir))
        {
            return false;
        }

        try
        {
            foreach (string name in XInputNames)
            {
                string dest = Path.Combine(targetDir, name);
                if (File.Exists(dest))
                {
                    File.Delete(dest); // Drop the hard link, keep the original intact.
                }

                File.Copy(proxy, dest, overwrite: true);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Installs the proxy into <paramref name="gameExeDir"/> for the given
    /// architecture. Returns true if isolation is now in place. Idempotent: calling
    /// it again for the same folder just refreshes the proxy and keeps the original
    /// backup intact.
    /// </summary>
    public bool Prepare(string gameExeDir, ProcessArchitecture arch)
    {
        string? proxy = GetProxyForArch(arch);
        if (proxy is null || !Directory.Exists(gameExeDir))
        {
            return false;
        }

        IsolatedDirectory entry = _state.Directories
            .FirstOrDefault(d => PathsEqual(d.Directory, gameExeDir))
            ?? new IsolatedDirectory { Directory = gameExeDir };

        try
        {
            foreach (string name in XInputNames)
            {
                string target = Path.Combine(gameExeDir, name);
                string backup = target + BackupSuffix;

                // Only back up a genuine original once. If a backup already exists,
                // the current file is our (stale) proxy from a previous run.
                if (!File.Exists(backup) && File.Exists(target))
                {
                    File.Move(target, backup);
                }

                // Track any existing backup, even one left by a run whose state was
                // lost, so restore can always put the original back.
                if (File.Exists(backup) && !entry.Backups.Any(b => b.OriginalName == name))
                {
                    entry.Backups.Add(new BackupEntry { OriginalName = name, BackupPath = backup });
                }

                File.Copy(proxy, target, overwrite: true);
                if (!entry.InstalledFiles.Any(f => PathsEqual(f, target)))
                {
                    entry.InstalledFiles.Add(target);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Could not modify the game folder (permissions / locked). Roll back
            // what we did to this folder and report isolation as unavailable.
            RestoreDirectory(entry);
            return false;
        }

        if (!_state.Directories.Contains(entry))
        {
            _state.Directories.Add(entry);
        }

        SaveState();
        return true;
    }

    /// <summary>
    /// Restores every game folder we touched. Folders that are still locked (the
    /// game is running) are kept in the state file and retried on the next start.
    /// </summary>
    public void RestoreAll()
    {
        var remaining = new List<IsolatedDirectory>();
        foreach (IsolatedDirectory entry in _state.Directories)
        {
            if (!RestoreDirectory(entry))
            {
                remaining.Add(entry);
            }
        }

        _state.Directories = remaining;
        SaveState();
    }

    /// <summary>
    /// Restores a single folder. Returns true if fully restored, false if some
    /// files were locked and should be retried later.
    /// </summary>
    private static bool RestoreDirectory(IsolatedDirectory entry)
    {
        bool complete = true;

        // Remove our installed proxy DLLs.
        foreach (string file in entry.InstalledFiles.ToList())
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
                entry.InstalledFiles.Remove(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                complete = false; // Likely still mapped by a running game.
            }
        }

        // Move the originals back.
        foreach (BackupEntry backup in entry.Backups.ToList())
        {
            string target = Path.Combine(entry.Directory, backup.OriginalName);
            try
            {
                if (File.Exists(backup.BackupPath) && !File.Exists(target))
                {
                    File.Move(backup.BackupPath, target);
                }
                entry.Backups.Remove(backup);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                complete = false;
            }
        }

        return complete;
    }

    private string? GetProxyForArch(ProcessArchitecture arch) => arch switch
    {
        ProcessArchitecture.X64 => _proxyX64,
        ProcessArchitecture.X86 => _proxyX86,
        _ => null
    };

    private static string? ResolveProxy(string dir)
    {
        string path = Path.Combine(dir, "SplitPlay.XInputProxy.dll");
        return File.Exists(path) ? path : null;
    }

    private IsolationState LoadState()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                return JsonSerializer.Deserialize<IsolationState>(
                    File.ReadAllText(_statePath), JsonOptions) ?? new IsolationState();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Corrupt/locked state: start fresh rather than crash.
        }

        return new IsolationState();
    }

    private void SaveState()
    {
        try
        {
            File.WriteAllText(_statePath, JsonSerializer.Serialize(_state, JsonOptions));
        }
        catch (IOException)
        {
            // Non-fatal: state persistence is best-effort.
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd('\\'),
            Path.GetFullPath(b).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
}
