using System.Xml.Linq;

namespace ScreenTrail.Tests.Review;

/// <summary>
/// ST-137, the timeline, the enlarged frame and the strip (premium review 2026-11-26, A-21, E-23, E-24,
/// A-22, A-26, A-27, D-21, D-27), read from the XAML and the source.
/// </summary>
public sealed class TimelineLayoutTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Controls = "clr-namespace:ScreenTrail.UI.Controls";

    /// <summary>
    /// Review finding E-24: the toggle is a disclosure button bound to the panel's state, named "Timeline and
    /// transcript", with a chevron glyph rather than a "▸" or "▾" in its words.
    /// </summary>
    [Fact]
    public void TheToggleSaysWhetherThePanelIsOpen()
    {
        var timeline = EnvelopeLayoutTests.Load("Review", "Timeline", "TimelineView.xaml");
        var toggle = timeline.Descendants(Controls + "DisclosureButton").Single();
        Assert.Equal("{Binding Expanded, Mode=OneWay}", (string?)toggle.Attribute("IsExpanded"));
        Assert.Equal("{Binding ToggleCommand}", (string?)toggle.Attribute("Command"));
        Assert.Equal("Timeline and transcript", (string?)toggle.Attribute("AutomationProperties.Name"));
        Assert.Equal("Alt+T", (string?)toggle.Attribute("AutomationProperties.AcceleratorKey"));

        var setters = toggle.Descendants(Wpf + "Setter").Select(s => (string?)s.Attribute("Value")).ToList();
        Assert.Contains("{StaticResource Icon.ChevronRight}", setters);
        Assert.Contains("{StaticResource Icon.ChevronDown}", setters);

        var source = File.ReadAllText(Path.Combine(EnvelopeLayoutTests.Ui(), "Review", "Timeline", "TimelineViewModel.cs"));
        Assert.DoesNotContain("▸", source, StringComparison.Ordinal);
        Assert.DoesNotContain("▾", source, StringComparison.Ordinal);
    }
}
