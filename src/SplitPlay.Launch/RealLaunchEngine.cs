using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SplitPlay.Core.Abstractions;
using SplitPlay.Core.Models;
using SplitPlay.Launch.Coop;
using SplitPlay.Launch.InputIsolation;

namespace SplitPlay.Launch;

/// <summary>
/// The launch engine. For a Steam game it auto-derives a co-op recipe, mirrors the
/// game per player (hard links), drops in the bundled Steam emulator + the XInput
/// proxy, launches each instance borderless and tiles it into its split region.
/// Each instance only sees its assigned controller. If the emulator isn't available
/// or a window can't be obtained, the affected slot falls back to a SplitPlay test
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

    // Keeps controllers bound to their instance across disconnects. Replaced on
    // each launch; the previous session's router is disposed first.
    private ControllerRouter? _router;

    public RealLaunchEngine(InputIsolationManager isolation, IGamepadService gamepads)
    {
        _isolation = isolation;
        _gamepads = gamepads;
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

        for (int i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlayerLaunchTarget target = request.Targets[i];
            int percent = 10 + (int)((i + 0.5) / total * 80);
            progress?.Report(new LaunchProgress(percent, $"Launching player {i + 1} of {total}..."));

            IntPtr handle = IntPtr.Zero;

            if (!testMode && exePath is not null)
            {
                if (emulatorReady)
                {
                    (IntPtr mirrorHandle, string? padFile) = await StartMirroredInstanceAsync(
                        request.Game, recipe!, target, i, arch, cancellationToken);
                    handle = mirrorHandle;
                    if (padFile is not null)
                    {
                        routes.Add((padFile, target.ControllerIndex));
                    }
                }
                else
                {
                    // Direct mode: launch the original folder in place. Window
                    // enforcement still applies through the proxy; live pad routing
                    // is skipped because both instances share one folder.
                    Process? proc = StartGameProcess(
                        exePath, Path.GetDirectoryName(exePath)!, string.Empty,
                        target.ControllerIndex, directIsolated, request.Game.AppId,
                        padFile: null, region: target.Region);
                    handle = await TryLaunchAndLocateAsync(proc, GameWindowTimeout, cancellationToken);
                }

                if (handle == IntPtr.Zero)
                {
                    notes.Add($"{target.Player.DisplayName}: game window not detected, used a test window.");
                }
            }

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
                return LaunchResult.Fail($"Could not obtain a window for {target.Player.DisplayName}.");
            }

            _windowManager.PlaceBorderless(handle, target.Region);
            placed.Add((handle, target.Region));

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
        if (!testMode && routes.Count > 0)
        {
            _router?.Dispose();
            _router = new ControllerRouter(_gamepads, routes);
        }

        progress?.Report(new LaunchProgress(100, "Ready."));
        return BuildResult(request, testMode, exePath, recipe, wantsEmulator, emulatorReady, directIsolated, notes);
    }

    /// <summary>
    /// Mirrors the game for this player, applies the Steam emulator + controller
    /// proxy, launches the instance borderless at the region size and returns its
    /// window handle (or zero on failure) plus the path of the live pad-slot file
    /// the engine routes controllers through.
    /// </summary>
    private async Task<(IntPtr Handle, string? PadFile)> StartMirroredInstanceAsync(
        SteamGame game, CoopRecipe recipe, PlayerLaunchTarget target, int playerIndex,
        ProcessArchitecture arch, CancellationToken cancellationToken)
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

        IntPtr handle = await _locator.WaitForMainWindowAsync(process, GameWindowTimeout, cancellationToken);
        return (handle, handle != IntPtr.Zero ? padFile : null);
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
                startInfo.Environment["SPLITPLAY_WIN_X"] = region.X.ToString();
                startInfo.Environment["SPLITPLAY_WIN_Y"] = region.Y.ToString();
                startInfo.Environment["SPLITPLAY_WIN_W"] = region.Width.ToString();
                startInfo.Environment["SPLITPLAY_WIN_H"] = region.Height.ToString();
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
            // Best-effort: the proxy falls back to its SPLITPLAY_XINPUT_INDEX env.
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
            Path.Combine(AppContext.BaseDirectory, "TestTarget", "SplitPlay.TestTarget.exe"),
            Path.Combine(AppContext.BaseDirectory, "SplitPlay.TestTarget.exe")
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
