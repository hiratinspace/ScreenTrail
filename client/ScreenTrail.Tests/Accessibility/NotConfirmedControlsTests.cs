using System.Xml.Linq;

namespace ScreenTrail.Tests.Accessibility;

/// <summary>
/// History's "Not confirmed" badge and its Mark as published button as the XAML draws them (ST-093,
/// contract v10; Spec §5 S4, §7), read off the files so it holds on the Mac and on every commit. What
/// the command does is the view model's and is tested on Windows; this holds that the badge is drawn
/// beside the status from the tokens, is named, and is not a tab stop, and that the button exists, is
/// bound, is named and is a standard button (so it has the standard hit target).
/// </summary>
public sealed class NotConfirmedControlsTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Theme = "clr-namespace:ScreenTrail.UI.Theme";

    [Fact]
    public void TheBadgeSitsBesideTheStatusBadgeAndIsNamed()
    {
        var history = Load("History", "HistoryPane.xaml");

        var column = Assert.Single(history.Descendants(Wpf + "DataGridTemplateColumn"), c => (string?)c.Attribute("Header") == "Status");
        var badges = column.Descendants(Wpf + "ContentControl").ToList();
        Assert.Equal(2, badges.Count);

        // The status first, as it always was, so sorting and the words do not change. Since v0.6.9 it is
        // the rail's badge: the word with its glyph beside it, in the state's hue (never colour alone).
        Assert.Equal("{StaticResource Badge}", (string?)badges[0].Attribute("Style"));
        Assert.Equal("{Binding StatusTone}", (string?)badges[0].Attribute(Theme + "Badge.Tone"));
        Assert.Contains(badges[0].Descendants(Wpf + "TextBlock"), t => (string?)t.Attribute("Text") == "{Binding Status}");
        Assert.Equal("{Binding StatusGlyph}", (string?)badges[0].Attribute(Theme + "Badge.Glyph"));
        Assert.Equal("Status", (string?)column.Attribute("SortMemberPath"));

        var notConfirmed = badges[1];
        Assert.Equal("{StaticResource Badge}", (string?)notConfirmed.Attribute("Style"));
        Assert.Equal("Warning", (string?)notConfirmed.Attribute(Theme + "Badge.Tone"));
        Assert.Contains(notConfirmed.Descendants(Wpf + "TextBlock"), t => (string?)t.Attribute("Text") == "{Binding NotConfirmedLabel}");
        Assert.Contains("NotConfirmedGlyph", (string?)notConfirmed.Attribute(Theme + "Badge.Glyph"), StringComparison.Ordinal);
        Assert.Equal("{Binding NotConfirmedVisibility}", (string?)notConfirmed.Attribute("Visibility"));
        Assert.False(string.IsNullOrWhiteSpace((string?)notConfirmed.Attribute("AutomationProperties.Name")));

        // Label in name (WCAG 2.5.3): the badge's name holds the words it shows.
        Assert.Contains(Core.History.SessionHistory.NotConfirmedLabel, (string?)notConfirmed.Attribute("AutomationProperties.Name"), StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace((string?)notConfirmed.Attribute("ToolTip")));

        // The row is what a screen reader lands on, and its name says it.
        var row = Assert.Single(history.Descendants(Wpf + "Style"), s => (string?)s.Attribute("TargetType") == "DataGridRow");
        Assert.Contains(row.Elements(Wpf + "Setter"), s => (string?)s.Attribute("Property") == "AutomationProperties.Name" && (string?)s.Attribute("Value") == "{Binding Spoken}");
    }

    [Fact]
    public void TheWarningToneIsItsOwnSoftEdgeDistinctFromSuccessAndDanger()
    {
        // Design system 10.5 (review findings A-02, B-24): one Badge, the tone drawing its tint and a soft
        // border.* edge, never the full-strength state colour.
        var components = Load("Theme", "Components.xaml");
        var badge = Style(components, "Badge");
        var tones = badge.Descendants(Wpf + "Trigger").Where(t => (string?)t.Attribute("Property") == "theme:Badge.Tone")
            .ToDictionary(t => (string)t.Attribute("Value")!, t => t.Elements(Wpf + "Setter").ToDictionary(s => (string)s.Attribute("TargetName")! + "." + (string)s.Attribute("Property")!, s => (string)s.Attribute("Value")!));

        Assert.Equal("{DynamicResource Brush.bg.warning}", tones["Warning"]["Chrome.Background"]);
        Assert.Equal("{DynamicResource Brush.border.warning}", tones["Warning"]["Chrome.BorderBrush"]);
        Assert.Equal("{DynamicResource Brush.border.success}", tones["Success"]["Chrome.BorderBrush"]);
        Assert.Equal("{DynamicResource Brush.border.danger}", tones["Danger"]["Chrome.BorderBrush"]);
        Assert.DoesNotContain(tones.Values.SelectMany(t => t.Values), v => v.Contains("state.published", StringComparison.Ordinal) && v.Contains("Border", StringComparison.Ordinal));
        Assert.Equal("{DynamicResource Brush.text.primary}", Setters(components, "Badge")["Foreground"]);
        Assert.Equal("{DynamicResource Radius.pill}", (string?)badge.Descendants(Wpf + "Border").First().Attribute("CornerRadius"));
        Assert.DoesNotContain(badge.Descendants().Attributes(), a => a.Value.StartsWith('#'));

        // Not focusable, so a badge adds no tab stop to a row.
        Assert.Equal("False", Setters(components, "Badge")["Focusable"]);

        // The styles it replaced are gone, so one state is drawn one way on every screen.
        foreach (var retired in new[] { "Badge.Suggested", "Badge.Confidence", "Badge.Published", "Badge.Unconfirmed", "Badge.State", "Badge.StateGlyph", "Rail.Badge", "Rail.BadgeGlyph" })
        {
            Assert.DoesNotContain(components.Descendants(Wpf + "Style"), s => (string?)s.Attribute(X + "Key") == retired);
        }
    }

    [Fact]
    public void MarkAsPublishedIsAStandardNamedButtonBoundToItsCommand()
    {
        var history = Load("History", "HistoryPane.xaml");

        var mark = Assert.Single(history.Descendants(Wpf + "Button"), b => (string?)b.Attribute("Command") == "{Binding MarkPublishedCommand}");
        Assert.Equal("_Mark as published", (string?)mark.Attribute("Content"));
        Assert.Equal(Core.Shell.MarkAsPublished.Action, ((string?)mark.Attribute("Content"))?.Replace("_", string.Empty, StringComparison.Ordinal));
        Assert.Equal("{StaticResource Button.Secondary}", (string?)mark.Attribute("Style"));
        Assert.False(string.IsNullOrWhiteSpace((string?)mark.Attribute("AutomationProperties.Name")));

        // WCAG 2.5.3, label in name: what the button shows is in what it is called, word for word, so
        // someone who says the words they see reaches it. The Windows audit holds this for every screen.
        Assert.Contains("Mark as published", (string?)mark.Attribute("AutomationProperties.Name"), StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace((string?)mark.Attribute("AutomationProperties.HelpText")));
        Assert.NotEqual("False", (string?)mark.Attribute("IsTabStop"));

        // No size of its own: the height, and so the hit target, is the button style's.
        Assert.Null(mark.Attribute("Height"));
        Assert.Null(mark.Attribute("MinHeight"));

        // Not the danger button, and beside it rather than in its place.
        var bulk = Assert.Single(history.Descendants(Wpf + "Button"), b => (string?)b.Attribute("Command") == "{Binding BulkDiscardCommand}");
        Assert.Same(mark.Parent, bulk.Parent);
        Assert.True(mark.IsBefore(bulk));
    }

    [Fact]
    public void TheNewCopyIsPlain()
    {
        var history = Load("History", "HistoryPane.xaml");

        Assert.DoesNotContain(history.DescendantsAndSelf().Attributes(), a => a.Value.Contains('\u2014', StringComparison.Ordinal));
        Assert.DoesNotContain(history.Descendants().Attributes(), a => a.Value.StartsWith('#'));
    }

    private static XElement Style(XElement root, string key) =>
        Assert.Single(root.Descendants(Wpf + "Style"), s => (string?)s.Attribute(X + "Key") == key);

    private static Dictionary<string, string> Setters(XElement root, string key) =>
        Style(root, key).Elements(Wpf + "Setter")
            .Where(s => s.Attribute("Value") is not null)
            .ToDictionary(s => (string)s.Attribute("Property")!, s => (string)s.Attribute("Value")!);

    private static XElement Load(string folder, string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "client", "ScreenTrail.UI", folder, file);
            if (File.Exists(candidate))
            {
                return XDocument.Load(candidate).Root!;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(file);
    }
}
