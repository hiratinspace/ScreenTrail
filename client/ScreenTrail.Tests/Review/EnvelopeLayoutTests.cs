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
