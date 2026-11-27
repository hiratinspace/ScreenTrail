using System.Xml.Linq;
using ScreenTrail.Tests.Theme;

namespace ScreenTrail.Tests.Settings;

/// <summary>
/// The Settings page's surfaces and rhythm, from the premium review (ST-140): what each tile, heading and
/// list row is drawn with.
/// </summary>
public sealed class SettingsPolishTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XElement Components() => EmptyStateTests.View("Theme", "Components.xaml");

    private static XElement Style(XElement file, string key) =>
        file.Descendants(Wpf + "Style").Single(s => (string?)s.Attribute(Xaml + "Key") == key);

    private static string? Setter(XElement style, string property) =>
        (string?)style.Elements(Wpf + "Setter").SingleOrDefault(s => (string?)s.Attribute("Property") == property)?.Attribute("Value");

    /// <summary>Review finding B-40: a tile is a card, radius 8; at 6 the banners inside it were rounder than it.</summary>
    [Fact]
    public void ATileHasTheCardsRadius()
    {
        Assert.Equal("{DynamicResource Radius.card}", Setter(Style(Components(), "Settings.Tile"), "CornerRadius"));
    }

    /// <summary>Review finding B-41: "Your data" is outlined like its neighbours; the danger is its Delete button's.</summary>
    [Fact]
    public void YourDataIsATileLikeTheOthers()
    {
        var privacy = EmptyStateTests.View("Settings/Privacy", "PrivacyPane.xaml");
        var tile = privacy.Descendants(Wpf + "Border").Single(b => (string?)b.Attribute(Xaml + "Name") == "YourData");
        Assert.Equal("{StaticResource Settings.Tile}", (string?)tile.Attribute("Style"));
        Assert.Null(tile.Attribute("BorderBrush"));
        Assert.Null(tile.Attribute("Margin"));
        Assert.Contains(tile.Descendants(Wpf + "Button"), b => (string?)b.Attribute("Style") == "{StaticResource Button.Danger}");
    }

    /// <summary>
    /// Review finding B-45: a section heading has 32 above and 12 below, every one alike, and the first is
    /// level with the nav (whose first item is 16 down). Activation's heading had its own 16 and 12.
    /// </summary>
    [Fact]
    public void SectionHeadingsShareOneRhythm()
    {
        var pane = EmptyStateTests.View("Settings", "SettingsPane.xaml");
        Assert.Equal("0,32,0,12", Setter(Style(pane, "Settings.Group"), "Margin"));
        var headings = pane.Descendants(Wpf + "TextBlock").Where(t => (string?)t.Attribute("Style") == "{StaticResource Settings.Group}").ToList();
        Assert.Equal("0,16,0,12", (string?)headings[0].Attribute("Margin"));
        Assert.All(headings.Skip(1), h => Assert.Null(h.Attribute("Margin")));
    }

    /// <summary>Review finding B-49: a hint under a choice starts at its label, 18 for the mark and 8 for the gap.</summary>
    [Fact]
    public void AHintUnderAChoiceStartsAtItsLabel()
    {
        var hint = Style(Components(), "Choice.Hint");
        Assert.Equal("{StaticResource Inset.choice-hint}", Setter(hint, "Margin"));
        var appearance = EmptyStateTests.View("Settings/Appearance", "AppearancePane.xaml");
        var hints = appearance.Descendants(Wpf + "TextBlock").Where(t => (string?)t.Attribute("Style") == "{StaticResource Choice.Hint}").ToList();
        Assert.Equal(2, hints.Count);
        Assert.DoesNotContain(appearance.Descendants(Wpf + "TextBlock"), t => ((string?)t.Attribute("Margin"))?.StartsWith("24,", StringComparison.Ordinal) == true);
    }

    /// <summary>
    /// Review finding B-43: a connection check's answer leads with a 16 px glyph in its state's colour (a
    /// check in state.published, a warning in state.danger), not a "✓" or "✗" in body colour, and wraps at
    /// the tile's width rather than a literal 520.
    /// </summary>
    [Fact]
    public void ACheckResultLeadsWithAGlyphInItsStatesColour()
    {
        XNamespace controls = "clr-namespace:ScreenTrail.UI.Controls";
        var pane = EmptyStateTests.View("Settings/Integrations", "IntegrationsPane.xaml");
        var line = pane.Descendants(Wpf + "Grid").Single(g => (string?)g.Attribute(Xaml + "Name") == "CheckLine");
        var glyph = line.Descendants(controls + "Glyph").Single();
        var style = glyph.Descendants(Wpf + "Style").Single();
        Assert.Equal("{StaticResource Icon}", (string?)style.Attribute("BasedOn"));
        Assert.Equal("{DynamicResource Brush.state.danger}", Setter(style, "Foreground"));
        var passed = style.Descendants(Wpf + "DataTrigger").Single();
        Assert.Equal("{Binding CheckPassed}", (string?)passed.Attribute("Binding"));
        Assert.Contains(passed.Elements(Wpf + "Setter"), s => (string?)s.Attribute("Value") == "{DynamicResource Brush.state.published}");
        Assert.DoesNotContain(line.Descendants(Wpf + "TextBlock"), t => t.Attribute("MaxWidth") is not null);
        Assert.DoesNotContain(pane.Descendants(), e => (string?)e.Attribute("Text") == "{Binding CheckMark}");
    }

    /// <summary>
    /// Review finding E-15 (with B-42's alignment): the mapping table names its columns, says who files the
    /// article, and lines its rows up with the header through shared column sizes.
    /// </summary>
    [Fact]
    public void TheMappingTableNamesItsColumnsAndLinesThemUp()
    {
        var pane = EmptyStateTests.View("Settings/Integrations", "IntegrationsPane.xaml");
        var table = pane.Descendants(Wpf + "StackPanel").Single(p => (string?)p.Attribute(Xaml + "Name") == "MappingTable");
        Assert.Equal("True", (string?)table.Attribute("Grid.IsSharedSizeScope"));
        Assert.Contains(table.Elements(Wpf + "TextBlock"), t => ((string?)t.Attribute("Text"))?.StartsWith("ScreenTrail files each KB article", StringComparison.Ordinal) == true);
        Assert.Contains(table.Elements(Wpf + "TextBlock"), t => (string?)t.Attribute("Text") == "No companies mapped yet. You are asked the first time you publish an article for a company.");

        var header = table.Elements(Wpf + "Grid").Single(g => (string?)g.Attribute(Xaml + "Name") == "MappingHeader");
        Assert.Equal(["In ConnectWise", "In Hudu"], header.Elements(Wpf + "TextBlock").Select(t => (string?)t.Attribute("Text")));
        var row = table.Descendants(Wpf + "DataTemplate").First().Elements(Wpf + "Grid").Single();
        foreach (var grid in new[] { header, row })
        {
            var groups = grid.Descendants(Wpf + "ColumnDefinition").Select(c => (string?)c.Attribute("SharedSizeGroup")).ToList();
            Assert.Equal([null, null, "Badge", "Action"], groups);
        }

        Assert.Equal("{DynamicResource Layout.row-height}", (string?)row.Attribute("MinHeight"));
    }

    /// <summary>
    /// Review finding B-42: the excluded apps and the custom patterns are list rows, 36 high under a rule,
    /// the value at the left and its Remove at the right, not a word with a Ghost button after it.
    /// </summary>
    [Theory]
    [InlineData("ExcludedList")]
    [InlineData("PatternList")]
    public void AListInATileIsRowsWithRemoveAtTheRight(string name)
    {
        XNamespace controls = "clr-namespace:ScreenTrail.UI.Controls";
        var row = Style(Components(), "Settings.ListRow");
        Assert.Equal("{DynamicResource Layout.row-height}", Setter(row, "MinHeight"));
        Assert.Equal("0,1,0,0", Setter(row, "BorderThickness"));

        var privacy = EmptyStateTests.View("Settings/Privacy", "PrivacyPane.xaml");
        var list = privacy.Descendants(controls + "Repeater").Single(r => (string?)r.Attribute(Xaml + "Name") == name);
        var border = list.Descendants(Wpf + "DataTemplate").Single().Elements(Wpf + "Border").Single();
        Assert.Equal("{StaticResource Settings.ListRow}", (string?)border.Attribute("Style"));
        var grid = border.Elements(Wpf + "Grid").Single();
        Assert.Equal(["*", "Auto"], grid.Descendants(Wpf + "ColumnDefinition").Select(c => (string?)c.Attribute("Width")));
        Assert.Equal("1", (string?)grid.Elements(Wpf + "Button").Single().Attribute("Grid.Column"));
    }

    /// <summary>
    /// Review finding B-48: what leaves this PC is five steps, each a 16 px glyph and a line of Body, then
    /// the promise in Body strong; it was numbered run-on text.
    /// </summary>
    [Fact]
    public void WhatLeavesThisPcIsFiveStepsAndAPromise()
    {
        XNamespace controls = "clr-namespace:ScreenTrail.UI.Controls";
        var privacy = EmptyStateTests.View("Settings/Privacy", "PrivacyPane.xaml");
        var flow = privacy.Descendants(Wpf + "StackPanel").Single(p => (string?)p.Attribute(Xaml + "Name") == "DataFlow");
        var rows = flow.Elements(Wpf + "Grid").ToList();
        Assert.Equal(5, rows.Count);
        Assert.Equal(
            ["{DynamicResource Icon.Camera}", "{DynamicResource Icon.Shield}", "{DynamicResource Icon.Sync}", "{DynamicResource Icon.Edit}", "{DynamicResource Icon.Send}"],
            rows.Select(r => (string?)r.Element(controls + "Glyph")!.Attribute("Text")));
        Assert.All(rows, r => Assert.Equal("{DynamicResource Layout.flow-row-height}", (string?)r.Attribute("MinHeight")));
        var promise = (XElement)flow.NextNode!;
        Assert.Equal("{StaticResource Text.BodyStrong}", (string?)promise.Attribute("Style"));
        Assert.DoesNotContain(privacy.Descendants(Wpf + "TextBlock"), t => ((string?)t.Attribute("Text"))?.Contains("1. ", StringComparison.Ordinal) == true);
    }

    /// <summary>
    /// Review finding B-47: a locked setting says it was decided, with a 12 px lock in text.secondary and a
    /// Caption, under each of the four controls a policy can set; it was a muted line like any hint.
    /// </summary>
    [Fact]
    public void ALockedSettingSaysItWasDecided()
    {
        XNamespace controls = "clr-namespace:ScreenTrail.UI.Controls";
        var locked = Style(Components(), "Settings.Locked");
        var glyph = locked.Descendants(controls + "Glyph").Single();
        Assert.Equal("{DynamicResource Icon.Lock}", (string?)glyph.Attribute("Text"));
        Assert.Equal("{DynamicResource Icon.sm}", (string?)glyph.Attribute("FontSize"));
        Assert.Equal("{DynamicResource Brush.text.secondary}", (string?)glyph.Attribute("Foreground"));
        Assert.Equal("{StaticResource Text.Caption}", (string?)locked.Descendants(Wpf + "TextBlock").Single().Attribute("Style"));

        var uses = new[] { ("Settings/Privacy", "PrivacyPane.xaml", 2), ("Settings/Capture", "CapturePane.xaml", 1), ("Update", "UpdatesPane.xaml", 1) };
        foreach (var (folder, file, count) in uses)
        {
            var view = EmptyStateTests.View(folder, file);
            Assert.Equal(count, view.Descendants(Wpf + "ContentControl").Count(c => (string?)c.Attribute("Style") == "{StaticResource Settings.Locked}"));
        }
    }

    /// <summary>
    /// Review finding B-39: the microphone and the speech model are a tile of their own, not two more heading
    /// levels inside Screenshots, and every Capture tile opens with its heading.
    /// </summary>
    [Fact]
    public void TheMicrophoneIsATileOfItsOwnAndEveryTileHasAHeading()
    {
        var capture = EmptyStateTests.View("Settings/Capture", "CapturePane.xaml");
        var tiles = capture.Descendants(Wpf + "Border").Where(b => (string?)b.Attribute("Style") == "{StaticResource Settings.Tile}").ToList();
        foreach (var tile in tiles)
        {
            var first = tile.Elements(Wpf + "StackPanel").Single().Elements().First();
            var heading = first.Name == Wpf + "TextBlock" ? first : first.Descendants(Wpf + "TextBlock").First();
            Assert.Equal("{StaticResource Text.TileHeading}", (string?)heading.Attribute("Style"));
        }

        var microphone = tiles.Single(t => (string?)t.Attribute(Xaml + "Name") == "MicrophoneTile");
        Assert.Contains(microphone.Descendants(Wpf + "ComboBox"), c => (string?)c.Attribute("AutomationProperties.Name") == "Speech model");
        var screenshots = tiles.Single(t => t.Descendants(Wpf + "TextBlock").Any(x => (string?)x.Attribute("Text") == "Screenshots"));
        Assert.DoesNotContain(screenshots.Descendants(Wpf + "TextBlock"), x => (string?)x.Attribute("Text") is "Microphone" or "Speech model");
        Assert.Contains(tiles, t => (string?)t.Attribute(Xaml + "Name") == "StartWithWindowsTile");
    }

    /// <summary>
    /// Review finding B-12: a field that is wrong looks wrong. Each Settings field with a problem marks its
    /// edge (theme:Field.HasError from the problem's words) and says what is wrong 4 under it, with the
    /// warning glyph, read out as it changes; it was a red sentence beside or below a field that looked fine.
    /// </summary>
    [Theory]
    [InlineData("Settings/Privacy", "PrivacyPane.xaml", "NewProcessProblem")]
    [InlineData("Settings/Privacy", "PrivacyPane.xaml", "NewPatternProblem")]
    [InlineData("Settings/Capture", "CapturePane.xaml", "Problem")]
    [InlineData("Settings/Activation", "ActivationPane.xaml", "Problem")]
    public void AFieldWithAProblemLooksWrongAndSaysWhyUnderIt(string folder, string file, string problem)
    {
        XNamespace theme = "clr-namespace:ScreenTrail.UI.Theme";
        var view = EmptyStateTests.View(folder, file);
        var field = view.Descendants(Wpf + "TextBox").Single(t => (string?)t.Attribute(theme + "Field.HasError") == $"{{Binding {problem}, Converter={{StaticResource HasText}}}}");
        Assert.Contains(view.Descendants(Wpf + "ContentControl"), c => (string?)c.Attribute("Style") == "{StaticResource Field.ErrorLine}" && (string?)c.Attribute("Content") == $"{{Binding {problem}}}");
        Assert.DoesNotContain(view.Descendants(Wpf + "TextBlock"), t => (string?)t.Attribute("Text") == $"{{Binding {problem}}}" && (string?)t.Attribute("Style") == "{StaticResource Text.Error}");
        Assert.NotNull(field);
    }

    [Fact]
    public void TheErrorLineIsTheWarningGlyphAndWordsReadOutAsTheyChange()
    {
        XNamespace controls = "clr-namespace:ScreenTrail.UI.Controls";
        var line = Style(Components(), "Field.ErrorLine");
        Assert.Equal("0,4,0,0", Setter(line, "Margin"));
        Assert.Equal("{DynamicResource Icon.Warning}", (string?)line.Descendants(controls + "Glyph").Single().Attribute("Text"));
        var words = line.Descendants(Wpf + "TextBlock").Single();
        Assert.Equal("{StaticResource Text.Error}", (string?)words.Attribute("Style"));
        Assert.Equal("Polite", (string?)words.Attribute(controls + "LiveRegion.Setting"));

        // A clash on a hotkey is a second row across the field's column and the rest.
        var capture = EmptyStateTests.View("Settings/Capture", "CapturePane.xaml");
        var row = capture.Descendants(Wpf + "Grid").Single(g => (string?)g.Attribute(Xaml + "Name") == "HotkeyRow");
        var error = row.Elements(Wpf + "ContentControl").Single();
        Assert.Equal(("1", "1", "2"), ((string?)error.Attribute("Grid.Row"), (string?)error.Attribute("Grid.Column"), (string?)error.Attribute("Grid.ColumnSpan")));
    }

    /// <summary>
    /// A field marked with theme:Field.HasError alone (B-12: its words are on the line under it) has no
    /// Field.Error, and the error trigger's ItemStatus refuses null: 84 binding errors on the Settings render
    /// in CI. The binding falls back to empty.
    /// </summary>
    [Fact]
    public void AnErrorEdgeWithoutWordsBindsNoNullItemStatus()
    {
        var setters = Components().Descendants(Wpf + "Setter").Where(s => (string?)s.Attribute("Property") == "AutomationProperties.ItemStatus" && ((string?)s.Attribute("Value"))?.Contains("theme:Field.Error", StringComparison.Ordinal) == true).ToList();
        Assert.NotEmpty(setters);
        Assert.All(setters, s => Assert.Contains("TargetNullValue=''", (string?)s.Attribute("Value"), StringComparison.Ordinal));
    }
}
