using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenTrail.UI.Theme;

/// <summary>
/// <c>theme:RoundedClip.Inside="True"</c> on a Border clips its child to the inside of its rounded
/// corners. A Border's CornerRadius rounds its own fill and edge, not what it holds, so a table in a card
/// drew its header and its last row's selection square over the card's corners (review finding A-09).
/// </summary>
internal static class RoundedClip
{
    public static readonly DependencyProperty InsideProperty = DependencyProperty.RegisterAttached(
        "Inside", typeof(bool), typeof(RoundedClip), new FrameworkPropertyMetadata(false, OnInsideChanged));

    public static bool GetInside(DependencyObject element) => (bool)element.GetValue(InsideProperty);

    public static void SetInside(DependencyObject element, bool value) => element.SetValue(InsideProperty, value);

    private static void OnInsideChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border)
        {
            return;
        }

        border.SizeChanged -= OnSizeChanged;
        if ((bool)e.NewValue)
        {
            border.SizeChanged += OnSizeChanged;
            Clip(border);
        }
        else if (border.Child is UIElement child)
        {
            child.Clip = null;
        }
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => Clip((Border)sender);

    /// <summary>The child's clip: the border's inside, with the corner radius less the edge.</summary>
    private static void Clip(Border border)
    {
        if (border.Child is not FrameworkElement child)
        {
            return;
        }

        var edge = border.BorderThickness;
        var width = Math.Max(0, border.ActualWidth - edge.Left - edge.Right - border.Padding.Left - border.Padding.Right);
        var height = Math.Max(0, border.ActualHeight - edge.Top - edge.Bottom - border.Padding.Top - border.Padding.Bottom);
        var radius = Math.Max(0, border.CornerRadius.TopLeft - Math.Max(edge.Left, edge.Top));
        child.Clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
    }
}
