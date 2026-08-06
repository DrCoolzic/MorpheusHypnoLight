using System.Globalization;
using Microsoft.Maui.Controls;

namespace MPHEditor.Converters;

/// <summary>
/// Converts a string to a boolean indicating whether it is non-null and non-empty.
/// Typically used to bind a control's <c>IsVisible</c> to the presence of optional text.
/// </summary>
public class StringToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrWhiteSpace(value as string);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
