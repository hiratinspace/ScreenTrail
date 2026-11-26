using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>
/// The check box and radio have every state a Windows choice has (design system 10.3, review finding B-18):
/// pressed off and on, hover on, and the disabled triple, with the tick drawn by the icon font.
/// </summary>
public sealed class ChoiceStatesTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Controls = "clr-namespace:ScreenTrail.UI.Controls";

    [Theory]
    [InlineData("CheckBox")]
    [InlineData("RadioButton")]
    public void PressedAndCheckedHoverHaveTheirOwnFills(string type)
    {
        var template = Implicit(type).Descendants(Wpf + "ControlTemplate").Single();
        var values = template.Descendants(Wpf + "Setter").Select(s => (string?)s.Attribute("Value")).ToList();

        Assert.Contains("{DynamicResource Brush.bg.pressed}", values);
        Assert.Contains("{DynamicResource Brush.accent.primary.hover}", values);
        Assert.Contains("{DynamicResource Brush.accent.primary.pressed}", values);
        Assert.Contains(template.Descendants(Wpf + "MultiTrigger"), m => m.Descendants(Wpf + "Condition").Any(c => (string?)c.Attribute("Property") == "IsPressed"));
    }

    [Theory]
    [InlineData("CheckBox")]
    [InlineData("RadioButton")]
    public void DisabledIsTheDisabledTriple(string type)
    {
        var disabled = Implicit(type).Descendants(Wpf + "Trigger").Single(t => (string?)t.Attribute("Property") == "IsEnabled");
        var values = disabled.Elements(Wpf + "Setter").Select(s => (string?)s.Attribute("Value")).ToList();

        Assert.Contains("{DynamicResource Brush.bg.disabled}", values);
        Assert.Contains("{DynamicResource Brush.border.subtle}", values);
        Assert.Contains("{DynamicResource Brush.text.disabled}", values);
        Assert.DoesNotContain("{DynamicResource Brush.text.muted}", values);
    }

    [Fact]
    public void TheTickIsTheIconFontsCheckInASmallRoundedSquare()
    {
        var box = Implicit("CheckBox");

        Assert.Empty(box.Descendants(Wpf + "Path"));
        var tick = box.Descendants(Controls + "Glyph").Single(g => (string?)g.Attribute(X + "Name") == "Tick");
        Assert.Equal("{StaticResource Icon.Check}", (string?)tick.Attribute("Text"));
        var mark = box.Descendants(Wpf + "Border").Single(b => (string?)b.Attribute(X + "Name") == "Box");
        Assert.Equal("{DynamicResource Radius.sm}", (string?)mark.Attribute("CornerRadius"));
        Assert.Equal("{DynamicResource Layout.choice-mark}", (string?)mark.Attribute("Width"));
    }

    [Theory]
    [InlineData("CheckBox")]
    [InlineData("RadioButton")]
    public void AChoiceIsAsWideAsItsLabelSoItsRingHugsIt(string type)
    {
        // Review finding B-19: stretched to the card, the ring ran 664 px around one short line.
        Assert.Contains(Implicit(type).Elements(Wpf + "Setter"), s => (string?)s.Attribute("Property") == "HorizontalAlignment" && (string?)s.Attribute("Value") == "Left");
    }

    internal static XElement Implicit(string type) =>
        Components().Elements(Wpf + "Style").Single(s => s.Attribute(X + "Key") is null && (string?)s.Attribute("TargetType") == type);

    internal static XElement Components() => XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Theme", "Components.xaml")).Root!;
}
