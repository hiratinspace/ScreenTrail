using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>A flat, still placeholder for a first load (design system 10.8).</summary>
public sealed class SkeletonTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ABlockIsFlatAndStillAndWaitsBeforeItShows()
    {
        var skeleton = ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == "Skeleton");
        var setters = skeleton.Elements(Wpf + "Setter").ToDictionary(s => (string)s.Attribute("Property")!, s => (string?)s.Attribute("Value"));

        Assert.Equal("{DynamicResource Brush.bg.hover}", setters["Background"]);
        Assert.Equal("{DynamicResource Radius.sm}", setters["CornerRadius"]);
        Assert.Equal("0", setters["Opacity"]);

        // One step to full after 300 ms: no shimmer, no pulse, no repeat.
        var appear = Assert.Single(skeleton.Descendants(Wpf + "DoubleAnimation"));
        Assert.Equal("0:0:0.3", (string?)appear.Attribute("BeginTime"));
        Assert.Equal("Opacity", (string?)appear.Attribute("Storyboard.TargetProperty"));
        Assert.DoesNotContain(skeleton.Descendants(), e => e.Attribute("RepeatBehavior") is not null || e.Name.LocalName.Contains("Gradient", StringComparison.Ordinal));
    }

    [Fact]
    public void TheProgressRingWaitsTheSame300Ms()
    {
        var ring = ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == "ProgressRing");
        Assert.Contains(ring.Descendants(Wpf + "DoubleAnimation"), a => (string?)a.Attribute("BeginTime") == "0:0:0.3" && (string?)a.Attribute("Storyboard.TargetProperty") == "Opacity");
    }

    [Fact]
    public void TheRailShowsThreeTwoLineRowsWhileItFirstAsks()
    {
        var rail = EmptyStateTests.View("History", "InboxRailView.xaml");
        var lines = rail.Descendants(Wpf + "Border").Where(b => (string?)b.Attribute("Style") == "{StaticResource Skeleton.Frame}").ToList();
        Assert.Equal(6, lines.Count);

        var asking = "{x:Static core:InboxRail.Asking}";
        // Collapsed but for the first ask, and all six lines inside that one block.
        var block = lines[0].Ancestors(Wpf + "StackPanel").First(p => p.Element(Wpf + "StackPanel.Style") is not null);
        var style = block.Element(Wpf + "StackPanel.Style")!;
        Assert.Contains(style.Descendants(Wpf + "Setter"), s => (string?)s.Attribute("Property") == "Visibility" && (string?)s.Attribute("Value") == "Collapsed");
        Assert.Equal(asking, (string?)style.Descendants(Wpf + "DataTrigger").Single().Attribute("Value"));
        Assert.All(lines, l => Assert.Contains(block, l.Ancestors()));
    }
}
