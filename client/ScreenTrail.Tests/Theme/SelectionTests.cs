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

/// <summary>Rail rows sit 8 in from both edges and have a pressed state (design system 10.4, review finding A-05).</summary>
public sealed class RailRowGeometryTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ARowIsInsetEvenlyAndPaddedFromTheToken()
    {
        var row = Keyed("Rail.Row");
        Assert.Equal("8,0,8,2", Setter(row, "Margin"));
        Assert.Equal("{DynamicResource Padding.rail-row}", Setter(row, "Padding"));
    }

    [Theory]
    [InlineData("Rail.Row", "{DynamicResource Brush.shell.pressed}")]
    [InlineData("Settings.NavItem", "{DynamicResource Brush.bg.pressed}")]
    public void ARowDarkensWhileItIsPressed(string key, string fill)
    {
        var row = Keyed(key);
        Assert.Equal("True", Setter(row, "theme:Pressed.Track"));
        var pressed = row.Descendants(Wpf + "Trigger").Single(t => (string?)t.Attribute("Property") == "theme:Pressed.IsPressed");
        Assert.Equal(fill, (string?)pressed.Element(Wpf + "Setter")!.Attribute("Value"));
    }

    private static string? Setter(XElement style, string property) =>
        (string?)style.Elements(Wpf + "Setter").Single(s => (string?)s.Attribute("Property") == property).Attribute("Value");

    private static XElement Keyed(string key) => ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == key);
}

/// <summary>Every rail group heading starts where the rows' titles do (design system 10.4, review finding A-06).</summary>
public sealed class RailGroupHeaderTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("Rail.GroupHeader")]
    [InlineData("Rail.GroupToggle")]
    public void AHeadingHasTheRowsInsetEdgeAndPadding(string key)
    {
        var style = ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == key);
        var setters = style.Elements(Wpf + "Setter").Where(s => s.Attribute("Value") is not null).ToDictionary(s => (string)s.Attribute("Property")!, s => (string?)s.Attribute("Value"));

        // The row: Margin 8 on the left, a 1 px edge, then 12 inside.
        Assert.Equal("8,0,8,2", setters["Margin"]);
        Assert.Equal("12,0", setters["Padding"]);
        Assert.Equal("{DynamicResource Layout.control-height-compact}", setters["MinHeight"]);
        var edge = key == "Rail.GroupHeader" ? setters["BorderThickness"] : (string?)style.Descendants(Wpf + "Border").Single().Attribute("BorderThickness");
        Assert.Equal("{DynamicResource Thickness.border}", edge);
    }

    [Fact]
    public void TheChevronComesAfterTheCount()
    {
        var toggle = EmptyStateTests.View("History", "InboxRailView.xaml").Descendants(Wpf + "ToggleButton").Single();
        var parts = toggle.Descendants(Wpf + "StackPanel").First().Elements().ToList();
        Assert.Equal("{Binding Name}", (string?)parts[0].Attribute("Text"));
        Assert.Equal("{Binding CountText}", (string?)parts[1].Attribute("Text"));
        Assert.Equal("{Binding Chevron}", (string?)parts[2].Attribute("Text"));
    }
}

/// <summary>The rail's foot nav (design system 10.4, review finding A-07).</summary>
public sealed class FootNavTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void AFootItemIsANavRowWithAPressedState()
    {
        var item = ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == "Rail.FootItem");
        Assert.Contains(item.Elements(Wpf + "Setter"), s => (string?)s.Attribute("Property") == "MinHeight" && (string?)s.Attribute("Value") == "{DynamicResource Layout.nav-row-height}");
        var pressed = item.Element(Wpf + "Style.Triggers")!.Elements(Wpf + "Trigger").Single(t => (string?)t.Attribute("Property") == "IsPressed");
        Assert.Equal("{DynamicResource Brush.shell.pressed}", (string?)pressed.Element(Wpf + "Setter")!.Attribute("Value"));
    }

    [Fact]
    public void TheFootIsInsetEvenlyAndItsItemsAreTwoApart()
    {
        var shell = EmptyStateTests.View("Shell", "ShellWindow.xaml");
        var items = shell.Descendants(Wpf + "Button").Where(b => ((string?)b.Attribute("Style"))?.Contains("Foot", StringComparison.Ordinal) == true || (string?)b.Attribute("Style") == "{StaticResource Rail.FootItem}").ToList();
        Assert.Equal(3, items.Count);
        Assert.Null(items[0].Attribute("Margin"));
        Assert.All(items.Skip(1), b => Assert.Equal("0,2,0,0", (string?)b.Attribute("Margin")));
        Assert.Equal("8", (string?)items[0].Parent!.Parent!.Attribute("Padding"));
    }
}
