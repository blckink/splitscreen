using System;
using System.Collections.ObjectModel;
using SplitPlay.App.Mvvm;

namespace SplitPlay.App.ViewModels;

/// <summary>
/// View model for one player position in the detail view: a label plus the
/// controller drop-down that binds a physical pad to this player's window.
/// </summary>
public sealed class PlayerSlotViewModel : ObservableObject
{
    private ControllerChoice _selected = ControllerChoice.Unassigned;

    public PlayerSlotViewModel(int index)
    {
        Index = index;
    }

    /// <summary>Zero-based player index.</summary>
    public int Index { get; }

    public string DisplayName => $"Player {Index + 1}";

    /// <summary>The controller choices available for this slot (shared list contents).</summary>
    public ObservableCollection<ControllerChoice> Choices { get; } = new();

    /// <summary>The currently selected controller choice.</summary>
    public ControllerChoice Selected
    {
        get => _selected;
        set
        {
            // Drop-downs can briefly bind null while their items are rebuilt.
            ControllerChoice next = value ?? ControllerChoice.Unassigned;
            if (SetProperty(ref _selected, next))
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Raised when the user picks a different controller for this slot.</summary>
    public event EventHandler? SelectionChanged;
}
