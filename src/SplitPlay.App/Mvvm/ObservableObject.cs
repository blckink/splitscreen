using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SplitPlay.App.Mvvm;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> base class for view models.
/// Kept tiny and dependency-free; <see cref="SetProperty{T}"/> is the only helper
/// most view models need.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Assigns <paramref name="value"/> to <paramref name="field"/> and raises
    /// <see cref="PropertyChanged"/> only if the value actually changed.
    /// </summary>
    /// <returns>True if the value changed.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
