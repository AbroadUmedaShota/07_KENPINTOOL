using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KenpinTool.Prototype.Converters;

public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            // If count is 0, Visible. Else Collapsed. (For "No items" message)
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
