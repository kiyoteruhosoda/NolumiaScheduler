using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace NolumiaScheduler.WinUI.Helpers;

public sealed class ColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is Windows.UI.Color c)
            return new SolidColorBrush(c);
        return new SolidColorBrush(Windows.UI.Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
