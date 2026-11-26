using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>
/// A text field shows focus as a 2 px accent edge over its 1 px one, so its text never moves, and with no
/// second ring outside it except in high contrast (design system 10.2, review finding B-09).
/// </summary>
public sealed class InputFocusTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void NoFieldThickensItsOwnEdgeOnFocus()
    {
        var thickened = Components().Descendants(Wpf + "Trigger")
            .Where(t => (string?)t.Attribute("Property") == "IsKeyboardFocused")
            .SelectMany(t => t.Elements(Wpf + "Setter"))
            .Where(s => (string?)s.Attribute("Property") == "BorderThickness")
            .ToList();

        Assert.Empty(thickened);
    }

    [Fact]
    public void TheSharedTemplateDrawsTheFocusEdgeOverTheChrome()
    {
        var template = Components().Elements(Wpf + "ControlTemplate").Single(t => (string?)t.Attribute(X + "Key") == "Input.Template");

        var edge = template.Descendants(Wpf + "Border").Single(b => (string?)b.Attribute(X + "Name") == "FocusEdge");
        Assert.Equal("False", (string?)edge.Attribute("IsHitTestVisible"));
        Assert.Equal("{DynamicResource Thickness.focus}", (string?)edge.Attribute("BorderThickness"));
        Assert.Equal("Collapsed", (string?)edge.Attribute("Visibility"));
    }

    [Theory]
    [InlineData("Tokens.Light.g.xaml", false)]
    [InlineData("Tokens.Dark.g.xaml", false)]
    [InlineData("Tokens.HighContrast.xaml", true)]
    public void OnlyHighContrastDrawsARingOutsideAField(string theme, bool ring)
    {
        var focus = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Theme", theme)).Root!
            .Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == "FocusVisual.input");

        Assert.Equal(ring, focus.Descendants(Wpf + "Rectangle").Any());
        Assert.Equal("{DynamicResource FocusVisual.input}", Setter("Input.Default", "FocusVisualStyle"));
    }

    [Fact]
    public void HoverDarkensAFieldsEdgeAndLeavesItsFill()
    {
        // Review finding B-10: the hover fill was the tone of the disabled look.
        var hover = Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == "Input.Default")
            .Descendants(Wpf + "Trigger").Single(t => (string?)t.Attribute("Property") == "IsMouseOver");

        Assert.Equal("{DynamicResource Brush.text.muted}", (string?)hover.Elements(Wpf + "Setter").Single(s => (string?)s.Attribute("Property") == "BorderBrush").Attribute("Value"));
        Assert.DoesNotContain(hover.Elements(Wpf + "Setter"), s => (string?)s.Attribute("Property") == "Background");
    }

    private static string? Setter(string key, string property) =>
        (string?)Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == key)
            .Elements(Wpf + "Setter").SingleOrDefault(s => (string?)s.Attribute("Property") == property)?.Attribute("Value");

    private static XElement Components() => XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Theme", "Components.xaml")).Root!;
}
