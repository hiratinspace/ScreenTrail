using System.Windows;

namespace ScreenTrail.UI.Theme;

/// <summary>
/// What a text field or combo says about itself beyond its value (design system 10.2): <c>theme:Field.Error</c>
/// draws the field's edge in state.danger with the error line 4 below it (a warning glyph and Text.Error), so
/// the field that is wrong looks wrong (review finding B-12); <c>theme:Field.HasError</c> marks the edge alone,
/// for a field whose message is said elsewhere, and is true whenever there is an error to show;
/// <c>theme:Field.Placeholder</c> is the quiet example shown in an empty text box (B-13). The error is also the
/// field's UI Automation item status, so a screen reader says it with the field.
/// </summary>
internal static class Field
{
    public static readonly DependencyProperty ErrorProperty = DependencyProperty.RegisterAttached(
        "Error", typeof(string), typeof(Field), new FrameworkPropertyMetadata(null, (d, _) => d.CoerceValue(HasErrorProperty)));

    public static readonly DependencyProperty HasErrorProperty = DependencyProperty.RegisterAttached(
        "HasError", typeof(bool), typeof(Field), new FrameworkPropertyMetadata(false, null, CoerceHasError));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Field), new FrameworkPropertyMetadata(null));

    public static string? GetError(DependencyObject element) => (string?)element.GetValue(ErrorProperty);

    public static void SetError(DependencyObject element, string? value) => element.SetValue(ErrorProperty, value);

    public static bool GetHasError(DependencyObject element) => (bool)element.GetValue(HasErrorProperty);

    public static void SetHasError(DependencyObject element, bool value) => element.SetValue(HasErrorProperty, value);

    public static string? GetPlaceholder(DependencyObject element) => (string?)element.GetValue(PlaceholderProperty);

    public static void SetPlaceholder(DependencyObject element, string? value) => element.SetValue(PlaceholderProperty, value);

    private static object CoerceHasError(DependencyObject element, object value) =>
        (bool)value || !string.IsNullOrEmpty(GetError(element));
}
