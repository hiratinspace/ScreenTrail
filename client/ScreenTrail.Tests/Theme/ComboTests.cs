using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>The combo and its open list (design system 10.2, review finding B-17).</summary>
public sealed class ComboTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Controls = "clr-namespace:ScreenTrail.UI.Controls";

    [Fact]
    public void TheOpenListFloatsOnAShadowThatIsNotBehindItsText()
    {
        var popup = Combo().Descendants(Wpf + "Popup").Single();
        var layers = popup.Element(Wpf + "Grid")!.Elements(Wpf + "Border").ToList();

        Assert.Equal(2, layers.Count);
        Assert.Equal("{DynamicResource Elevation.popup}", (string?)layers[0].Attribute("Effect"));
        Assert.Empty(layers[0].Elements());
        Assert.Null(layers[1].Attribute("Effect"));
        Assert.Equal("{DynamicResource Brush.border.strong}", (string?)layers[1].Attribute("BorderBrush"));
    }

    [Fact]
    public void TheChosenItemIsTintedAndCheckedWithNoBar()
    {
        var item = Components().Elements(Wpf + "Style").Single(s => s.Attribute(X + "Key") is null && (string?)s.Attribute("TargetType") == "ComboBoxItem");

        Assert.Empty(item.Descendants(Wpf + "Rectangle"));
        Assert.Contains(item.Descendants(Controls + "Glyph"), g => (string?)g.Attribute("Text") == "{StaticResource Icon.Check}");
    }

    [Fact]
    public void TheChevronIsTheIconAndFocusIsTheRingUntilTheListOpens()
    {
        var toggle = Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == "ComboBox.Toggle");
        Assert.Empty(toggle.Descendants(Wpf + "Path"));
        Assert.Contains(toggle.Descendants(Controls + "Glyph"), g => (string?)g.Attribute("Text") == "{StaticResource Icon.ChevronDown}");

        var combo = Combo();
        Assert.DoesNotContain(combo.Descendants(Wpf + "Trigger"), t => (string?)t.Attribute("Property") == "IsKeyboardFocusWithin");
        var open = combo.Descendants(Wpf + "Trigger").Single(t => (string?)t.Attribute("Property") == "IsDropDownOpen");
        Assert.Equal("FocusEdge", (string?)open.Element(Wpf + "Setter")!.Attribute("TargetName"));
    }

    private static XElement Combo() => Components().Elements(Wpf + "Style").Single(s => s.Attribute(X + "Key") is null && (string?)s.Attribute("TargetType") == "ComboBox");

    private static XElement Components() => XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Theme", "Components.xaml")).Root!;
}
