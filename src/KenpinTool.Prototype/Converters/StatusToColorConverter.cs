using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace KenpinTool.Prototype.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            "OK" => Brushes.Green,
            "NG" => Brushes.Red,
            "Warn" => Brushes.Yellow,
            _ => Brushes.Gray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
