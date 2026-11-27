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

    /// <summary>
    /// Review findings A-21 and E-23: a frame marker is an 8 px round mark in text.muted, Iris only when its
    /// frame is selected, on a 28 x 28 button with no chrome; the track is 28 high, one Tab stop with arrows
    /// between markers; the summary is a tabular Caption. The drawing code has a baseline, flat bands with an
    /// edge and no opacity, and no Iris square at rest.
    /// </summary>
    [Fact]
    public void TheTrackIsABaselineWithQuietMarkersThatAreButtons()
    {
        var timeline = EnvelopeLayoutTests.Load("Review", "Timeline", "TimelineView.xaml");
        var marker = timeline.Descendants(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == "Timeline.FrameMarker");
        Assert.Equal("Button", (string?)marker.Attribute("TargetType"));
        var setters = marker.Elements(Wpf + "Setter").ToDictionary(s => (string)s.Attribute("Property")!, s => (string?)s.Attribute("Value"));
        Assert.Equal("{DynamicResource Layout.hit-target-min}", setters["Width"]);
        Assert.Equal("{DynamicResource Layout.hit-target-min}", setters["Height"]);
        var mark = marker.Descendants(Wpf + "Ellipse").Single();
        Assert.Equal("{DynamicResource Brush.text.muted}", (string?)mark.Attribute("Fill"));
        var selected = marker.Descendants(Wpf + "Trigger").Single(t => (string?)t.Attribute("Property") == "Tag");
        Assert.Equal("{DynamicResource Brush.accent.primary}", (string?)selected.Element(Wpf + "Setter")!.Attribute("Value"));

        var track = timeline.Descendants(Wpf + "Canvas").Single(c => (string?)c.Attribute(X + "Name") == "Track");
        Assert.Equal("{DynamicResource Layout.hit-target-min}", (string?)track.Attribute("Height"));
        Assert.Equal("Once", (string?)track.Attribute("KeyboardNavigation.TabNavigation"));
        Assert.Equal("Cycle", (string?)track.Attribute("KeyboardNavigation.DirectionalNavigation"));

        var summary = timeline.Descendants(Wpf + "TextBlock").Single(t => (string?)t.Attribute("Text") == "{Binding Summary}");
        Assert.Equal("{StaticResource Text.Caption}", (string?)summary.Attribute("Style"));
        Assert.Null(summary.Attribute("FontSize"));

        var code = File.ReadAllText(Path.Combine(EnvelopeLayoutTests.Ui(), "Review", "Timeline", "TimelineView.xaml.cs"));
        Assert.DoesNotContain("Opacity", code, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Brush.accent.primary", code, StringComparison.Ordinal);
        Assert.DoesNotContain("⚑", code, StringComparison.Ordinal);
        Assert.Contains("\"Brush.border.subtle\"", code, StringComparison.Ordinal);
        Assert.Contains("\"Brush.border.warning\"", code, StringComparison.Ordinal);
        Assert.Contains("\"Timeline.FrameMarker\"", code, StringComparison.Ordinal);
    }
}
