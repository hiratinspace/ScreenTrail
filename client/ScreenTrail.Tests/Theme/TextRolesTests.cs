using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>The type scale (design system 3.2, review finding A-31): one style per role, each with its line height.</summary>
public sealed class TextRolesTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("Text.Body", "Leading.body")]
    [InlineData("Text.Caption", "Leading.caption")]
    [InlineData("Text.Label", "Leading.caption")]
    [InlineData("Text.Section", "Leading.section")]
    [InlineData("Text.Title", "Leading.title")]
    [InlineData("Text.Display", "Leading.display")]
    public void EachRoleSetsItsOwnLineHeight(string role, string leading)
    {
        var style = Styles().Single(s => (string?)s.Attribute(X + "Key") == role);

        Assert.Contains(style.Elements(Wpf + "Setter"), s => (string?)s.Attribute("Property") == "LineHeight" && (string?)s.Attribute("Value") == $"{{DynamicResource {leading}}}");
    }

    [Theory]
    [InlineData("Review/Publish/PublishPane.xaml")]
    [InlineData("Review/Publish/EnvelopeView.xaml")]
    public void NoLineIsMadeSemiboldByHand(string file)
    {
        // Nine lines were Text.Body with a local semibold, and they drifted (one with Size.sm, one without).
        var view = XDocument.Load(Path.Combine(Ui(), file));

        Assert.DoesNotContain(view.Descendants(Wpf + "TextBlock"), t => t.Attribute("FontWeight") is not null && ((string?)t.Attribute("Style"))?.Contains("Text.", StringComparison.Ordinal) == true);
    }

    private static IEnumerable<XElement> Styles() =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Theme", "Components.xaml")).Root!.Elements(Wpf + "Style");

    private static string Ui()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "client", "ScreenTrail.UI")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("client"), "client", "ScreenTrail.UI");
    }
}
