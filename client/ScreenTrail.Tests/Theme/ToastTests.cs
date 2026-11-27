using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>
/// The toast's shadow is on an empty border behind its words (design system 6, review findings A-28, B-27,
/// D-18): an effect on the element holding text renders it to a bitmap, and the words lose ClearType.
/// </summary>
public sealed class ToastTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void TheShadowIsTheFloatElevationOnAnEmptySibling()
    {
        var toast = ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == "Toast");

        Assert.DoesNotContain(toast.Elements(Wpf + "Setter"), s => (string?)s.Attribute("Property") == "Effect");
        Assert.Empty(toast.Descendants(Wpf + "DropShadowEffect"));
        var layers = toast.Descendants(Wpf + "ControlTemplate").Single().Element(Wpf + "Grid")!.Elements(Wpf + "Border").ToList();
        Assert.Equal("{DynamicResource Elevation.float}", (string?)layers[0].Attribute("Effect"));
        Assert.Empty(layers[0].Elements());
        Assert.Null(layers[1].Attribute("Effect"));
        Assert.Equal("16,12,12,12", (string?)toast.Elements(Wpf + "Setter").Single(s => (string?)s.Attribute("Property") == "Padding").Attribute("Value"));
    }

    [Fact]
    public void NoStyleInTheProductCarriesItsOwnShadow()
    {
        // Elevation comes from the generated tokens only, so light, dark and high contrast each get theirs.
        Assert.Empty(ChoiceStatesTests.Components().Descendants(Wpf + "DropShadowEffect"));
        Assert.DoesNotContain(ChoiceStatesTests.Components().Elements(Wpf + "Style"), s => (string?)s.Attribute(X + "Key") == "StatusPill");
    }
}
