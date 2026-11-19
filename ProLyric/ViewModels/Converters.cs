using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ProLyric.ViewModels;

public class BoolToVisConverter : IValueConverter
{
    public static readonly BoolToVisConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Visible;
}

public class EmptyStringToVisibilityConverter : IValueConverter
{
    public static readonly EmptyStringToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s && string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class InverseBooleanConverter : IValueConverter
{
    public static readonly InverseBooleanConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;
}

public class NullToVisibilityConverter : IValueConverter
{
    public static readonly NullToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Hidden : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class DualColumnWidthConverter : IValueConverter
{
    public static readonly DualColumnWidthConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool isDual && isDual
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class TemplateStatusBgConverter : IValueConverter
{
    public static readonly TemplateStatusBgConverter Instance = new();

    private static readonly SolidColorBrush ValidBg = new(Color.FromRgb(0x0D, 0x1E, 0x13));
    private static readonly SolidColorBrush InvalidBg = new(Color.FromRgb(0x1F, 0x0D, 0x0D));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool valid && valid ? ValidBg : InvalidBg;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class TemplateStatusBorderConverter : IValueConverter
{
    public static readonly TemplateStatusBorderConverter Instance = new();

    private static readonly SolidColorBrush ValidBorder = new(Color.FromRgb(0x1E, 0x46, 0x2B));
    private static readonly SolidColorBrush InvalidBorder = new(Color.FromRgb(0x46, 0x1E, 0x1E));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool valid && valid ? ValidBorder : InvalidBorder;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class TemplateStatusFgConverter : IValueConverter
{
    public static readonly TemplateStatusFgConverter Instance = new();

    private static readonly SolidColorBrush ValidFg = new(Color.FromRgb(0x4A, 0xDE, 0x80));
    private static readonly SolidColorBrush InvalidFg = new(Color.FromRgb(0xF8, 0x71, 0x71));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool valid && valid ? ValidFg : InvalidFg;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
