using System.Globalization;
using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>
/// The focus ring follows the shape it circles (design system 5, review finding B-32): its radius is the
/// control's plus the 3 px gap, so it runs parallel to the edge instead of one 8 px ring on every shape.
/// </summary>
public sealed class FocusRingTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("FocusRing", "Radius.control")]
    [InlineData("FocusRing.Small", "Radius.sm")]
    public void ARingsRadiusIsItsShapesPlusTheGap(string ring, string shape)
    {
        var rectangle = Ring(ring);
        var gap = -double.Parse((string)rectangle.Attribute("Margin")!, CultureInfo.InvariantCulture);

        Assert.Equal(Radius(shape) + gap, double.Parse((string)rectangle.Attribute("RadiusX")!, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("CheckBox", "FocusRing.Small")]
    [InlineData("RadioButton", "FocusRing.Small")]
    public void EachShapeTakesItsOwnRing(string target, string ring)
    {
        var style = Components().Elements(Wpf + "Style").Single(s => s.Attribute(X + "Key") is null && (string?)s.Attribute("TargetType") == target);

        Assert.Contains(style.Elements(Wpf + "Setter"), s => (string?)s.Attribute("Property") == "FocusVisualStyle" && (string?)s.Attribute("Value") == $"{{StaticResource {ring}}}");
    }

    private static XElement Ring(string key) =>
        Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == key).Descendants(Wpf + "Rectangle").Single();

    private static double Radius(string key) =>
        double.Parse(
            XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Theme", "Tokens.Common.g.xaml")).Root!.Elements().Single(e => (string?)e.Attribute(X + "Key") == key).Value,
            CultureInfo.InvariantCulture);

    private static XElement Components() => XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Theme", "Components.xaml")).Root!;
}
