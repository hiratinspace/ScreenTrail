using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>The slider and the level meter (design system 10.3 and 10.8; review findings B-23, B-30).</summary>
public sealed class SliderAndMeterTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void TheThumbKeepsItsSizeAndStepsDarkerWhenHeldOrHovered()
    {
        var thumb = Keyed("Slider.Thumb");
        var knob = thumb.Descendants(Wpf + "Ellipse").Single();
        Assert.Equal("{DynamicResource Layout.slider-thumb}", (string?)knob.Attribute("Width"));
        Assert.Equal("{DynamicResource Brush.bg.surface}", (string?)knob.Attribute("Stroke"));
        Assert.Equal("2", (string?)knob.Attribute("StrokeThickness"));

        var setters = thumb.Descendants(Wpf + "Setter").Where(s => s.Attribute("TargetName") is not null).ToList();
        Assert.DoesNotContain(setters, s => (string?)s.Attribute("Property") is "Width" or "Height");
        Assert.Contains(setters, s => (string?)s.Attribute("Value") == "{DynamicResource Brush.accent.primary.hover}");
        Assert.Contains(setters, s => (string?)s.Attribute("Value") == "{DynamicResource Brush.accent.primary.pressed}");
    }

    [Fact]
    public void ALockedSlidersFillIsNotIris()
    {
        var disabled = Keyed("Slider.Fill").Descendants(Wpf + "Trigger").Single(t => (string?)t.Attribute("Property") == "IsEnabled");
        Assert.Equal("{DynamicResource Brush.text.disabled}", (string?)disabled.Element(Wpf + "Setter")!.Attribute("Value"));
    }

    private static string? Setter(XElement style, string property) =>
        (string?)style.Elements(Wpf + "Setter").Single(s => (string?)s.Attribute("Property") == property).Attribute("Value");

    private static XElement Keyed(string key) => ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == key);
}
