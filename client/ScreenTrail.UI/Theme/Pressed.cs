using System.Windows;
using System.Windows.Input;

namespace ScreenTrail.UI.Theme;

/// <summary>
/// <c>theme:Pressed.Track</c> gives a row that is not a button a pressed state (design system 10.4, review
/// finding A-05): <c>theme:Pressed.IsPressed</c> is true while the left button is held on it, and false
/// again when it is let go, the pointer leaves, or the row loses the mouse. A list item has no IsPressed
/// of its own, so a row with hover and no press felt dead on click.
/// </summary>
internal static class Pressed
{
    public static readonly DependencyProperty TrackProperty = DependencyProperty.RegisterAttached(
        "Track", typeof(bool), typeof(Pressed), new FrameworkPropertyMetadata(false, OnTrackChanged));

    public static readonly DependencyProperty IsPressedProperty = DependencyProperty.RegisterAttached(
        "IsPressed", typeof(bool), typeof(Pressed), new FrameworkPropertyMetadata(false));

    public static bool GetTrack(DependencyObject element) => (bool)element.GetValue(TrackProperty);

    public static void SetTrack(DependencyObject element, bool value) => element.SetValue(TrackProperty, value);

    public static bool GetIsPressed(DependencyObject element) => (bool)element.GetValue(IsPressedProperty);

    public static void SetIsPressed(DependencyObject element, bool value) => element.SetValue(IsPressedProperty, value);

    private static void OnTrackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.PreviewMouseLeftButtonDown -= OnDown;
        element.PreviewMouseLeftButtonUp -= OnUp;
        element.MouseLeave -= OnUp;
        element.LostMouseCapture -= OnUp;
        if ((bool)e.NewValue)
        {
            element.PreviewMouseLeftButtonDown += OnDown;
            element.PreviewMouseLeftButtonUp += OnUp;
            element.MouseLeave += OnUp;
            element.LostMouseCapture += OnUp;
        }
        else
        {
            element.ClearValue(IsPressedProperty);
        }
    }

    private static void OnDown(object sender, MouseButtonEventArgs e) => ((DependencyObject)sender).SetValue(IsPressedProperty, true);

    private static void OnUp(object sender, MouseEventArgs e) => ((DependencyObject)sender).SetValue(IsPressedProperty, false);
}
