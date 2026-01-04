using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KenpinTool.Prototype;

public sealed class BoolToGridLengthConverter : IValueConverter
{
    public GridLength TrueLength { get; set; } = new(1, GridUnitType.Star);
    public GridLength FalseLength { get; set; } = new(0);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
        {
            return TrueLength;
        }

        return FalseLength;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

