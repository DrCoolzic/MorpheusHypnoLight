using System.Globalization;
using Microsoft.Maui.Controls;
using MPHEditor.Services;

namespace MPHEditor.Converters;

/// <summary>
/// Converts a <see cref="PlayerStateEnum"/> value into the icon file name for the
/// play/pause button (shows a "pause" icon while playing, otherwise a "play" icon).
/// </summary>
public class PlayImageConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is PlayerStateEnum.PLAYING ? "pause.png" : "play.png";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
