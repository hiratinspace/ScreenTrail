using System.Windows;

namespace ScreenTrail.UI.Theme;

/// <summary>
/// A button's leading icon (design system 10.1): <c>theme:ButtonIcon.Leading="{StaticResource Icon.Delete}"</c>
/// draws the glyph 16 px, 8 before the label, in the label's colour. The label stays the button's
/// <c>Content</c>, so its accessible name and access key are unchanged. Button.Danger and
/// Button.DangerSolid set Icon.Delete, so a destructive button is told by shape too, not by red alone,
/// which high contrast takes away (review finding B-06).
/// </summary>
internal static class ButtonIcon
{
    public static readonly DependencyProperty LeadingProperty = DependencyProperty.RegisterAttached(
        "Leading", typeof(string), typeof(ButtonIcon), new FrameworkPropertyMetadata(null));

    public static string? GetLeading(DependencyObject element) => (string?)element.GetValue(LeadingProperty);

    public static void SetLeading(DependencyObject element, string? value) => element.SetValue(LeadingProperty, value);
}
