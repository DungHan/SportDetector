using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NBA.App.Converters;

/// <summary>
/// Resolves a minimap marker's <c>StyleKey</c> (<see cref="NBA.App.Models.CourtMarker"/>) to a fill color, so
/// tracked-player markers are visually distinguishable from calibration-keypoint markers on the same minimap.
/// Unrecognized/null keys keep the original keypoint color, so existing (unstyled) markers are unaffected.
/// </summary>
public sealed class MarkerStyleToBrushConverter : IValueConverter
{
    public static readonly MarkerStyleToBrushConverter Instance = new();

    // Brushes are AvaloniaObjects with dispatcher-thread affinity - a single instance shared across
    // Convert calls would break if bound from more than one UI thread (as headless test sessions do, one per
    // test case), so a fresh brush is created per call rather than cached in a static field.
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string) switch
        {
            "player" => new SolidColorBrush(Color.Parse("#F2F2F2")),
            _ => new SolidColorBrush(Color.Parse("#40A0FF")),
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
