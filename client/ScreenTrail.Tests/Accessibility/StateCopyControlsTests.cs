using System.Xml.Linq;

namespace ScreenTrail.Tests.Accessibility;

/// <summary>
/// The copy and state fixes as the XAML draws them, read off the files so they hold on the Mac: links and
/// Retry named from data, result rows coloured and worded by state, the shell's chip with its state dot,
/// the reason beside a disabled Discard, and the microphone's privacy-page button. What the words are is
/// the Core's and tested there (PublishResultRowsTests, StateCopyTests).
/// </summary>
public sealed class StateCopyControlsTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly XNamespace Controls = "clr-namespace:ScreenTrail.UI.Controls";

    [Fact]
    public void ThePublishPaneNamesNoProviderOfItsOwn()
    {
        var pane = Text("Review/Publish", "PublishPane.xaml");

        Assert.DoesNotContain("Open in ConnectWise", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Retry\"", pane, StringComparison.Ordinal);
    }

    [Fact]
    public void EachResultLinkAndRetryAreNamedFromTheirRow()
    {
        var pane = Load("Review/Publish", "PublishPane.xaml");

        var link = Assert.Single(Buttons(pane), b => (string?)b.Attribute("CommandParameter") == "{Binding Link}");
        Assert.Equal("{Binding LinkText}", (string?)link.Attribute("Content"));
        Assert.Equal("{Binding LinkName}", (string?)link.Attribute("AutomationProperties.Name"));

        var retry = Assert.Single(Buttons(pane), b => (string?)b.Attribute("Command") == "{Binding RetryCommand}");
        Assert.Equal("{Binding RetryLabel}", (string?)retry.Attribute("Content"));
        Assert.Equal("{Binding RetryName}", (string?)retry.Attribute("AutomationProperties.Name"));
    }

    [Fact]
    public void EachResultRowDrawsItsGlyphInTheStatesColourBesideAWord()
    {
        var pane = Load("Review/Publish", "PublishPane.xaml");

        // A Segoe Fluent glyph by the row's Mark (review finding A-24), not a typed tick, 16 px in a 16 px column.
        var glyph = Assert.Single(pane.Descendants(Controls + "Glyph"), g => (string?)g.Attribute("AutomationProperties.Name") == "{Binding MarkName}");
        Assert.Equal("{DynamicResource Icon.control}", (string?)glyph.Attribute("FontSize"));
        Assert.Equal("{DynamicResource Icon.control}", (string?)glyph.Attribute("Width"));
        Assert.Equal("0,0,12,0", (string?)glyph.Attribute("Margin"));
        var setters = glyph.Descendants(Wpf + "Setter").Select(s => (string?)s.Attribute("Value")).ToHashSet();
        Assert.Contains("{DynamicResource Brush.state.published}", setters);
        Assert.Contains("{DynamicResource Brush.state.danger}", setters);
        Assert.Contains("{DynamicResource Brush.state.paused}", setters);
        Assert.Contains("{StaticResource Icon.Check}", setters);
        Assert.Contains("{StaticResource Icon.Close}", setters);
        Assert.Contains("{StaticResource Icon.Unknown}", setters);
        var marks = glyph.Descendants(Wpf + "DataTrigger").Select(t => (string?)t.Attribute("Value")).ToList();
        Assert.Equal(["Icon.Check", "Icon.Close"], marks);

        // Every badge and link starts at one x: shared column sizes across the rows.
        var rows = Assert.Single(pane.Descendants(Controls + "Repeater"), r => (string?)r.Attribute("ItemsSource") == "{Binding Results}");
        Assert.Equal("True", (string?)rows.Attribute("Grid.IsSharedSizeScope"));
        var groups = rows.Descendants(Wpf + "ColumnDefinition").Select(c => (string?)c.Attribute("SharedSizeGroup")).ToList();
        Assert.Equal([null, null, "Badge", "Link"], groups);

        // At least 48 high, a rule between rows and none above the first.
        var row = rows.Descendants(Wpf + "Border").First();
        Assert.Equal("{DynamicResource Layout.receipt-row-height}", (string?)row.Attribute("MinHeight"));
        Assert.Contains(rows.Descendants(Wpf + "DataTrigger"), t => (string?)t.Attribute("Binding") == "{Binding RelativeSource={RelativeSource PreviousData}}");

        Assert.Single(pane.Descendants(Wpf + "ContentControl"), c => (string?)c.Attribute("Content") == "{Binding Badge}");
    }

    [Fact]
    public void TheShellsStatusChipCarriesTheStateDot()
    {
        var shell = Load("Shell", "ShellWindow.xaml");

        // The shared dot, coloured per tone by a style built on it. Since v0.6.4 the chip sits on the
        // frame's rail, so it takes the frame's state colours (Spec §3, status chip).
        var dot = Assert.Single(shell.Descendants(Wpf + "Ellipse"), e => e.Descendants(Wpf + "Style").Any(s => (string?)s.Attribute("BasedOn") == "{StaticResource StateDot}"));
        var colours = dot.Descendants(Wpf + "Setter").Select(s => (string?)s.Attribute("Value")).ToHashSet();
        Assert.Contains("{DynamicResource Brush.shell.recording}", colours);
        Assert.Contains("{DynamicResource Brush.shell.paused}", colours);
        Assert.Contains(dot.Descendants(Wpf + "DataTrigger"), t => (string?)t.Attribute("Binding") == "{Binding StatusTone}");
    }

    [Fact]
    public void ReviewsDiscardSaysWhyItIsOffBesideIt()
    {
        var review = Load("Review", "ReviewPane.xaml");

        var hint = Assert.Single(review.Descendants(Wpf + "TextBlock"), t => (string?)t.Attribute("Text") == "{Binding Note.DiscardHint}");
        Assert.Equal("Polite", (string?)hint.Attribute(Controls + "LiveRegion.Setting"));

        var discard = Assert.Single(Buttons(review), b => (string?)b.Attribute("Command") == "{Binding Note.DiscardCommand}");
        Assert.Equal("{Binding Note.DiscardHelp}", (string?)discard.Attribute("AutomationProperties.HelpText"));
    }

    [Fact]
    public void ThePermissionFixIsNamedForWhatItOpens()
    {
        var onboarding = Load("Onboarding", "OnboardingWindow.xaml");

        var fix = Assert.Single(Buttons(onboarding), b => (string?)b.Attribute("CommandParameter") == "{Binding FixLink}");
        Assert.Equal("{Binding FixLabel}", (string?)fix.Attribute("Content"));
        Assert.Equal("{Binding FixName}", (string?)fix.Attribute("AutomationProperties.Name"));
    }

    [Fact]
    public void TheRenderHarnessGivesTheBlockedMicrophoneItsLink()
    {
        // The harness put the deep link in FixHint, the words' field, so the render showed no fix at all.
        var harness = Text("Shell", "ShellWindow.xaml.cs");

        Assert.Contains("FixLink = \"ms-settings:privacy-microphone\"", harness, StringComparison.Ordinal);
    }

    private static IEnumerable<XElement> Buttons(XElement root) => root.Descendants(Wpf + "Button");

    private static XElement Load(string folder, string file) => XDocument.Parse(Text(folder, file)).Root!;

    private static string Text(string folder, string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "client", "ScreenTrail.UI", folder, file);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(file);
    }
}
