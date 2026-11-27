using System.Xml.Linq;

namespace ScreenTrail.Tests.Review;

/// <summary>
/// ST-136, the Review pane's envelope and the receipt (premium review 2026-11-26, A-15 to A-17, A-20,
/// E-27, A-23 to A-25), read from the XAML.
/// </summary>
public sealed class EnvelopeLayoutTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string[] CaptionStyles = ["{StaticResource Text.Caption}", "{StaticResource Text.Muted}", "{StaticResource Text.Error}"];

    /// <summary>
    /// Review finding A-15: a screen title is 20/28, not the 24 that outweighed the note, and the count
    /// beside "All sessions" is a Caption 8 from it, sitting on its baseline rather than nudged up.
    /// </summary>
    [Fact]
    public void AScreenTitleIs20On28AndTheCountBesideItIsACaption()
    {
        var tokens = Load("Theme", "Tokens.Common.g.xaml");
        Assert.Equal("20", Token(tokens, "Size.xl"));
        Assert.Equal("28", Token(tokens, "Leading.title"));

        var history = Load("History", "HistoryPane.xaml");
        var count = history.Descendants(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == "History.Count");
        Assert.Equal("{StaticResource Text.Caption}", (string?)count.Attribute("BasedOn"));

        var beside = history.Descendants(Wpf + "TextBlock").Single(t => (string?)t.Attribute("Style") == "{StaticResource History.Count}");
        Assert.Equal("8,0,0,0", (string?)beside.Attribute("Margin"));
        Assert.Equal("Bottom", (string?)beside.Attribute("VerticalAlignment"));
    }

    /// <summary>
    /// Review finding A-16: the title row is the title, Discard and Publish, nothing else, so a reason never
    /// takes the subject's width; every reason is on one Caption line under the buttons that keeps its
    /// height when empty, Publish's first, so the envelope does not jump.
    /// </summary>
    [Fact]
    public void TheReasonsAreOneLineUnderTheButtonsThatKeepsItsHeight()
    {
        var review = Load("Review", "ReviewPane.xaml");
        var card = review.Descendants(Wpf + "Border").Single(b => (string?)b.Attribute(X + "Name") == "EnvelopeCard");
        var parts = card.Element(Wpf + "StackPanel")!.Elements().ToList();

        var row = parts[0];
        Assert.Equal("Grid", row.Name.LocalName);
        Assert.Equal("0,0,0,4", (string?)row.Attribute("Margin"));
        Assert.Equal(3, row.Descendants(Wpf + "ColumnDefinition").Count());
        Assert.Single(row.Elements(Wpf + "TextBlock"));
        Assert.Equal("{Binding Title}", (string?)row.Element(Wpf + "TextBlock")!.Attribute("Text"));

        var reasons = parts[1];
        Assert.Equal("Reasons", (string?)reasons.Attribute(X + "Name"));
        Assert.Equal("{DynamicResource Leading.caption}", (string?)reasons.Attribute("MinHeight"));
        Assert.Equal("Right", (string?)reasons.Attribute("HorizontalAlignment"));
        var said = reasons.Descendants(Wpf + "TextBlock").Select(t => (string?)t.Attribute("Text")).ToList();
        Assert.True(said.IndexOf("{Binding Publish.BlockedBecause}") < said.IndexOf("{Binding Note.DiscardHint}"), "Publish's reason comes first");
        Assert.Contains("{Binding Note.DiscardError}", said);
        Assert.All(reasons.Descendants(Wpf + "TextBlock"), t => Assert.Contains((string?)t.Attribute("Style"), CaptionStyles));

        var envelope = card.Descendants().Single(e => e.Name.LocalName == "EnvelopeView");
        Assert.Equal("0,8,0,0", (string?)envelope.Attribute("Margin"));
    }

    /// <summary>
    /// Review finding A-17: the bound ticket is a value, its ID in Body strong and the rest in Body 8 after,
    /// with no box, background or edge, so it does not look like the Time field two lines down. Change is a
    /// link 12 after it, its key cap 4 after that.
    /// </summary>
    [Fact]
    public void TheBoundTicketIsWordsNotABox()
    {
        var envelope = Load("Review", "Publish", "EnvelopeView.xaml");
        var value = envelope.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "TicketValue");
        Assert.Equal("StackPanel", value.Name.LocalName);
        Assert.Null(value.Attribute("Background"));
        Assert.Null(value.Attribute("BorderBrush"));
        Assert.Equal("0,0,12,0", (string?)value.Attribute("Margin"));
        Assert.Equal("{Binding ToValue}", (string?)value.Attribute("AutomationProperties.Name"));
        Assert.DoesNotContain(value.Ancestors(Wpf + "Border"), b => (string?)b.Attribute("BorderBrush") == "{DynamicResource Brush.border.control}");

        var words = value.Elements(Wpf + "TextBlock").ToList();
        Assert.Equal("{StaticResource Text.BodyStrong}", (string?)words[0].Attribute("Style"));
        Assert.Equal("{StaticResource Text.Body}", (string?)words[1].Attribute("Style"));
        Assert.Equal("8,0,0,0", (string?)words[1].Attribute("Margin"));

        var change = envelope.Descendants(Wpf + "Button").Single(b => (string?)b.Attribute(X + "Name") == "ChangeButton");
        Assert.Equal("{StaticResource Button.Link}", (string?)change.Attribute("Style"));
        var cap = change.ElementsAfterSelf(Wpf + "Border").First();
        Assert.Equal("4,0,0,0", (string?)cap.Attribute("Margin"));
    }

    internal static XElement Load(params string[] path) => XDocument.Load(Path.Combine([Ui(), .. path])).Root!;

    private static string Token(XElement dictionary, string key) =>
        dictionary.Elements().Single(e => (string?)e.Attribute(X + "Key") == key).Value;

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
