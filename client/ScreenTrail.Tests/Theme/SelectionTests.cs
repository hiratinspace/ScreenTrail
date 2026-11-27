using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>One selection treatment everywhere (design system 10.4, review finding A-01).</summary>
public sealed class SelectionTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void TheBarIsAThreeBySixteenPillInTheRibbonBand()
    {
        var bar = Keyed("SelectionBar").Elements(Wpf + "Setter").ToDictionary(s => (string)s.Attribute("Property")!, s => (string?)s.Attribute("Value"));
        Assert.Equal("3", bar["Width"]);
        Assert.Equal("{DynamicResource Icon.control}", bar["Height"]);
        Assert.Equal("1.5", bar["RadiusX"]);
        Assert.Equal("{DynamicResource Brush.brand.ribbon.band}", bar["Fill"]);
    }

    [Fact]
    public void EverySelectedRowDrawsTheSameBar()
    {
        var bars = ChoiceStatesTests.Components().Descendants(Wpf + "Rectangle").Where(r => (string?)r.Attribute(X + "Name") == "Indicator").ToList();

        // The keyless list item, Settings' nav, the table row, the rail row and the rail's foot.
        Assert.Equal(5, bars.Count);
        Assert.All(bars, b => Assert.Equal("{StaticResource SelectionBar}", (string?)b.Attribute("Style")));
        Assert.All(bars, b => Assert.Null(b.Attribute("Fill")));
    }

    [Fact]
    public void TheFootsSelectedItemIsBgSurfaceNotTheHoverGrey()
    {
        var selected = Keyed("Rail.FootItem").Descendants(Wpf + "Trigger").Single(t => (string?)t.Attribute("Property") == "Tag");
        Assert.Contains(selected.Elements(Wpf + "Setter"), s => (string?)s.Attribute("TargetName") == "Chrome" && (string?)s.Attribute("Value") == "{DynamicResource Brush.bg.surface}");
    }

    private static XElement Keyed(string key) => ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == key);
}

/// <summary>Settings' nav rows (design system 10.4, review finding B-44).</summary>
public sealed class SettingsNavRowTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ARowIsThirtyTwoHighWithOrWithoutACount()
    {
        var item = Keyed("Settings.NavItem");
        var setters = item.Elements(Wpf + "Setter").Where(s => s.Attribute("Value") is not null).ToDictionary(s => (string)s.Attribute("Property")!, s => (string?)s.Attribute("Value"));
        Assert.Equal("{DynamicResource Layout.nav-row-height}", setters["Height"]);
        Assert.Equal("12,0", setters["Padding"]);
        Assert.Equal("0,0,0,2", setters["Margin"]);
        Assert.Equal("{DynamicResource Radius.control}", (string?)item.Descendants(Wpf + "Border").Single().Attribute("CornerRadius"));
    }

    [Fact]
    public void TheSelectedNameIsBodyStrong()
    {
        var label = Keyed("Settings.NavLabel");
        var selected = label.Descendants(Wpf + "DataTrigger").Single();
        Assert.Contains(selected.Elements(Wpf + "Setter"), s => (string?)s.Attribute("Property") == "FontWeight");
        Assert.Contains(label.Elements(Wpf + "Setter"), s => (string?)s.Attribute("Value") == "{DynamicResource Brush.text.secondary}");
    }

    private static XElement Keyed(string key) => ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == key);
}
