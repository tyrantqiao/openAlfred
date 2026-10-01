using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OpenAlfred.App.Converters;

/// <summary>null / 空字符串 → Collapsed，否则 Visible。</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
