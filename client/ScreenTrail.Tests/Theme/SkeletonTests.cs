using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>A flat, still placeholder for a first load (design system 10.8).</summary>
public sealed class SkeletonTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Theme = "clr-namespace:ScreenTrail.UI.Theme";

    [Fact]
    public void ABlockIsFlatAndStillAndWaitsBeforeItShows()
    {
        var skeleton = ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == "Skeleton");
        var setters = skeleton.Elements(Wpf + "Setter").ToDictionary(s => (string)s.Attribute("Property")!, s => (string?)s.Attribute("Value"));

        Assert.Equal("{DynamicResource Brush.bg.hover}", setters["Background"]);
        Assert.Equal("{DynamicResource Radius.sm}", setters["CornerRadius"]);
        Assert.Equal("0", setters["Opacity"]);

        // One step to full after 300 ms, from code (theme:Waiting.Appear): no shimmer, no pulse, no repeat.
        Assert.Equal("True", setters["theme:Waiting.Appear"]);
        Assert.DoesNotContain(skeleton.Descendants(), e => e.Name.LocalName.Contains("Storyboard", StringComparison.Ordinal) || e.Name.LocalName.Contains("Gradient", StringComparison.Ordinal));
    }

    /// <summary>
    /// In high contrast bg.hover is the window colour, so a block with no edge was invisible and a drafting
    /// note rendered as an empty pane (CI's note render, ST-132). The block's edge is skeleton.edge:
    /// transparent in light and dark, GrayText in high contrast.
    /// </summary>
    [Fact]
    public void InHighContrastABlockIsOutlinedSoItCanBeSeen()
    {
        var skeleton = ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == "Skeleton");
        var setters = skeleton.Elements(Wpf + "Setter").ToDictionary(s => (string)s.Attribute("Property")!, s => (string?)s.Attribute("Value"));
        Assert.Equal("{DynamicResource Brush.skeleton.edge}", setters["BorderBrush"]);
        Assert.Equal("{DynamicResource Thickness.border}", setters["BorderThickness"]);

        var theme = Path.Combine(Ui(), "Theme");
        string Edge(string file) => (string)XDocument.Load(Path.Combine(theme, file)).Root!.Elements()
            .Single(e => (string?)e.Attribute(X + "Key") == "Token.skeleton.edge").Attribute("Member")!;
        Assert.Equal("SystemColors.GrayTextColor", Edge("Tokens.HighContrast.xaml"));
        foreach (var file in new[] { "Tokens.Light.g.xaml", "Tokens.Dark.g.xaml" })
        {
            Assert.Equal("#00000000", XDocument.Load(Path.Combine(theme, file)).Root!.Elements().Single(e => (string?)e.Attribute(X + "Key") == "Token.skeleton.edge").Value);
        }
    }

    [Fact]
    public void TheProgressRingWaitsTheSame300MsAndTurnsFromCode()
    {
        var ring = ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == "ProgressRing");
        Assert.Contains(ring.Descendants(), e => (string?)e.Attribute(X + "Name") == "Ring" && (string?)e.Attribute(Theme + "Waiting.Appear") == "True");
        Assert.Contains(ring.Descendants(), e => (string?)e.Attribute(X + "Name") == "Arc" && (string?)e.Attribute(Theme + "Waiting.Spin") == "True");
    }

    /// <summary>
    /// Spec §2.3, §7: a storyboard declared in XAML runs whatever Windows' animation setting says, so no
    /// view or style declares one; motion starts from code, through ThemeManager.Motion. The Windows suite
    /// audits the live windows for this; ST-130's ring and skeleton broke it, and the ring kept turning
    /// with reduce motion on.
    /// </summary>
    [Fact]
    public void NoXamlDeclaresAStoryboard()
    {
        var ui = Ui();
        var declaring = Directory.EnumerateFiles(ui, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => XDocument.Load(f).Descendants().Any(e => e.Name.LocalName is "BeginStoryboard" or "Storyboard"))
            .Select(f => Path.GetRelativePath(ui, f));
        Assert.Empty(declaring);
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

    private static string Ui()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "client", "ScreenTrail.UI");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("client/ScreenTrail.UI was not found above the test output directory");
    }
}
