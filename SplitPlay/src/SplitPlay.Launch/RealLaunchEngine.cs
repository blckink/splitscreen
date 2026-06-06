using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SplitPlay.Core.Abstractions;
using SplitPlay.Core.Models;
using SplitPlay.Launch.InputIsolation;

namespace SplitPlay.Launch;

/// <summary>
/// The launch engine. It starts the game executable once per player, waits for
/// each instance's window, makes it borderless and places it into the player's
/// split region. Each instance is launched through the XInput proxy
/// (<see cref="InputIsolationManager"/>) so it only ever sees its assigned
/// controller - one pad drives one window, even in the background, while keyboard
/// and mouse stay free for the desktop.
///
/// If a second instance cannot produce its own window (single-instance games) or
/// test mode is on, the affected slot falls back to a SplitPlay test window so the
/// split layout is always verifiable.
/// </summary>
public sealed class RealLaunchEngine : ILaunchEngine
{
    private static readonly TimeSpan GameWindowTimeout = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan TestWindowTimeout = TimeSpan.FromSeconds(10);

    // Distinct colors so the two test windows are easy to tell apart.
    private static readonly string[] PlayerColors = { "#4FD1A5", "#5AA9E6", "#E0A85A", "#C77DD6" };

    private readonly WindowManager _windowManager = new();
    private readonly GameWindowLocator _locator = new();
    private readonly InputIsolationManager _isolation;

    public RealLaunchEngine(InputIsolationManager isolation)
    {
        _isolation = isolation;
    }

    public async Task<LaunchResult> LaunchAsync(
        LaunchRequest request,
        IProgress<LaunchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? validationError = Validate(request);
        if (validationError is not null)
        {
            return LaunchResult.Fail(validationError);
        }

        return await Task.Run(() => RunAsync(request, progress, cancellationToken), cancellationToken);
    }

    private async Task<LaunchResult> RunAsync(
        LaunchRequest request,
        IProgress<LaunchProgress>? progress,
        CancellationToken cancellationToken)
    {
        bool testMode = request.Profile.UseTestWindows;

        progress?.Report(new LaunchProgress(8, "Resolving game executable..."));
        string? exePath = testMode
            ? null
            : ExecutableResolver.Resolve(
                request.Game.InstallDir, request.Game.Name, request.Profile.ExecutableOverride);

        // Install the per-instance XInput proxy into the game folder (unless test
        // mode, no exe, or the user turned isolation off).
        var isolation = SetupIsolation(request, exePath);

        string? testTargetPath = ResolveTestTargetPath();
        var notes = new List<string>();
        var startedWindows = new List<IntPtr>();
        int total = request.Targets.Count;

        for (int i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlayerLaunchTarget target = request.Targets[i];
            int percent = 10 + (int)((i + 0.5) / total * 80);
            progress?.Report(new LaunchProgress(
                percent, $"Launching instance {i + 1} of {total}..."));

            IntPtr handle = IntPtr.Zero;

            // 1. Try the real game executable (unless we are in test mode).
            if (!testMode && exePath is not null)
            {
                handle = await TryLaunchAndLocateAsync(
                    StartGame(exePath, target.ControllerIndex, isolation.Applied),
                    GameWindowTimeout, cancellationToken);

                if (handle == IntPtr.Zero)
                {
                    notes.Add($"{target.Player.DisplayName}: game window not detected, used a test window.");
                }
            }

            // 2. Fall back to a SplitPlay test window for this slot.
            if (handle == IntPtr.Zero)
            {
                if (testTargetPath is null)
                {
                    return LaunchResult.Fail(
                        "Test window helper (SplitPlay.TestTarget.exe) was not found next to the app.");
                }

                handle = await TryLaunchAndLocateAsync(
                    StartTestWindow(testTargetPath, target, i), TestWindowTimeout, cancellationToken);
            }

            if (handle == IntPtr.Zero)
            {
                return LaunchResult.Fail(
                    $"Could not obtain a window for {target.Player.DisplayName}.");
            }

            _windowManager.PlaceBorderless(handle, target.Region);
            startedWindows.Add(handle);
        }

        // Give both windows focus in order so they are visibly active.
        progress?.Report(new LaunchProgress(95, "Positioning windows..."));
        foreach (IntPtr handle in startedWindows)
        {
            _windowManager.BringToFront(handle);
            await Task.Delay(60, cancellationToken);
        }

        progress?.Report(new LaunchProgress(100, "Ready."));
        return BuildResult(request, testMode, exePath, notes, isolation);
    }

    /// <summary>
    /// Installs the XInput proxy for this launch if applicable, and returns whether
    /// it was applied plus a human-readable note for the result message.
    /// </summary>
    private IsolationOutcome SetupIsolation(LaunchRequest request, string? exePath)
    {
        if (request.Profile.UseTestWindows || exePath is null)
        {
            return new IsolationOutcome(false, null);
        }

        if (!request.Profile.IsolateControllers)
        {
            return new IsolationOutcome(false, "Controller isolation is disabled in this game's settings.");
        }

        ProcessArchitecture arch = PeArchitectureReader.Read(exePath);
        if (!_isolation.IsProxyAvailable(arch))
        {
            return new IsolationOutcome(false,
                "Controller isolation is OFF - the XInput proxy is not built yet " +
                "(run native/build-proxy.cmd once).");
        }

        string exeDir = Path.GetDirectoryName(exePath)!;
        bool applied = _isolation.Prepare(exeDir, arch);
        return applied
            ? new IsolationOutcome(true, "Controller input is isolated per window (one pad to one window).")
            : new IsolationOutcome(false,
                "Controller isolation could not be applied (could not write to the game folder).");
    }

    private async Task<IntPtr> TryLaunchAndLocateAsync(
        Process? process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (process is null)
        {
            return IntPtr.Zero;
        }

        return await _locator.WaitForMainWindowAsync(process, timeout, cancellationToken);
    }

    private static Process? StartGame(string exePath, int controllerIndex, bool isolated)
    {
        try
        {
            // UseShellExecute must be false so we can set a per-process environment
            // variable - that is how the proxy learns which pad this instance owns.
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
                UseShellExecute = false
            };

            if (isolated)
            {
                startInfo.Environment[InputIsolationManager.EnvVarName] =
                    controllerIndex.ToString();
            }

            return Process.Start(startInfo);
        }
        catch (Exception)
        {
            // Anti-cheat, permissions, etc. - treat as "couldn't launch", fall back.
            return null;
        }
    }

    private static Process? StartTestWindow(string testTargetPath, PlayerLaunchTarget target, int playerIndex)
    {
        string color = PlayerColors[playerIndex % PlayerColors.Length];
        try
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = testTargetPath,
                UseShellExecute = false,
                ArgumentList =
                {
                    "--player", (playerIndex + 1).ToString(),
                    "--controller", target.ControllerIndex.ToString(),
                    "--color", color
                }
            });
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Finds the bundled test-window helper next to the running app.</summary>
    private static string? ResolveTestTargetPath()
    {
        // Bundled into a "TestTarget" subfolder by the app build; also accept it
        // sitting directly next to the app as a fallback.
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "TestTarget", "SplitPlay.TestTarget.exe"),
            Path.Combine(AppContext.BaseDirectory, "SplitPlay.TestTarget.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Returns a validation error message, or null if the request is valid.</summary>
    private static string? Validate(LaunchRequest request)
    {
        if (request.Targets.Count < 2)
        {
            return "A split session needs at least two players.";
        }

        var indices = request.Targets.Select(t => t.ControllerIndex).ToList();
        if (indices.Any(i => i < 0))
        {
            return "Every player must have a controller assigned.";
        }

        if (indices.Distinct().Count() != indices.Count)
        {
            return "Each player must be assigned a different controller.";
        }

        return null;
    }

    private static LaunchResult BuildResult(
        LaunchRequest request, bool testMode, string? exePath,
        List<string> notes, IsolationOutcome isolation)
    {
        string orientation = request.Profile.Orientation.ToString().ToLowerInvariant();

        if (testMode)
        {
            return LaunchResult.Ok(
                $"Opened {request.Targets.Count} test windows in a {orientation} split.");
        }

        if (exePath is null)
        {
            return LaunchResult.Ok(
                "No game executable could be detected, so test windows were used. " +
                "Set an executable override for this game and try again.");
        }

        string message = $"Launched \"{request.Game.Name}\" as a {orientation} split.";
        if (notes.Count > 0)
        {
            message += " " + string.Join(" ", notes);
        }

        if (isolation.Note is not null)
        {
            message += " " + isolation.Note;
        }

        return LaunchResult.Ok(message);
    }

    /// <summary>Result of trying to set up controller isolation for a launch.</summary>
    private readonly record struct IsolationOutcome(bool Applied, string? Note);
}
