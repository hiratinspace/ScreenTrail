using System.Xml.Linq;
using ScreenTrail.Tests.Theme;

namespace ScreenTrail.Tests.Settings;

/// <summary>
/// The Settings page's surfaces and rhythm, from the premium review (ST-140): what each tile, heading and
/// list row is drawn with.
/// </summary>
public sealed class SettingsPolishTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XElement Components() => EmptyStateTests.View("Theme", "Components.xaml");

    private static XElement Style(XElement file, string key) =>
        file.Descendants(Wpf + "Style").Single(s => (string?)s.Attribute(Xaml + "Key") == key);

    private static string? Setter(XElement style, string property) =>
        (string?)style.Elements(Wpf + "Setter").SingleOrDefault(s => (string?)s.Attribute("Property") == property)?.Attribute("Value");

    /// <summary>Review finding B-40: a tile is a card, radius 8; at 6 the banners inside it were rounder than it.</summary>
    [Fact]
    public void ATileHasTheCardsRadius()
    {
        Assert.Equal("{DynamicResource Radius.card}", Setter(Style(Components(), "Settings.Tile"), "CornerRadius"));
    }

    /// <summary>Review finding B-41: "Your data" is outlined like its neighbours; the danger is its Delete button's.</summary>
    [Fact]
    public void YourDataIsATileLikeTheOthers()
    {
        var privacy = EmptyStateTests.View("Settings/Privacy", "PrivacyPane.xaml");
        var tile = privacy.Descendants(Wpf + "Border").Single(b => (string?)b.Attribute(Xaml + "Name") == "YourData");
        Assert.Equal("{StaticResource Settings.Tile}", (string?)tile.Attribute("Style"));
        Assert.Null(tile.Attribute("BorderBrush"));
        Assert.Null(tile.Attribute("Margin"));
        Assert.Contains(tile.Descendants(Wpf + "Button"), b => (string?)b.Attribute("Style") == "{StaticResource Button.Danger}");
    }

    /// <summary>
    /// Review finding B-45: a section heading has 32 above and 12 below, every one alike, and the first is
    /// level with the nav (whose first item is 16 down). Activation's heading had its own 16 and 12.
    /// </summary>
    [Fact]
    public void SectionHeadingsShareOneRhythm()
    {
        var pane = EmptyStateTests.View("Settings", "SettingsPane.xaml");
        Assert.Equal("0,32,0,12", Setter(Style(pane, "Settings.Group"), "Margin"));
        var headings = pane.Descendants(Wpf + "TextBlock").Where(t => (string?)t.Attribute("Style") == "{StaticResource Settings.Group}").ToList();
        Assert.Equal("0,16,0,12", (string?)headings[0].Attribute("Margin"));
        Assert.All(headings.Skip(1), h => Assert.Null(h.Attribute("Margin")));
    }

    /// <summary>Review finding B-49: a hint under a choice starts at its label, 18 for the mark and 8 for the gap.</summary>
    [Fact]
    public void AHintUnderAChoiceStartsAtItsLabel()
    {
        var hint = Style(Components(), "Choice.Hint");
        Assert.Equal("{StaticResource Inset.choice-hint}", Setter(hint, "Margin"));
        var appearance = EmptyStateTests.View("Settings/Appearance", "AppearancePane.xaml");
        var hints = appearance.Descendants(Wpf + "TextBlock").Where(t => (string?)t.Attribute("Style") == "{StaticResource Choice.Hint}").ToList();
        Assert.Equal(2, hints.Count);
        Assert.DoesNotContain(appearance.Descendants(Wpf + "TextBlock"), t => ((string?)t.Attribute("Margin"))?.StartsWith("24,", StringComparison.Ordinal) == true);
    }
}
