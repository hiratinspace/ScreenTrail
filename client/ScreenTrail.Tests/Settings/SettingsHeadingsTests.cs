using System.Xml.Linq;
using ScreenTrail.Tests.Theme;

namespace ScreenTrail.Tests.Settings;

/// <summary>
/// A nav item opens a heading with its own word (review finding E-17): "Privacy" opened "Privacy and
/// redaction", which read as a different page.
/// </summary>
public sealed class SettingsHeadingsTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void EachSectionsHeadingIsItsNavWord()
    {
        var pane = EmptyStateTests.View("Settings", "SettingsPane.xaml");
        var sections = pane.Descendants(Wpf + "StackPanel").Where(p => p.Attribute("Tag") is not null).ToList();

        Assert.Equal(["Activation", "Capture", "Privacy", "Integrations", "Appearance", "Updates"], sections.Select(s => (string)s.Attribute("Tag")!));
        foreach (var section in sections)
        {
            var heading = section.Elements(Wpf + "TextBlock").First();
            Assert.Equal((string)section.Attribute("Tag")!, (string?)heading.Attribute("Text"));
        }
    }
}
