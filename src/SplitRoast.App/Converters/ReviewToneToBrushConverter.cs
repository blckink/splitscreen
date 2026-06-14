using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SplitRoast.App.ViewModels;

namespace SplitRoast.App.Converters;

/// <summary>
/// Maps a <see cref="ReviewTone"/> to a themed brush for the small sentiment dot in
/// the Discover caption (green = positive, amber = mixed, red = negative, grey =
/// unknown). Reuses the existing suitability palette so colours stay consistent.
/// </summary>
public sealed class ReviewToneToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string key = value is ReviewTone tone
            ? tone switch
            {
                ReviewTone.Positive => "SuitGoodBrush",
                ReviewTone.Mixed => "SuitMaybeBrush",
                ReviewTone.Negative => "DangerBrush",
                _ => "SuitUnknownBrush"
            }
            : "SuitUnknownBrush";

        return Application.Current?.TryFindResource(key) as Brush
               ?? new SolidColorBrush(Color.FromRgb(0x55, 0x60, 0x6C));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
