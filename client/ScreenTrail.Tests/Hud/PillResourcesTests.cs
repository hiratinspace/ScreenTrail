using System.Text.RegularExpressions;
using System.Xml.Linq;
using ScreenTrail.Core.Hud;

namespace ScreenTrail.Tests.Hud;

/// <summary>
/// Every style and brush the pill asks for exists, in every theme (INV-4, ST-072).
///
/// The pill looks its styles up dynamically so it can be built on a bare UI thread in the shell's quit
/// and re-dock tests. A static lookup of a key that is not there throws as the window is built; a dynamic
/// one quietly falls back to WPF's default, which on the pill is black text on a dark surface: a pill on
/// a monitor, reporting itself as showing, whose words nobody can read. The render harness checks the
/// glyph's colour and the surface, not the words, so this holds the keys instead.
/// </summary>
public sealed partial class PillResourcesTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("Tokens.Dark.g.xaml")]
    [InlineData("Tokens.Light.g.xaml")]
    [InlineData("Tokens.HighContrast.xaml")]
    public void EveryResourceThePillAsksForIsDefined(string theme)
    {
        var pill = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Hud", "HudWindow.xaml"));
        var asked = Lookup().Matches(pill).Select(m => m.Groups["key"].Value).ToHashSet(StringComparer.Ordinal);

        var defined = new HashSet<string>(StringComparer.Ordinal);
        defined.UnionWith(Keys(XDocument.Parse(pill)));
        foreach (var file in new[] { "Tokens.Common.g.xaml", "Components.xaml", "Icons.xaml", theme })
        {
            defined.UnionWith(Keys(XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Theme", file))));
        }

        // The control: the pill asks for its styles and its surface, so an empty set cannot pass.
        Assert.Contains("Hud.IconButton", asked);
        Assert.Contains("Brush.shell.bg", asked);
        Assert.Empty(asked.Except(defined));
    }

    /// <summary>
    /// Spec §5 S2 (v0.6.0): the pill is part of the frame. It draws on <c>shell.bg</c> with a
    /// <c>shell.muted</c> outline, carries no brand mark (C-H5), and colours its states with the <c>shell.*</c>
    /// state tokens, which high contrast maps to system colours. The pane tokens it used before would put
    /// pane colours on the frame, where the contrast test does not hold them.
    /// </summary>
    [Fact]
    public void ThePillIsPartOfTheFrame()
    {
        var pill = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Hud", "HudWindow.xaml"));
        var asked = Lookup().Matches(pill).Select(m => m.Groups["key"].Value).ToHashSet(StringComparer.Ordinal);

        foreach (var key in new[]
        {
            "Brush.shell.bg", "Brush.shell.text", "Brush.shell.muted", "Brush.shell.line",
            "Brush.shell.recording", "Brush.shell.paused", "Brush.shell.scope", "Brush.shell.offline",
        })
        {
            Assert.Contains(key, asked);
        }

        Assert.DoesNotContain(asked, key => key.StartsWith("Brush.state.", StringComparison.Ordinal) || key.StartsWith("Brush.bg.", StringComparison.Ordinal));

        // No brand on a status control (review finding C-H5): the ribbon cap was a wedge at the pill's end.
        Assert.DoesNotContain(asked, key => key.StartsWith("Brush.brand.", StringComparison.Ordinal));
        Assert.DoesNotContain("<Path", pill, StringComparison.Ordinal);
    }

    /// <summary>
    /// One button per action. The expanded card used to repeat Mark under the pill's own Mark, and two
    /// focusable buttons with one name read to a screen reader as the same control twice (Accessibility
    /// Insights, SiblingUniqueAndFocusable, CI on #289).
    /// </summary>
    [Fact]
    public void EachActionHasOneButton()
    {
        var pill = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Hud", "HudWindow.xaml"));
        var commands = CommandBinding().Matches(pill).Select(m => m.Groups["command"].Value).ToList();

        Assert.Contains("MarkCommand", commands);
        Assert.Equal(commands.Distinct(StringComparer.Ordinal), commands);
    }

    [GeneratedRegex(@"Command=""\{Binding\s+(?<command>\w+)\}""")]
    private static partial Regex CommandBinding();

    /// <summary>Tokens only: no colour is typed into the pill (Spec §2).</summary>
    [Fact]
    public void ThePillTypesNoColour()
    {
        var pill = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Hud", "HudWindow.xaml"));

        Assert.DoesNotMatch(TypedColour(), pill);
    }

    [GeneratedRegex(@"""#[0-9A-Fa-f]{3,8}""|Colors\.|=""(?:Red|White|Black|Gray)""")]
    private static partial Regex TypedColour();

    /// <summary>
    /// The pill is built on a bare UI thread in the shell's quit, re-dock and clock tests, where the app's
    /// resources are not loaded. A static lookup of an app resource throws there while the window is built
    /// (ST-127 named the pill's icons statically, and five Windows tests failed on Icon.Shield), so only
    /// the pill's own resources are looked up statically.
    /// </summary>
    [Fact]
    public void OnlyThePillsOwnResourcesAreLookedUpStatically()
    {
        var pill = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Hud", "HudWindow.xaml"));
        var own = Keys(XDocument.Parse(pill)).ToHashSet(StringComparer.Ordinal);
        var statics = StaticLookup().Matches(pill).Select(m => m.Groups["key"].Value).ToList();

        Assert.Contains("Hud.Icon", statics);
        Assert.DoesNotContain(statics, key => !own.Contains(key));
    }

    [GeneratedRegex(@"\{StaticResource\s+(?<key>[^}\s]+)\s*\}")]
    private static partial Regex StaticLookup();

    /// <summary>
    /// Review finding C-H6 (less the shadow, which waits for a Windows check): the pill is 40 tall with a
    /// 1 px border.strong edge, 2 px in high contrast, and so is its card's; the 32 px buttons still fit.
    /// </summary>
    [Fact]
    public void ThePillIs40TallWithAStrongEdgeThatHighContrastDoubles()
    {
        var theme = Path.Combine(AppContext.BaseDirectory, "Theme");
        double Height(string key) => double.Parse(XDocument.Load(Path.Combine(theme, "Tokens.Common.g.xaml")).Root!.Elements()
            .Single(e => (string?)e.Attribute(X + "Key") == key).Value, System.Globalization.CultureInfo.InvariantCulture);
        string Edge(string file) => XDocument.Load(Path.Combine(theme, file)).Root!.Elements()
            .Single(e => (string?)e.Attribute(X + "Key") == "Thickness.float-edge").Value;

        Assert.Equal(40, Height("Layout.hud-height"));
        Assert.Equal("1", Edge("Tokens.Light.g.xaml"));
        Assert.Equal("1", Edge("Tokens.Dark.g.xaml"));
        Assert.Equal("2", Edge("Tokens.HighContrast.xaml"));

        var pill = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Hud", "HudWindow.xaml"));
        foreach (var name in new[] { "Pill", "Card" })
        {
            var surface = pill.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);
            Assert.Equal("{DynamicResource Brush.border.strong}", (string?)surface.Attribute("BorderBrush"));
            Assert.Equal("{DynamicResource Thickness.float-edge}", (string?)surface.Attribute("BorderThickness"));
        }

        // 40 less the 2 px high contrast edge both sides and the padding above and below holds a 32 px button.
        var frame = pill.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "Pill");
        var padding = ((string)frame.Attribute("Padding")!).Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.True(40 - (2 * 2) - padding[1] - padding[3] >= Height("Layout.hud-control"), "The pill's buttons do not fit inside it.");
    }

    /// <summary>Review finding C-H9, amendment 24: Stop sits 8 from Pause, the other buttons 4 apart.</summary>
    [Fact]
    public void StopSitsFurtherFromPauseThanTheOtherButtonsSitApart()
    {
        var pill = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Hud", "HudWindow.xaml"));
        var buttons = pill.Descendants().Where(e => e.Name.LocalName == "Button" && (string?)e.Attribute("Style") == "{DynamicResource Hud.IconButton}")
            .ToDictionary(b => ((string?)b.Attribute("Command"))!, b => (string?)b.Attribute("Margin"));

        Assert.Equal("8,0,0,0", buttons["{Binding StopCommand}"]);
        Assert.Equal("4,0,0,0", buttons["{Binding PauseOrResumeCommand}"]);
        Assert.Equal("4,0,0,0", buttons["{Binding ToggleExpandedCommand}"]);
    }

    /// <summary>
    /// Review finding C-H8: the pill's buttons draw no outline in any state; hover and pressed are fills,
    /// pressed is never Iris, and disabled dims the glyph.
    /// </summary>
    [Fact]
    public void ThePillsButtonsChangeTheirFillAndNeverDrawARing()
    {
        var components = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Theme", "Components.xaml"));
        var style = components.Root!.Elements().Single(e => e.Name.LocalName == "Style" && (string?)e.Attribute(X + "Key") == "Hud.IconButton");

        Assert.DoesNotContain(style.Descendants(), e => (string?)e.Attribute("Property") == "BorderBrush" || e.Attribute("BorderBrush") is not null);
        Assert.Contains(style.Descendants(), e => (string?)e.Attribute("Property") == "BorderThickness" && (string?)e.Attribute("Value") == "0");
        var triggers = style.Descendants().Where(e => e.Name.LocalName == "Trigger").ToDictionary(t => $"{(string?)t.Attribute("Property")}={(string?)t.Attribute("Value")}", t => t.Elements().ToList());
        Assert.Contains(triggers["IsMouseOver=True"], e => (string?)e.Attribute("Value") == "{DynamicResource Brush.shell.hover}");
        Assert.Contains(triggers["IsPressed=True"], e => (string?)e.Attribute("Value") == "{DynamicResource Brush.shell.pressed}");
        Assert.Contains(triggers["IsEnabled=False"], e => (string?)e.Attribute("Property") == "Foreground" && (string?)e.Attribute("Value") == "{DynamicResource Brush.text.disabled}");
        Assert.DoesNotContain(style.Descendants(), e => ((string?)e.Attribute("Value") ?? string.Empty).Contains("shell.accent", StringComparison.Ordinal));

        // C-H7: the glyph rests in shell.muted and turns shell.text under the pointer, and the pill's button
        // glyphs take the button's colour rather than setting their own.
        Assert.Contains(style.Elements(), e => (string?)e.Attribute("Property") == "Foreground" && (string?)e.Attribute("Value") == "{DynamicResource Brush.shell.muted}");
        Assert.Contains(triggers["IsMouseOver=True"], e => (string?)e.Attribute("Property") == "Foreground" && (string?)e.Attribute("Value") == "{DynamicResource Brush.shell.text}");
        var pill = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Hud", "HudWindow.xaml"));
        var buttonGlyphs = pill.Descendants().Where(e => e.Name.LocalName == "Button").SelectMany(b => b.Descendants().Where(g => g.Name.LocalName == "Glyph")).ToList();
        Assert.Equal(4, buttonGlyphs.Count);
        Assert.All(buttonGlyphs, g => Assert.Equal("{StaticResource Hud.Glyph}", (string?)g.Attribute("Style")));
    }

    /// <summary>
    /// Review finding C-H2: a long state word trims with an ellipsis and keeps its whole words in the
    /// tooltip, so the pill stays a pill; and it is never narrower than 240.
    /// </summary>
    [Fact]
    public void TheStateWordTrimsAndThePillHasAMinimumWidth()
    {
        var pill = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Hud", "HudWindow.xaml"));
        var word = pill.Descendants().Single(e => (string?)e.Attribute("Text") == "{Binding StateWord}");
        Assert.Equal("CharacterEllipsis", (string?)word.Attribute("TextTrimming"));
        Assert.Equal("{DynamicResource Layout.hud-word-max}", (string?)word.Attribute("MaxWidth"));
        Assert.Equal("{Binding StateWordTooltip}", (string?)word.Attribute("ToolTip"));

        var frame = pill.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "Pill");
        Assert.Equal("{DynamicResource Layout.hud-min-width}", (string?)frame.Attribute("MinWidth"));
    }

    /// <summary>
    /// Review finding C-H3: the state mark is drawn (dot, ring, progress ring) or is an icon looked up by
    /// name. Every icon a state can carry has its trigger in the pill, and no state text goes into the mark.
    /// </summary>
    [Fact]
    public void EveryStateIconHasItsTriggerAndTheMarkTypesNoCharacter()
    {
        var pill = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Hud", "HudWindow.xaml"));
        var style = pill.Descendants().Single(e => e.Name.LocalName == "Style" && (string?)e.Attribute(X + "Key") == "Hud.ToneGlyph");
        var triggered = style.Descendants().Where(e => e.Name.LocalName == "DataTrigger" && (string?)e.Attribute("Binding") == "{Binding StateGlyph}")
            .Select(e => (string?)e.Attribute("Value")).ToList();

        foreach (var icon in new[] { HudGlyphs.Pause, HudGlyphs.Lock, HudGlyphs.Hide, HudGlyphs.Unknown })
        {
            Assert.Contains(icon, triggered);
        }

        var mark = pill.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "StateGlyphText");
        Assert.Null(mark.Attribute("Text"));
    }

    private static IEnumerable<string> Keys(XDocument document) =>
        document.Descendants().Select(e => e.Attribute(X + "Key")?.Value).OfType<string>();

    [GeneratedRegex(@"\{(?:Dynamic|Static)Resource\s+(?<key>[^}\s]+)\s*\}")]
    private static partial Regex Lookup();
}
