using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SplitPlay.Core.Abstractions;

namespace SplitPlay.Launch.InputIsolation;

/// <summary>
/// Keeps each running instance bound to the same physical controller across
/// disconnects. XInput slots (0-3) are not stable: a pad that is switched off and
/// back on can reappear on a different slot. For every player we write the slot it
/// should currently follow into a tiny file that the in-game proxy polls. When the
/// set of connected pads changes we re-derive the slots so a returning controller
/// is handed back to the same instance it drove before - the user never has to
/// re-assign anything mid-session.
/// </summary>
public sealed class ControllerRouter : IDisposable
{
    private sealed class Route
    {
        public required string PadFile { get; init; }
        public int Slot { get; set; }
    }

    private readonly IGamepadService _gamepads;
    private readonly List<Route> _routes;
    private readonly object _gate = new();
    private bool _disposed;

    public ControllerRouter(IGamepadService gamepads, IEnumerable<(string PadFile, int Slot)> routes)
    {
        _gamepads = gamepads;
        _routes = routes.Select(r => new Route { PadFile = r.PadFile, Slot = r.Slot }).ToList();

        // Establish the initial mapping on disk so the proxies pick it up.
        foreach (Route route in _routes)
        {
            WriteSlot(route);
        }

        _gamepads.GamepadsChanged += OnGamepadsChanged;
    }

    private void OnGamepadsChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            HashSet<int> connected = _gamepads.GetConnectedGamepads()
                .Where(g => g.IsConnected)
                .Select(g => g.UserIndex)
                .ToHashSet();

            // Slots still held by a route whose controller is present.
            HashSet<int> held = _routes
                .Where(r => connected.Contains(r.Slot))
                .Select(r => r.Slot)
                .ToHashSet();

            // Connected slots nobody currently owns: these are controllers that
            // have just (re)appeared and can be handed to an orphaned instance.
            var free = new Queue<int>(connected.Where(s => !held.Contains(s)).OrderBy(s => s));

            foreach (Route route in _routes)
            {
                if (connected.Contains(route.Slot))
                {
                    continue; // Its controller is still there - nothing to do.
                }

                if (free.Count > 0)
                {
                    route.Slot = free.Dequeue(); // The returning controller, possibly a new slot.
                    WriteSlot(route);
                }

                // Otherwise the controller is still gone: keep the last slot so it
                // resumes automatically once it reconnects on that same slot.
            }
        }
    }

    private static void WriteSlot(Route route)
    {
        try
        {
            File.WriteAllText(route.PadFile, route.Slot.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: the proxy keeps using its last known slot.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _gamepads.GamepadsChanged -= OnGamepadsChanged;
        }
    }
}
