using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace WinAuthRemaster.Converters;

/// <summary>カード色(hex文字列)をセミ透過ブラシに変換。null/空→null(フォールバック)</summary>
public sealed class CardColorToBrushConverter : IValueConverter
{
    private const byte OVERLAY_ALPHA = 40; // ~15% 透過

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrEmpty(hex)) return null;
        try
        {
            var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(Color.FromArgb(OVERLAY_ALPHA, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }
        catch { return null; }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
