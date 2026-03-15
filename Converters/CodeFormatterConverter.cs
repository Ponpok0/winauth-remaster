using System.Globalization;
using System.Windows.Data;

namespace WinAuthRemaster.Converters;

public sealed class CodeFormatterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string code) return value;
        return code.Length switch
        {
            6 => $"{code[..3]} {code[3..]}",
            8 => $"{code[..4]} {code[4..]}",
            _ => code
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
