using System.Windows;

namespace ScreenTrail.UI.Theme;

/// <summary>
/// <c>theme:Meter.Clipping</c>: the level meter's fill turns from state.published to state.paused while the
/// voice clips (design system 10.8, review finding B-30). The words under the meter say it too.
/// </summary>
internal static class Meter
{
    public static readonly DependencyProperty ClippingProperty = DependencyProperty.RegisterAttached(
        "Clipping", typeof(bool), typeof(Meter), new FrameworkPropertyMetadata(false));

    public static bool GetClipping(DependencyObject element) => (bool)element.GetValue(ClippingProperty);

    public static void SetClipping(DependencyObject element, bool value) => element.SetValue(ClippingProperty, value);
}
