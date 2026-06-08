using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using SplitPlay.Core.Abstractions;
using SplitPlay.Core.Models;
using Timer = System.Timers.Timer;

namespace SplitPlay.Input;

/// <summary>
/// XInput-backed <see cref="IGamepadService"/>. Polls connection state on a light
/// background timer and raises <see cref="GamepadsChanged"/> only when the set of
/// connected pads actually changes, so the UI updates without busy work. Live
/// state reads and rumble go straight through <see cref="XInputReader"/>.
/// </summary>
public sealed class XInputGamepadService : IGamepadService
{
    // Polling cadence. XInput offers no event API, so a 1s poll is the standard
    // approach; it is cheap (four struct queries) and plenty responsive for
    // plug/unplug feedback in a setup screen.
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly Timer _timer;
    private readonly object _gate = new();
    private bool[] _connected = new bool[XInputReader.MaxControllers];
    private bool _disposed;

    public XInputGamepadService()
    {
        _timer = new Timer(PollInterval.TotalMilliseconds) { AutoReset = true };
        _timer.Elapsed += OnPollTick;

        // Establish an initial snapshot so the first GetConnectedGamepads() call
        // is accurate even before monitoring starts.
        RefreshState();
    }

    public event EventHandler? GamepadsChanged;

    public IReadOnlyList<GamepadInfo> GetConnectedGamepads()
    {
        lock (_gate)
        {
            return Enumerable.Range(0, XInputReader.MaxControllers)
                .Where(i => _connected[i])
                .Select(i => new GamepadInfo { UserIndex = i, IsConnected = true })
                .ToList();
        }
    }

    public void StartMonitoring() => _timer.Start();

    public void StopMonitoring() => _timer.Stop();

    public GamepadState ReadState(int userIndex) => XInputReader.ReadState(userIndex);

    public void SetVibration(int userIndex, double leftMotor, double rightMotor) =>
        XInputReader.SetVibration(userIndex, leftMotor, rightMotor);

    private void OnPollTick(object? sender, ElapsedEventArgs e)
    {
        if (RefreshState())
        {
            GamepadsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Re-reads connection state. Returns true if it changed since last time.
    /// </summary>
    private bool RefreshState()
    {
        var current = new bool[XInputReader.MaxControllers];
        for (int i = 0; i < XInputReader.MaxControllers; i++)
        {
            current[i] = XInputReader.IsConnected(i);
        }

        lock (_gate)
        {
            if (current.SequenceEqual(_connected))
            {
                return false;
            }

            _connected = current;
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Elapsed -= OnPollTick;
        _timer.Dispose();

        // Make sure no controller is left rumbling when we shut down.
        for (int i = 0; i < XInputReader.MaxControllers; i++)
        {
            XInputReader.SetVibration(i, 0, 0);
        }
    }
}
