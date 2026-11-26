using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>
/// One empty state, with no card and no Iris art (design system issue 1 and 2.2, review finding A-08).
/// </summary>
public sealed class EmptyStateTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void TheDiscIsTheInfoGreyAndTheGlyphIsNotIris()
    {
        var components = ChoiceStatesTests.Components();
        Assert.Equal("{DynamicResource Brush.bg.info}", Setter(components, "EmptyState.Disc", "Background"));
        Assert.Equal("{DynamicResource Radius.pill}", Setter(components, "EmptyState.Disc", "CornerRadius"));
        Assert.Equal("{DynamicResource Brush.text.secondary}", Setter(components, "EmptyState.Icon", "Foreground"));
        Assert.Equal("0,48", Setter(components, "EmptyState", "Margin"));
        Assert.Equal("{DynamicResource Layout.empty-max-width}", Setter(components, "EmptyState", "MaxWidth"));
    }

    [Theory]
    [InlineData("Shell", "ShellWindow.xaml")]
    [InlineData("History", "HistoryPane.xaml")]
    [InlineData("History", "InboxRailView.xaml")]
    [InlineData("Review", "NoteEditorView.xaml")]
    public void NoEmptyStateIsBoxedInACardOrDrawnInIris(string folder, string file)
    {
        var view = XDocument.Load(Path.Combine(Root(), "client", "ScreenTrail.UI", folder, file)).Root!;
        var glyphs = view.Descendants().Where(e => ((string?)e.Attribute("Style"))?.StartsWith("{StaticResource EmptyState.Icon", StringComparison.Ordinal) == true
            || e.Descendants(Wpf + "Style").Any(s => ((string?)s.Attribute("BasedOn"))?.StartsWith("{StaticResource EmptyState.Icon", StringComparison.Ordinal) == true)).ToList();

        Assert.NotEmpty(glyphs);
        Assert.All(glyphs, g => Assert.NotEqual("{DynamicResource Brush.accent.primary}", (string?)g.Attribute("Foreground")));
        Assert.DoesNotContain(view.Descendants(), e => ((string?)e.Attribute("Style")) == "{StaticResource Card}" && e.Descendants().Any(d => ((string?)d.Attribute("Style"))?.StartsWith("{StaticResource EmptyState", StringComparison.Ordinal) == true));
        Assert.DoesNotContain(view.Descendants(), e => (string?)e.Attribute("CornerRadius") == "28");
    }

    private static string? Setter(XElement root, string key, string property) =>
        (string?)root.Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == key)
            .Elements(Wpf + "Setter").Single(s => (string?)s.Attribute("Property") == property).Attribute("Value");

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(directory!.FullName, "client", "ScreenTrail.UI")))
        {
            directory = directory.Parent;
        }

        return directory.FullName;
    }
}
