namespace SplitPlay.App.ViewModels;

/// <summary>
/// One selectable entry in a player's controller drop-down. A null
/// <see cref="Index"/> represents the "Unassigned" choice.
/// </summary>
public sealed class ControllerChoice
{
    /// <summary>XInput user index, or null for "Unassigned".</summary>
    public int? Index { get; init; }

    /// <summary>Label shown in the drop-down.</summary>
    public required string Label { get; init; }

    /// <summary>The shared "no controller" choice.</summary>
    public static ControllerChoice Unassigned { get; } =
        new() { Index = null, Label = "Unassigned" };
}
