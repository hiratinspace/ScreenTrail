using System.Text.Json;
using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>
/// Spec §7: when Windows high contrast is on, every token maps to the system palette. The dictionary is
/// written by hand while the light and dark ones are generated from shared/design/tokens.json, so a token
/// added to tokens.json arrives in two of the three themes and is a missing resource in the third, at run
/// time, in the theme a technician who needs it has least chance of reporting (2026-10-12 review, T-377).
/// </summary>
public sealed class HighContrastTokensTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void HighContrastDefinesEveryTokenTheThemesDo()
    {
        var light = Keys("Tokens.Light.g.xaml");
        var dark = Keys("Tokens.Dark.g.xaml");
        var highContrast = Keys("Tokens.HighContrast.xaml");

        Assert.NotEmpty(light);
        Assert.Equal(light, dark);
        Assert.Empty(light.Except(highContrast));
        Assert.Empty(highContrast.Except(light));
    }

    [Fact]
    public void TheCommonDictionaryIsNotRedefinedByAnyTheme()
    {
        // Sizes and weights live in the common dictionary and are merged under every theme. A theme that
        // redefined one would win or lose by merge order, which nobody would notice until a pill changed size.
        var common = Keys("Tokens.Common.g.xaml");

        Assert.NotEmpty(common);
        foreach (var theme in new[] { "Tokens.Light.g.xaml", "Tokens.Dark.g.xaml", "Tokens.HighContrast.xaml" })
        {
            Assert.Empty(common.Intersect(Keys(theme)));
        }
    }

    [Fact]
    public void EachTokenMapsToTheSystemColourTokensJsonNames()
    {
        // Spec v0.6.0: tokens.json names each colour's system colour (`hc`), so the hand-written dictionary
        // and the token file say the same thing. A token mapped here to a different system colour from the
        // one the file names is a decision made in one place and not the other.
        using var tokens = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Design", "tokens.json")));
        var path = Path.Combine(AppContext.BaseDirectory, "Theme", "Tokens.HighContrast.xaml");
        // An x:Static object element, not "{x:Static ...}" as a Color's text: XAML does not read markup in
        // element text, so every Token.* lookup in high contrast threw "Token is not valid" (the title bar
        // asked for Token.bg.base, and every high contrast screen failed to open).
        var root = XDocument.Load(path).Root!;
        Assert.DoesNotContain(root.Elements(), e => e.Name.LocalName == "Color");
        var colours = root.Elements()
            .Where(e => e.Name == X + "Static")
            .ToDictionary(e => e.Attribute(X + "Key")!.Value, e => e.Attribute("Member")!.Value, StringComparer.Ordinal);

        foreach (var colour in tokens.RootElement.GetProperty("color").EnumerateObject())
        {
            var systemColour = colour.Value.GetProperty("hc").GetString();
            Assert.Equal($"SystemColors.{systemColour}Color", colours[$"Token.{colour.Name}"]);
        }
    }

    [Fact]
    public void NoTokenTakesTheWindowBorderColours()
    {
        // ActiveBorder and InactiveBorder are the colours of a window's sizing border, not of content. Night
        // sky makes ActiveBorder yellow (#FFFF00), so every card outline and divider in the app was drawn
        // yellow there (v0.6.12). Content draws only the content pairs: Window, WindowText, GrayText,
        // Highlight and HighlightText, HotTrack, Control and ControlText, and WindowFrame for edges.
        using var tokens = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Design", "tokens.json")));
        foreach (var colour in tokens.RootElement.GetProperty("color").EnumerateObject())
        {
            var systemColour = colour.Value.GetProperty("hc").GetString();
            Assert.DoesNotContain("Border", systemColour, StringComparison.Ordinal);
        }

        Assert.Equal("GrayText", tokens.RootElement.GetProperty("color").GetProperty("border.subtle").GetProperty("hc").GetString());
    }

    private static SortedSet<string> Keys(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Theme", file);
        var document = XDocument.Load(path);
        return new SortedSet<string>(
            document.Root!.Elements().Select(e => e.Attribute(X + "Key")?.Value).Where(k => k is not null)!,
            StringComparer.Ordinal);
    }
}
