using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SplitPlay.Core.Abstractions;
using SplitPlay.Core.Models;

namespace SplitPlay.Launch;

/// <summary>
/// MVP placeholder for the real launch engine. It validates the request and walks
/// through the planned pipeline steps (reporting progress), but does NOT yet spawn
/// real game processes or hook input. This lets the full app - scanning, UI,
/// profiles, controller assignment - be built and exercised end to end first.
///
/// The real engine will implement these same steps for real, behind the unchanged
/// <see cref="ILaunchEngine"/> interface:
///   1. Resolve the game executable (profile override or auto-detect).
///   2. Prepare a second instance (per <see cref="InstanceStrategy"/>):
///        - mirror game files + drop in a lobby emulator (Goldberg/Nemirtingas), or
///        - spin up a second real Steam instance/account.
///   3. Start each instance with its controller pinned via a per-instance XInput
///      shim, so one pad only ever drives one window.
///   4. Wait for each game window, make it borderless and move it into its region
///      (see <see cref="WindowManager"/>).
///   5. Monitor the instances and tear everything down cleanly on exit.
/// </summary>
public sealed class StubLaunchEngine : ILaunchEngine
{
    public async Task<LaunchResult> LaunchAsync(
        LaunchRequest request,
        IProgress<LaunchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // --- Validation (this part is real and useful even in the stub) ---
        if (request.Targets.Count < 2)
        {
            return LaunchResult.Fail("A split session needs at least two players.");
        }

        var controllerIndices = request.Targets.Select(t => t.ControllerIndex).ToList();
        if (controllerIndices.Distinct().Count() != controllerIndices.Count)
        {
            return LaunchResult.Fail(
                "Each player must be assigned a different controller.");
        }

        if (controllerIndices.Any(i => i < 0))
        {
            return LaunchResult.Fail("Every player must have a controller assigned.");
        }

        // --- Simulated pipeline with progress reporting ---
        progress?.Report(new LaunchProgress(10, "Resolving game executable..."));
        await SimulateStepAsync(cancellationToken);

        int total = request.Targets.Count;
        for (int i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int percent = 10 + (int)((i + 1) / (double)total * 70);
            progress?.Report(new LaunchProgress(
                percent, $"Preparing instance {i + 1} of {total}..."));
            await SimulateStepAsync(cancellationToken);
        }

        progress?.Report(new LaunchProgress(90, "Positioning windows..."));
        await SimulateStepAsync(cancellationToken);

        progress?.Report(new LaunchProgress(100, "Ready."));

        return LaunchResult.Ok(
            $"[Preview] Would launch \"{request.Game.Name}\" as a " +
            $"{request.Profile.Orientation.ToString().ToLowerInvariant()} split for " +
            $"{total} players. The real launch engine is not wired up yet.");
    }

    // Stands in for the real per-step work; short delay keeps the UI progress
    // visible. Cancellable so the user can abort.
    private static Task SimulateStepAsync(CancellationToken cancellationToken) =>
        Task.Delay(350, cancellationToken);
}
