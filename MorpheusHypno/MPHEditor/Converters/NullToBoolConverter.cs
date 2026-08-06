using System.Globalization;
using Microsoft.Maui.Controls;

namespace MPHEditor.Converters;

/// <summary>
/// Converts an object to a boolean indicating whether it is non-null.
/// Typically used to bind a control's <c>IsVisible</c> to the presence of an optional value.
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
