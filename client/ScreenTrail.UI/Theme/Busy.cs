using System.Windows;

namespace ScreenTrail.UI.Theme;

/// <summary>
/// <c>theme:Busy.Active</c>: a Primary button whose work is on its way (design system 10.1 loading, review
/// finding B-46). It keeps the Primary fill while it cannot be pressed again, so "Saving" reads as the button
/// doing its job and not as a button that is broken; the view swaps its words for a ring and "Saving".
/// </summary>
internal static class Busy
{
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached(
        "Active", typeof(bool), typeof(Busy), new FrameworkPropertyMetadata(false));

    public static bool GetActive(DependencyObject element) => (bool)element.GetValue(ActiveProperty);

    public static void SetActive(DependencyObject element, bool value) => element.SetValue(ActiveProperty, value);
}
