using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SplitRoast.Core.Abstractions;
using SplitRoast.Core.Models;
using SplitRoast.Launch.Coop;
using SplitRoast.Launch.Diagnostics;
using SplitRoast.Launch.InputIsolation;

namespace SplitRoast.Launch;

/// <summary>
/// The launch engine. For a Steam game it auto-derives a co-op recipe, mirrors the
/// game per player (hard links), drops in the bundled Steam emulator + the XInput
/// proxy, launches each instance borderless and tiles it into its split region.
/// Each instance only sees its assigned controller. If the emulator isn't available
/// or a window can't be obtained, the affected slot falls back to a SplitRoast test
/// window; test mode opens placeholder windows only.
/// </summary>
public sealed class RealLaunchEngine : ILaunchEngine
{
    private static readonly TimeSpan GameWindowTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan TestWindowTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PauseBetweenInstances = TimeSpan.FromSeconds(15);

    private static readonly string[] PlayerColors = { "#4FD1A5", "#5AA9E6", "#E0A85A", "#C77DD6" };

    private readonly WindowManager _windowManager = new();
    private readonly GameWindowLocator _locator = new();
    private readonly InputIsolationManager _isolation;
    private readonly IGamepadService _gamepads;

    // The currently running session (processes + router + isolation to tear down).
    // Replaced on each launch; the previous one is stopped first.
    private readonly object _sessionGate = new();
    private GameSession? _session;

    public RealLaunchEngine(InputIsolationManager isolation, IGamepadService gamepads)
    {
        _isolation = isolation;
        _gamepads = gamepads;
    }

    public bool IsSessionActive
    {
        get { lock (_sessionGate) { return _session?.IsActive == true; } }
    }

    public event EventHandler? SessionStateChanged;

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        GameSession? session;
        lock (_sessionGate)
        {
            session = _session;
        }

        if (session is not null)
        {
            await session.StopAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Stops and clears any active session before a new launch.</summary>
    private async Task StopActiveSessionAsync()
    {
        GameSession? previous;
        lock (_sessionGate)
        {
            previous = _session;
            _session = null;
        }

        if (previous is not null)
        {
            previous.Ended -= OnSessionEnded;
            await previous.StopAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Records a new session and notifies listeners it is active.</summary>
    private void SetSession(GameSession session)
    {
        lock (_sessionGate)
        {
            _session = session;
        }

        session.Ended += OnSessionEnded;
        SessionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSessionEnded(object? sender, EventArgs e)
    {
        lock (_sessionGate)
        {
            if (ReferenceEquals(_session, sender))
            {
                _session = null;
            }
        }

        SessionStateChanged?.Invoke(this, EventArgs.Empty);
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
        int total = request.Targets.Count;

        var diag = new LaunchDiagnostics(request.Game.AppId, request.Game.Name);
        diag.Log($"Launch requested: {total} players, " +
                 $"{request.Profile.Orientation.ToString().ToLowerInvariant()} split, " +
                 $"testMode={testMode}.");

        // Never run two sessions at once: cleanly tear down a previous one first.
        await StopActiveSessionAsync();

        progress?.Report(new LaunchProgress(6, "Analyzing game..."));
        string? exePath = testMode
            ? null
            : ExecutableResolver.Resolve(
                request.Game.InstallDir, request.Game.Name, request.Profile.ExecutableOverride);

        ProcessArchitecture arch = exePath is not null
            ? PeArchitectureReader.Read(exePath)
            : ProcessArchitecture.Unknown;

        CoopRecipe? recipe = exePath is not null
            ? GameAnalyzer.Analyze(request.Game.AppId, request.Game.InstallDir, exePath)
            : null;

        // Decide how to obtain a second instance.
        bool wantsEmulator = recipe?.UsesSteam == true &&
                             request.Profile.InstanceStrategy != InstanceStrategy.DualSteamAccounts;
        bool emulatorReady = wantsEmulator && GoldbergLocator.CanServe(recipe!);

        diag.Log($"Analysis: exe='{exePath ?? "(none)"}', arch={arch}, engine={recipe?.Engine.ToString() ?? "n/a"}, " +
                 $"sdks={recipe?.DetectedSdks.ToString() ?? "n/a"}, steamDrm={recipe?.HasSteamDrm == true}, " +
                 $"wantsEmulator={wantsEmulator}, emulatorReady={emulatorReady}.");

        // Surface online SDKs we detect but can't yet emulate, so the diagnostics
        // explain why such a game may not pair up rather than failing silently.
        if (recipe is not null && (recipe.UsesEpic || recipe.UsesGalaxy))
        {
            diag.Log($"Note: detected {(recipe.UsesEpic ? "Epic Online Services" : "")}" +
                     $"{(recipe.UsesEpic && recipe.UsesGalaxy ? " + " : "")}" +
                     $"{(recipe.UsesGalaxy ? "GOG Galaxy" : "")} SDK. A matching network emulator " +
                     "is not bundled yet, so a shared co-op session may be unavailable for this title.");
        }

        // Steam-DRM (SteamStub) games relaunch themselves through Steam when the
        // client isn't running - the copy we start exits and a stray fullscreen
        // window appears instead of the split. Start Steam first so our mirrored
        // instances launch cleanly. Plain Steam games (e.g. Roots of Pacha) have no
        // stub, so this is skipped and their behaviour is unchanged.
        if (!testMode && recipe?.HasSteamDrm == true)
        {
            progress?.Report(new LaunchProgress(8, "Starting Steam (required by this game)..."));
            bool steamOk = SteamClientController.EnsureRunning(diag.Log, cancellationToken);
            diag.Log($"Steam DRM detected; ensured Steam client running={steamOk}.");
        }

        // Direct mode (no mirroring): install the proxy into the original folder once.
        bool directIsolated = false;
        if (!testMode && exePath is not null && !emulatorReady && request.Profile.IsolateControllers)
        {
            directIsolated = _isolation.Prepare(Path.GetDirectoryName(exePath)!, arch);
        }

        string? testTargetPath = ResolveTestTargetPath();
        var notes = new List<string>();
        var placed = new List<(IntPtr Handle, ScreenRegion Region)>();
        var routes = new List<(string PadFile, int Slot)>();

        // Every process we start (games + any test windows) so the session can stop
        // them; gameProcesses also drives automatic single-instance recovery.
        var processes = new List<Process>();
        var gameProcesses = new List<Process>();

        // Launches one real game instance (mirrored or direct) and locates its window.
        async Task<(IntPtr Handle, Process? Process, string? PadFile)> LaunchRealInstanceAsync(
            PlayerLaunchTarget t, int idx)
        {
            if (emulatorReady)
            {
                (IntPtr h, string? pf, Process? p) = await StartMirroredInstanceAsync(
                    request.Game, recipe!, t, idx, arch, diag, cancellationToken);
                return (h, p, pf);
            }

            // Direct mode: launch the original folder in place. Window enforcement
            // still applies through the proxy; live pad routing is skipped because
            // both instances share one folder.
            Process? proc = StartGameProcess(
                exePath!, Path.GetDirectoryName(exePath!)!, string.Empty,
                t.ControllerIndex, directIsolated, request.Game.AppId, padFile: null, region: t.Region);
            diag.Log($"player {idx + 1}: direct launch {(proc is not null ? "started pid " + proc.Id : "FAILED to start")}.");
            IntPtr located = await TryLaunchAndLocateAsync(proc, GameWindowTimeout, cancellationToken);
            return (located, proc, null);
        }

        for (int i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlayerLaunchTarget target = request.Targets[i];
            int percent = 10 + (int)((i + 0.5) / total * 80);
            progress?.Report(new LaunchProgress(percent, $"Launching player {i + 1} of {total}..."));

            IntPtr handle = IntPtr.Zero;
            Process? instanceProcess = null;
            string? padFile = null;

            if (!testMode && exePath is not null)
            {
                (handle, instanceProcess, padFile) = await LaunchRealInstanceAsync(target, i);

                // Automatic single-instance recovery: a second copy that exits before
                // it ever shows a window has almost certainly hit a single-instance
                // lock held by an earlier copy. Free that lock and retry this one -
                // no per-game setting required, and only when we actually detect it.
                if (handle == IntPtr.Zero && instanceProcess is not null && HasExited(instanceProcess)
                    && gameProcesses.Any(p => !HasExited(p)))
                {
                    progress?.Report(new LaunchProgress(
                        percent, $"Player {i + 1} closed itself (single-instance lock); freeing it and retrying..."));
                    diag.Log($"player {i + 1}: exited before showing a window; freeing single-instance locks and retrying.");

                    int freed = 0;
                    foreach (Process earlier in gameProcesses.Where(p => !HasExited(p)))
                    {
                        freed += MutexKiller.CloseSingleInstanceLocks(earlier.Id);
                    }

                    diag.Log($"player {i + 1}: freed {freed} single-instance lock(s).");

                    if (freed > 0)
                    {
                        (handle, instanceProcess, padFile) = await LaunchRealInstanceAsync(target, i);
                        if (handle != IntPtr.Zero)
                        {
                            notes.Add($"{target.Player.DisplayName}: cleared a single-instance lock to open a second copy.");
                        }
                    }
                }

                if (instanceProcess is not null)
                {
                    processes.Add(instanceProcess);
                    gameProcesses.Add(instanceProcess);
                }

                if (padFile is not null)
                {
                    routes.Add((padFile, target.ControllerIndex));
                }

                // The game IS running, we just couldn't grab its top window in time
                // (common with borderless/fullscreen titles). The in-game proxy pins
                // the window to the split region itself, so do NOT spawn a test
                // window - that was the cause of stray "Player 1/2" windows.
                if (handle == IntPtr.Zero && instanceProcess is not null && !HasExited(instanceProcess))
                {
                    diag.Log($"player {i + 1}: window not located in time; leaving it to the in-game proxy to position.");
                    notes.Add($"{target.Player.DisplayName}: launched (positioned by the in-game helper).");
                }
            }

            // Only fall back to a test window when the real game is NOT running.
            bool gameRunning = instanceProcess is not null && !HasExited(instanceProcess);
            if (handle == IntPtr.Zero && !gameRunning)
            {
                if (testTargetPath is null)
                {
                    diag.Log("Test window helper (SplitRoast.TestTarget.exe) not found next to the app.");
                    return LaunchResult.Fail(
                        "Test window helper (SplitRoast.TestTarget.exe) was not found next to the app.");
                }

                if (!testMode)
                {
                    diag.Log($"player {i + 1}: game did not start; using a test window.");
                }

                Process? testProcess = StartTestWindow(testTargetPath, target, i);
                if (testProcess is not null)
                {
                    processes.Add(testProcess);
                }
                handle = await TryLaunchAndLocateAsync(testProcess, TestWindowTimeout, cancellationToken);

                if (handle == IntPtr.Zero)
                {
                    diag.Log($"player {i + 1}: could not obtain any window.");
                    return LaunchResult.Fail($"Could not obtain a window for {target.Player.DisplayName}.");
                }
            }

            if (handle != IntPtr.Zero)
            {
                _windowManager.PlaceBorderless(handle, target.Region);
                placed.Add((handle, target.Region));
            }

            // Give a real game instance time to initialise before the next one.
            if (!testMode && exePath is not null && i < total - 1)
            {
                await Task.Delay(PauseBetweenInstances, cancellationToken);
            }
        }

        progress?.Report(new LaunchProgress(95, "Positioning windows..."));
        foreach ((IntPtr handle, _) in placed)
        {
            _windowManager.BringToFront(handle);
            await Task.Delay(60, cancellationToken);
        }

        // Start keeping each instance bound to its controller across disconnects.
        // The game windows themselves are pinned to their region from inside the
        // game process by the proxy (it clamps WM_WINDOWPOSCHANGING), so no
        // polling watchdog is needed here.
        ControllerRouter? router = (!testMode && routes.Count > 0)
            ? new ControllerRouter(_gamepads, routes)
            : null;

        // Hand everything we started to a session that owns the teardown: stop the
        // games, dispose the router, and (direct mode only) restore the original game
        // folder. Mirrored instances live in disposable copies that never touch the
        // original, so they need no restore.
        SetSession(new GameSession(processes, router, _isolation, restoreIsolationOnStop: directIsolated));

        // Gather the per-instance proxy logs and the game's own log into the
        // diagnostics folder so the player can inspect a crash from the detail page.
        if (!testMode)
        {
            await Task.Delay(1500, cancellationToken); // let logs flush
            diag.CollectProxyLogs(InstanceMirror.GetInstancesRoot(request.Game.LibraryPath, request.Game.AppId));
            diag.CollectGameLog(request.Game.Name);
        }

        LaunchResult result =
            BuildResult(request, testMode, exePath, recipe, wantsEmulator, emulatorReady, directIsolated, notes);
        diag.Log($"Result: {(result.Success ? "OK" : "FAILED")} — {result.Message}");

        progress?.Report(new LaunchProgress(100, "Ready."));
        return result;
    }

    /// <summary>Whether a process has exited, treating an inaccessible one as exited.</summary>
    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch { return true; }
    }

    /// <summary>
    /// Mirrors the game for this player, applies the Steam emulator + controller
    /// proxy, launches the instance borderless at the region size and returns its
    /// window handle (or zero on failure) plus the path of the live pad-slot file
    /// the engine routes controllers through.
    /// </summary>
    private async Task<(IntPtr Handle, string? PadFile, Process? Process)> StartMirroredInstanceAsync(
        SteamGame game, CoopRecipe recipe, PlayerLaunchTarget target, int playerIndex,
        ProcessArchitecture arch, LaunchDiagnostics diag, CancellationToken cancellationToken)
    {
        string instanceDir = InstanceMirror.GetInstanceDir(game.LibraryPath, game.AppId, playerIndex);
        InstanceMirror.Mirror(recipe.SourceInstallDir, instanceDir);
        SteamEmulator.Apply(instanceDir, recipe, playerIndex);

        // Unity reads its window/fullscreen prefs from the registry during startup,
        // overriding command-line size. Force windowed mode + our size as a first
        // line of defence; the in-process proxy clamp is the real guarantee.
        if (recipe.Engine == EngineType.Unity)
        {
            UnityRegistry.TryApplyWindowed(
                game.InstallDir, game.Name,
                target.Region.Width, target.Region.Height, target.Region.X, target.Region.Y);
        }

        string exeInInstance = Path.Combine(instanceDir, recipe.ExeRelativePath);
        string exeDir = Path.GetDirectoryName(exeInInstance)!;
        bool isolated = arch != ProcessArchitecture.Unknown && _isolation.DeployProxy(exeDir, arch);

        // Each instance owns its folder, so it gets a private live pad-slot file.
        string? padFile = isolated ? Path.Combine(exeDir, InputIsolationManager.PadFileName) : null;

        string args = recipe.BuildWindowArgs(target.Region.Width, target.Region.Height);
        Process? process = StartGameProcess(
            exeInInstance, exeDir, args, target.ControllerIndex, isolated, game.AppId,
            padFile, target.Region);
        bool started = process is not null;

        diag.Log($"player {playerIndex + 1}: mirrored -> {instanceDir}; emulator applied; " +
                 $"proxy {(isolated ? "deployed" : "NOT deployed")}; " +
                 $"process {(started ? "started pid " + process!.Id : "FAILED to start")}.");

        IntPtr handle = started
            ? await _locator.WaitForMainWindowAsync(process!, GameWindowTimeout, cancellationToken)
            : IntPtr.Zero;

        // Route this controller whenever the instance is actually running and isolated,
        // even if we never grabbed its window (borderless games still need isolation).
        return (handle, (isolated && started) ? padFile : null, process);
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

    private static Process? StartGameProcess(
        string exePath, string workingDir, string arguments,
        int controllerIndex, bool isolated, uint appId,
        string? padFile, ScreenRegion region)
    {
        try
        {
            // UseShellExecute=false so we can pass per-process environment variables
            // (the proxy's controller index/region and the emulator's appid).
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = workingDir,
                Arguments = arguments,
                UseShellExecute = false
            };

            if (isolated)
            {
                startInfo.Environment[InputIsolationManager.EnvVarName] = controllerIndex.ToString();

                // The proxy keeps the window inside its split region from inside the
                // game process (clamps WM_WINDOWPOSCHANGING), so an intro cannot push
                // it to fullscreen.
                startInfo.Environment["SPLITROAST_WIN_X"] = region.X.ToString();
                startInfo.Environment["SPLITROAST_WIN_Y"] = region.Y.ToString();
                startInfo.Environment["SPLITROAST_WIN_W"] = region.Width.ToString();
                startInfo.Environment["SPLITROAST_WIN_H"] = region.Height.ToString();
            }

            if (padFile is not null)
            {
                // Seed the live pad-slot file with the initial controller, then point
                // the proxy at it so the engine can re-route on reconnect.
                TryWriteAllText(padFile, controllerIndex.ToString());
                startInfo.Environment[InputIsolationManager.PadFileEnvVar] = padFile;
            }

            startInfo.Environment["SteamAppId"] = appId.ToString();
            startInfo.Environment["SteamGameId"] = appId.ToString();

            return Process.Start(startInfo);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void TryWriteAllText(string path, string content)
    {
        try
        {
            File.WriteAllText(path, content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: the proxy falls back to its SPLITROAST_XINPUT_INDEX env.
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

    private static string? ResolveTestTargetPath()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "TestTarget", "SplitRoast.TestTarget.exe"),
            Path.Combine(AppContext.BaseDirectory, "SplitRoast.TestTarget.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

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
        LaunchRequest request, bool testMode, string? exePath, CoopRecipe? recipe,
        bool wantsEmulator, bool emulatorReady, bool directIsolated, List<string> notes)
    {
        string orientation = request.Profile.Orientation.ToString().ToLowerInvariant();

        if (testMode)
        {
            return LaunchResult.Ok($"Opened {request.Targets.Count} test windows in a {orientation} split.");
        }

        if (exePath is null)
        {
            return LaunchResult.Ok(
                "No game executable could be detected, so test windows were used. " +
                "Set an executable override for this game and try again.");
        }

        string message = $"Launched \"{request.Game.Name}\" as a {orientation} split.";

        if (emulatorReady)
        {
            message += " Running mirrored instances via the bundled Steam emulator;" +
                       " controller input is isolated per window.";
        }
        else if (wantsEmulator)
        {
            message += " This game needs the Steam emulator for a second instance, which" +
                       " is not available in this build - used test windows instead.";
        }
        else if (directIsolated)
        {
            message += " Controller input is isolated per window.";
        }

        if (notes.Count > 0)
        {
            message += " " + string.Join(" ", notes);
        }

        return LaunchResult.Ok(message);
    }
}
