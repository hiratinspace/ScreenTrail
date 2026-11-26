using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>
/// The envelope's As segments and Also chips (design system 10.3 and 10.5; review findings A-18, B-20, A-19,
/// B-21): nested corners, one selection signal, and a chip mark that is a square, not a near-circle.
/// </summary>
public sealed class SegmentAndChipTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ASegmentsCornersNestInsideItsGroup()
    {
        var group = Keyed("Segment.Group");
        Assert.Equal("2", Setter(group, "Padding"));
        Assert.Equal("{DynamicResource Radius.control}", Setter(group, "CornerRadius"));
        Assert.Equal("{DynamicResource Brush.border.strong}", Setter(group, "BorderBrush"));

        var chrome = Named(Keyed("Segment"), "Chrome");
        Assert.Equal("{DynamicResource Radius.sm}", (string?)chrome.Attribute("CornerRadius"));
    }

    [Fact]
    public void TheChosenSegmentIsTintedAndBoldWithNoUnderline()
    {
        var segment = Keyed("Segment");
        Assert.DoesNotContain(segment.Descendants(), e => (string?)e.Attribute(X + "Name") == "Bar");
        Assert.Equal("{DynamicResource Brush.text.secondary}", Setter(segment, "Foreground"));

        var chosen = segment.Descendants(Wpf + "Trigger").Single(t => (string?)t.Attribute("Property") == "IsChecked").Elements(Wpf + "Setter").ToList();
        Assert.Contains(chosen, s => (string?)s.Attribute("Value") == "{DynamicResource Brush.bg.selected}");
        Assert.Contains(chosen, s => (string?)s.Attribute("Value") == "{DynamicResource Weight.semibold}");
        Assert.Contains(chosen, s => (string?)s.Attribute("Value") == "{DynamicResource Thickness.selected-edge}");
    }

    [Fact]
    public void AChipsMarkIsASquareAndTheChipTintsWhenOn()
    {
        var chip = Keyed("CheckChip");
        var box = Named(chip, "Box");
        Assert.Equal("{DynamicResource Radius.sm}", (string?)box.Attribute("CornerRadius"));
        Assert.Equal("{DynamicResource Layout.choice-mark}", (string?)box.Attribute("Width"));
        Assert.Equal("{DynamicResource Brush.border.strong}", (string?)Named(chip, "Chrome").Attribute("BorderBrush"));
        Assert.Equal("8,0,12,0", Setter(chip, "Padding"));

        var on = chip.Descendants(Wpf + "Trigger").Single(t => (string?)t.Attribute("Property") == "IsChecked");
        Assert.Contains(on.Elements(Wpf + "Setter"), s => (string?)s.Attribute("TargetName") == "Chrome" && (string?)s.Attribute("Value") == "{DynamicResource Brush.bg.selected}");
        Assert.Empty(chip.Descendants(Wpf + "Path"));
    }

    [Theory]
    [InlineData("Tokens.Light.g.xaml", "0")]
    [InlineData("Tokens.Dark.g.xaml", "0")]
    [InlineData("Tokens.HighContrast.xaml", "2")]
    public void OnlyHighContrastDrawsTheChosenSegmentsEdge(string file, string thickness)
    {
        var root = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Theme", file)).Root!;
        Assert.Equal(thickness, root.Elements(Wpf + "Thickness").Single(t => (string?)t.Attribute(X + "Key") == "Thickness.selected-edge").Value);
    }

    private static string? Setter(XElement style, string property) =>
        (string?)style.Elements(Wpf + "Setter").Single(s => (string?)s.Attribute("Property") == property).Attribute("Value");

    private static XElement Named(XElement scope, string name) => scope.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    private static XElement Keyed(string key) => ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == key);
}
