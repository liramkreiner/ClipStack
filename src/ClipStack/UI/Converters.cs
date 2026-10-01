using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ClipStack.UI;

public sealed class InverseBoolToVisibility : IValueConverter
{
    public static readonly InverseBoolToVisibility Instance = new();
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class NullToCollapsed : IValueConverter
{
    public static readonly NullToCollapsed Instance = new();
    public object Convert(object? value, Type t, object p, CultureInfo c) => value is null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
