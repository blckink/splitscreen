using System;
using System.Threading;
using System.Threading.Tasks;
using SplitPlay.Core.Models;

namespace SplitPlay.Core.Abstractions;

/// <summary>
/// Orchestrates starting a split-screen session: preparing instances, launching
/// them, routing each controller to exactly one window and positioning the
/// windows into their regions.
///
/// The MVP ships a stub implementation that validates the request and reports the
/// pipeline steps without performing the real OS-level work, so the rest of the
/// app (UI, scanning, profiles, input) can be completed and tested first. The
/// real engine drops in behind this same interface later.
/// </summary>
public interface ILaunchEngine
{
    /// <summary>
    /// Starts the session described by <paramref name="request"/>.
    /// </summary>
    /// <param name="progress">Optional progress sink for live status updates.</param>
    Task<LaunchResult> LaunchAsync(
        LaunchRequest request,
        IProgress<LaunchProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
