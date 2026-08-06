using System.Globalization;
using Microsoft.Maui.Controls;

namespace MPHEditor.Converters;

/// <summary>
/// Converts an integer rating (0-5) into a row of star images, suitable for use as the
/// <see cref="ContentView.Content"/> of a rating display.
/// </summary>
public class RatingToStarsConverter : IValueConverter
{
    private const int MaxStars = 5;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int rating = value is int i ? i : 0;

        var layout = new HorizontalStackLayout { Spacing = 2 };
        for (int index = 0; index < MaxStars; index++)
        {
            layout.Children.Add(new Image
            {
                Source = index < rating ? "star_gold.png" : "star.png",
                WidthRequest = 20,
                HeightRequest = 20,
            });
        }
        return layout;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
