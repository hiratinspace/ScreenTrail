using System.Globalization;
using System.Windows.Data;

namespace ScreenTrail.UI.Theme;

/// <summary>
/// True when the value is a string with something in it: a field's <c>theme:Field.HasError</c> from the
/// problem its view model words (review finding B-12), where the words themselves are said under it.
/// </summary>
internal sealed class HasText : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && text.Length > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
