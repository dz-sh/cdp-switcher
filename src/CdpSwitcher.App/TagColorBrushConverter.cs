using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CdpSwitcher.App;

public sealed class TagColorBrushConverter : IValueConverter
{
    public bool UseContrastingForeground { get; set; }

    public object Convert(
        object value,
        Type targetType,
        object parameter,
        string language)
    {
        var color = ParseColor(value as string);
        if (UseContrastingForeground)
        {
            var luminance =
                (0.2126 * color.R) +
                (0.7152 * color.G) +
                (0.0722 * color.B);
            color = luminance >= 145
                ? Color.FromArgb(255, 0, 0, 0)
                : Color.FromArgb(255, 255, 255, 255);
        }

        return new SolidColorBrush(color);
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        string language)
    {
        throw new NotSupportedException();
    }

    private static Color ParseColor(string? value)
    {
        if (value is null ||
            value.Length != 7 ||
            value[0] != '#' ||
            !byte.TryParse(
                value.AsSpan(1, 2),
                System.Globalization.NumberStyles.HexNumber,
                provider: null,
                out var red) ||
            !byte.TryParse(
                value.AsSpan(3, 2),
                System.Globalization.NumberStyles.HexNumber,
                provider: null,
                out var green) ||
            !byte.TryParse(
                value.AsSpan(5, 2),
                System.Globalization.NumberStyles.HexNumber,
                provider: null,
                out var blue))
        {
            return Color.FromArgb(255, 107, 114, 128);
        }

        return Color.FromArgb(255, red, green, blue);
    }
}
