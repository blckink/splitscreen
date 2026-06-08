using System.Collections.Generic;

namespace SplitPlay.Launch.InputIsolation;

/// <summary>
/// Persisted record of every change the isolation manager made to a game folder,
/// so the original files can always be restored - even after a crash or after the
/// app is restarted. Stored as JSON in %AppData%/SplitPlay.
/// </summary>
public sealed class IsolationState
{
    public List<IsolatedDirectory> Directories { get; set; } = new();
}

/// <summary>One game folder we dropped proxy DLLs into.</summary>
public sealed class IsolatedDirectory
{
    /// <summary>The game's executable directory.</summary>
    public required string Directory { get; set; }

    /// <summary>Absolute paths of the proxy DLLs we installed (to delete on restore).</summary>
    public List<string> InstalledFiles { get; set; } = new();

    /// <summary>Originals we moved aside, to move back on restore.</summary>
    public List<BackupEntry> Backups { get; set; } = new();
}

/// <summary>A single backed-up original DLL.</summary>
public sealed class BackupEntry
{
    /// <summary>The original file name, e.g. "xinput1_4.dll".</summary>
    public required string OriginalName { get; set; }

    /// <summary>Absolute path of the backup copy.</summary>
    public required string BackupPath { get; set; }
}
