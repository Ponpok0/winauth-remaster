using System.Globalization;
using System.Windows.Data;

namespace WinAuthRemaster.Converters;

/// <summary>
/// Converts progress (0.0-1.0) to an angle (0-360) for arc rendering.
/// </summary>
public sealed class ProgressToAngleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double progress)
            return progress * 360.0;
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts progress (0.0-1.0) to a boolean indicating if the arc is large (> 180 degrees).
/// </summary>
public sealed class ProgressToIsLargeArcConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double progress)
            return progress > 0.5;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
