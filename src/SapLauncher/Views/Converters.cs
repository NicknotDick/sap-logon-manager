using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SapLauncher.Views;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

public class ProbeToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value?.ToString() switch
        {
            "Online" => "Brush.Success",
            "Offline" => "Brush.Danger",
            "Probing" => "Brush.Warn",
            _ => "Brush.TextMuted"
        };
        return Application.Current.TryFindResource(key) ?? Application.Current.Resources["Brush.TextMuted"];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
