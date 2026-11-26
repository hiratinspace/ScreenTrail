using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>The tooltip is a small level 2 surface (design system 10.6, review finding B-28).</summary>
public sealed class ToolTipTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ATipIsRadiusFourWithItsShadowBehindItsWords()
    {
        var tip = ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => s.Attribute(X + "Key") is null && (string?)s.Attribute("TargetType") == "ToolTip");
        var setters = tip.Elements(Wpf + "Setter").ToDictionary(s => (string)s.Attribute("Property")!, s => (string?)s.Attribute("Value"));

        Assert.Equal("False", setters["HasDropShadow"]);
        Assert.Equal("Bottom", setters["Placement"]);
        Assert.Equal("{DynamicResource Layout.tooltip-max-width}", setters["MaxWidth"]);

        var layers = tip.Descendants(Wpf + "ControlTemplate").Single().Element(Wpf + "Grid")!.Elements(Wpf + "Border").ToList();
        Assert.Equal("{DynamicResource Elevation.popup}", (string?)layers[0].Attribute("Effect"));
        Assert.Empty(layers[0].Elements());
        Assert.Equal("8,4", (string?)layers[1].Attribute("Padding"));
        Assert.Equal("{DynamicResource Radius.sm}", (string?)layers[1].Attribute("CornerRadius"));
        Assert.Equal("Enabled", (string?)layers[1].Attribute("RenderOptions.ClearTypeHint"));
    }
}
