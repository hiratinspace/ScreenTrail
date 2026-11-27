using System.Windows;

namespace ScreenTrail.UI.Theme;

/// <summary>
/// A control's size: <c>theme:Density.Compact="True"</c> takes a button to the compact size, 28 high with
/// 8 px sides and a 56 minimum (design system 10.1, review finding B-03), for a button inside a step, a
/// banner, a toast or a list row. Every button style reads it from the shared base, so one property
/// works on all seven styles.
/// </summary>
internal static class Density
{
    public static readonly DependencyProperty CompactProperty = DependencyProperty.RegisterAttached(
        "Compact", typeof(bool), typeof(Density), new FrameworkPropertyMetadata(false));

    public static bool GetCompact(DependencyObject element) => (bool)element.GetValue(CompactProperty);

    public static void SetCompact(DependencyObject element, bool value) => element.SetValue(CompactProperty, value);
}
