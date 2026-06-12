using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace NolumiaScheduler.WinUI.Helpers;

public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility visibility && visibility == Visibility.Visible;
}
