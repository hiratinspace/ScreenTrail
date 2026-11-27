using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ScreenTrail.Tests.Accessibility;

/// <summary>
/// One way to show a shortcut (design system 10.1 and 10.5; review findings A-14, B-08, E-22): inside a button,
/// Text.KeyHint 8 after the label in the label's colour; beside a link or toggle, a KeyCap. Never typed into a
/// button's Content with spaces, and never part of the accessible name.
/// </summary>
public sealed partial class ShortcutHintTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void NoControlTypesItsShortcutIntoItsContent()
    {
        var typed = Views()
            .SelectMany(v => v.Root.Descendants().Attributes("Content").Where(a => SpacedKey().IsMatch(a.Value)).Select(a => $"{v.Name}: {a.Value}"))
            .ToList();

        Assert.Empty(typed);
    }

    [Fact]
    public void AHintInsideAButtonIsEightAfterTheLabelInTheLabelsColour()
    {
        var hints = Views()
            .SelectMany(v => v.Root.Descendants(Wpf + "TextBlock")
                .Where(t => (string?)t.Attribute("Style") == "{StaticResource Text.KeyHint}" && t.Ancestors(Wpf + "Button").Any())
                .Select(t => (v.Name, Hint: t)))
            .ToList();

        Assert.NotEmpty(hints);
        Assert.All(hints, h =>
        {
            Assert.True((string?)h.Hint.Attribute("Margin") == "8,0,0,0", $"{h.Name}: {h.Hint.Attribute("Text")} is not 8 after its label");
            Assert.True(h.Hint.Attribute("Foreground") is null, $"{h.Name}: {h.Hint.Attribute("Text")} sets its own colour");
        });
    }

    [Fact]
    public void AButtonWithAHintIsNamedWithoutItAndOffersItAsItsAcceleratorKey()
    {
        var buttons = Views()
            .SelectMany(v => v.Root.Descendants().Where(e => e.Name.LocalName is "Button" or "ToggleButton")
                .Where(b => b.Descendants(Wpf + "TextBlock").Any(t => (string?)t.Attribute("Style") == "{StaticResource Text.KeyHint}"))
                .Select(b => (v.Name, Button: b)))
            .ToList();

        Assert.All(buttons, b =>
        {
            var key = (string)b.Button.Descendants(Wpf + "TextBlock").First(t => (string?)t.Attribute("Style") == "{StaticResource Text.KeyHint}").Attribute("Text")!;
            var name = (string?)b.Button.Attribute("AutomationProperties.Name");
            Assert.False(string.IsNullOrEmpty(name), $"{b.Name}: the {key} button has no name of its own, so its hint is read as part of it");
            Assert.False(Regex.IsMatch(name!, $@"[\s,]{Regex.Escape(key)}$"), $"{b.Name}: \"{name}\" ends with its key");
            Assert.Equal(key, (string?)b.Button.Attribute("AutomationProperties.AcceleratorKey"));
        });
    }

    [Fact]
    public void AKeyCapIsTwentyHighWithTheSmallRadius()
    {
        var components = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Theme", "Components.xaml")).Root!;
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var cap = components.Elements(Wpf + "Style").Single(s => (string?)s.Attribute(x + "Key") == "KeyCap");
        string? Setter(string property) => (string?)cap.Elements(Wpf + "Setter").SingleOrDefault(s => (string?)s.Attribute("Property") == property)?.Attribute("Value");

        Assert.Equal("{DynamicResource Layout.keycap-height}", Setter("MinHeight"));
        Assert.Equal("{DynamicResource Radius.sm}", Setter("CornerRadius"));
        Assert.Equal("4,0", Setter("Padding"));
    }

    /// <summary>A label, two or more spaces, then a key: "Save   Ctrl+S", "Close  Esc", "OCR text  T".</summary>
    [GeneratedRegex(@"\S {2,}(Ctrl|Alt|Shift|Esc|Enter|Del|[A-Z0-9])\S*$")]
    private static partial Regex SpacedKey();

    private static IEnumerable<(string Name, XElement Root)> Views()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "client", "ScreenTrail.UI")))
        {
            directory = directory.Parent;
        }

        var ui = Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("client"), "client", "ScreenTrail.UI");
        return Directory.EnumerateFiles(ui, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (Path.GetRelativePath(ui, f), XDocument.Load(f).Root!));
    }
}
