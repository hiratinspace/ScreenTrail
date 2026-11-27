using System.Windows;

namespace ScreenTrail.UI.Theme;

/// <summary>
/// The one banner (design system 10.6, review findings A-11, A-12, B-26): <c>theme:Banner.Tone</c> is Info
/// (the default), Success, Warning or Danger, and draws the fill, the soft edge and the 16 px glyph at the
/// left, 2 below the first line's top and 8 before the words; <c>theme:Banner.Title</c> is an optional Body
/// strong first line; <c>theme:Banner.Glyph</c> replaces the tone's glyph (Icon.Unknown for "Not
/// confirmed"), or hides it when empty, for a banner that draws its own.
/// </summary>
internal static class Banner
{
    public static readonly DependencyProperty ToneProperty = DependencyProperty.RegisterAttached(
        "Tone", typeof(string), typeof(Banner), new FrameworkPropertyMetadata("Info"));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.RegisterAttached(
        "Title", typeof(string), typeof(Banner), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
        "Glyph", typeof(string), typeof(Banner), new FrameworkPropertyMetadata(null));

    public static string GetTone(DependencyObject element) => (string)element.GetValue(ToneProperty);

    public static void SetTone(DependencyObject element, string value) => element.SetValue(ToneProperty, value);

    public static string? GetTitle(DependencyObject element) => (string?)element.GetValue(TitleProperty);

    public static void SetTitle(DependencyObject element, string? value) => element.SetValue(TitleProperty, value);

    public static string? GetGlyph(DependencyObject element) => (string?)element.GetValue(GlyphProperty);

    public static void SetGlyph(DependencyObject element, string? value) => element.SetValue(GlyphProperty, value);
}
