using System.Globalization;
using System.Windows.Data;

namespace Jarvis.UI;

/// <summary>Converts a Boolean to its inverse for busy-state control bindings.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool flag ? !flag : throw new ArgumentException("Expected a Boolean binding value.", nameof(value));

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool flag ? !flag : throw new ArgumentException("Expected a Boolean binding value.", nameof(value));
}
