using System.Windows;

namespace ScreenTrail.UI.Theme;

/// <summary>
/// The one badge (design system 10.5, review findings A-02, B-24): <c>theme:Badge.Tone</c> is Neutral (the
/// default), Accent, Success, Warning, Danger or Scope, and draws the fill, edge and glyph colour;
/// <c>theme:Badge.Glyph</c> is an optional 12 px glyph 4 before the words. The words are always
/// text.primary, so the state is said in a word and the colour only agrees with it.
/// </summary>
internal static class Badge
{
    public static readonly DependencyProperty ToneProperty = DependencyProperty.RegisterAttached(
        "Tone", typeof(string), typeof(Badge), new FrameworkPropertyMetadata("Neutral"));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
        "Glyph", typeof(string), typeof(Badge), new FrameworkPropertyMetadata(null));

    public static string GetTone(DependencyObject element) => (string)element.GetValue(ToneProperty);

    public static void SetTone(DependencyObject element, string value) => element.SetValue(ToneProperty, value);

    public static string? GetGlyph(DependencyObject element) => (string?)element.GetValue(GlyphProperty);

    public static void SetGlyph(DependencyObject element, string? value) => element.SetValue(GlyphProperty, value);
}
