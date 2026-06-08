using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SplitPlay.App.Converters;

/// <summary>
/// Converts a boolean to <see cref="Visibility"/> inverted: false -> Visible,
/// true -> Collapsed. Handy for "show this only when the flag is off" cases such
/// as placeholders and empty states.
/// </summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is true;
        return flag ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Collapsed or Visibility.Hidden;
}
