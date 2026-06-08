using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using SplitPlay.App.Mvvm;
using SplitPlay.Core.Abstractions;
using SplitPlay.Core.Models;

namespace SplitPlay.App.ViewModels;

/// <summary>
/// Live overview of the controllers connected to the PC. Updates automatically as
/// pads are plugged in or removed, and offers a rumble "identify" test so the user
/// can feel which physical pad is which.
/// </summary>
public sealed class ControlsViewModel : PageViewModel
{
    // How long the identify rumble lasts.
    private static readonly TimeSpan RumbleDuration = TimeSpan.FromMilliseconds(700);

    private readonly IGamepadService _gamepadService;

    public ControlsViewModel(IGamepadService gamepadService)
    {
        _gamepadService = gamepadService;
        _gamepadService.GamepadsChanged += OnGamepadsChanged;

        TestCommand = new RelayCommand<GamepadInfo>(OnTest);
        Refresh();
    }

    public override string Title => "Controls";

    /// <summary>The currently connected controllers.</summary>
    public ObservableCollection<GamepadInfo> Controllers { get; } = new();

    /// <summary>Rumbles a controller briefly so the user can identify it.</summary>
    public RelayCommand<GamepadInfo> TestCommand { get; }

    /// <summary>True when no controllers are connected (shows guidance text).</summary>
    public bool IsEmpty => Controllers.Count == 0;

    private void OnTest(GamepadInfo? pad)
    {
        if (pad is not null)
        {
            _ = RumbleAsync(pad.UserIndex);
        }
    }

    private async Task RumbleAsync(int userIndex)
    {
        _gamepadService.SetVibration(userIndex, 1.0, 1.0);
        try
        {
            await Task.Delay(RumbleDuration);
        }
        finally
        {
            // Always stop, even if the delay is interrupted.
            _gamepadService.SetVibration(userIndex, 0, 0);
        }
    }

    private void OnGamepadsChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.Invoke(Refresh);

    private void Refresh()
    {
        Controllers.Clear();
        foreach (GamepadInfo pad in _gamepadService.GetConnectedGamepads())
        {
            Controllers.Add(pad);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }
}
