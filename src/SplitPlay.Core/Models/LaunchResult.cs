namespace SplitPlay.Core.Models;

/// <summary>
/// Outcome of a launch attempt. Kept deliberately simple: success flag plus a
/// human-readable message that the UI can surface directly.
/// </summary>
public sealed class LaunchResult
{
    public required bool Success { get; init; }

    /// <summary>Message describing the result (error detail on failure).</summary>
    public required string Message { get; init; }

    public static LaunchResult Ok(string message = "Session started.") =>
        new() { Success = true, Message = message };

    public static LaunchResult Fail(string message) =>
        new() { Success = false, Message = message };
}

/// <summary>
/// Progress update emitted while a session is being prepared and started, so the
/// UI can show a live status line (e.g. "Preparing instance 2 of 2...").
/// </summary>
/// <param name="Percent">Overall progress, 0-100.</param>
/// <param name="Status">Short human-readable status text.</param>
public readonly record struct LaunchProgress(int Percent, string Status);
