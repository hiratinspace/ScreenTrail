using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>
/// An unsaved change is marked without Iris (design system 10.5 and issue 8, review finding B-25): on a
/// pending page the Save bar's Primary button is the one Iris call.
/// </summary>
public sealed class ChangedMarkTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void TheChangedBadgeIsNeutralWithAQuietDot()
    {
        var changed = ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == "Badge.Changed");

        Assert.Equal("{StaticResource Badge}", (string?)changed.Attribute("BasedOn"));
        Assert.DoesNotContain(changed.Descendants().Attributes(), a => a.Value.Contains("accent", StringComparison.Ordinal) || a.Value.Contains("bg.selected", StringComparison.Ordinal));
        var dot = changed.Descendants(Wpf + "Ellipse").Single();
        Assert.Equal("{DynamicResource Brush.text.secondary}", (string?)dot.Attribute("Fill"));
        Assert.Equal("0,0,4,0", (string?)dot.Attribute("Margin"));
    }

    [Fact]
    public void TheNavCountIsThePlainBadgeWithTheNumberAlone()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(directory!.FullName, "client", "ScreenTrail.UI")))
        {
            directory = directory.Parent;
        }

        var settings = XDocument.Load(Path.Combine(directory.FullName, "client", "ScreenTrail.UI", "Settings", "SettingsPane.xaml")).Root!;
        var count = settings.Descendants(Wpf + "ContentControl").Single(c => (string?)c.Attribute("Content") == "{Binding PendingText}");

        Assert.Equal("{StaticResource Badge}", (string?)count.Attribute("Style"));
    }
}
