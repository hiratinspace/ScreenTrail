using System.Globalization;
using System.Xml.Linq;

namespace ScreenTrail.Tests.Accessibility;

/// <summary>
/// The All sessions table as the XAML draws it (Spec §5 S4, as built v0.6.6; §3 Table; §7), read off the
/// files so it holds on the Mac and on every commit. Two defects are held shut here: the selection's tint
/// was each cell's, so it stopped where the columns stopped, and keyboard focus was a ring round the first
/// cell. Both are the row's now. That the row really runs the table's width and the outline really shows
/// when the arrows move is held on Windows (AllSessionsTableTests there).
/// </summary>
public sealed class AllSessionsTableTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// The table's width at the window's 960 minimum (review finding D-06): the window's frame (16), the
    /// narrow rail (232) and the pane's narrow padding (16 each side) taken off, and a vertical scroll bar
    /// (18) for a list longer than the pane.
    /// </summary>
    private const double TableAtTheMinimum = 960 - 16 - 232 - 32 - 18;

    [Fact]
    public void TheSelectionIsTheRowsSoItRunsTheTablesWidth()
    {
        var row = Style("Table.Row");
        var selected = Assert.Single(row.Element(Wpf + "Style.Triggers")!.Elements(Wpf + "Trigger"), t => (string?)t.Attribute("Property") == "IsSelected");
        Assert.Equal("{DynamicResource Brush.bg.selected}", Setter(selected, "Background"));

        // Hover first, selection after it: a selected row under the pointer keeps the selection's tint.
        var order = row.Element(Wpf + "Style.Triggers")!.Elements(Wpf + "Trigger").Select(t => (string?)t.Attribute("Property")).ToList();
        Assert.True(order.IndexOf("IsMouseOver") < order.IndexOf("IsSelected"), "The hover trigger must come before the selection's.");

        // The row's fill is drawn by the row's own border, behind every cell and past the last one.
        var fill = Assert.Single(row.Descendants(Wpf + "Border"), b => (string?)b.Attribute(Xaml + "Name") == "DGR_Border");
        Assert.Equal("{TemplateBinding Background}", (string?)fill.Attribute("Background"));
        Assert.Single(fill.Descendants(Wpf + "DataGridCellsPresenter"));

        // No cell paints a fill of its own over it, in any state.
        var cell = CellStyle();
        Assert.Equal("Transparent", Setter(cell, "Background"));
        Assert.DoesNotContain(
            cell.Descendants(Wpf + "Setter"),
            s => (string?)s.Attribute("Property") == "Background" && (string?)s.Attribute("Value") != "Transparent");
    }

    [Fact]
    public void ASelectedRowIsMarkedByShapeAsWellAsTint()
    {
        // The tint is faint on white, so the row carries the one selection bar at its leading edge too
        // (design system 10.4: collapsed and never hit-tested, from the SelectionBar style).
        var row = Style("Table.Row");
        var mark = Assert.Single(row.Descendants(Wpf + "Rectangle"), r => (string?)r.Attribute(Xaml + "Name") == "Indicator");
        Assert.Equal("{StaticResource SelectionBar}", (string?)mark.Attribute("Style"));

        var selected = Assert.Single(TemplateTriggers(row), t => (string?)t.Attribute("Property") == "IsSelected");
        Assert.Contains(selected.Elements(Wpf + "Setter"), s => (string?)s.Attribute("TargetName") == "Indicator" && (string?)s.Attribute("Value") == "Visible");
    }

    [Fact]
    public void KeyboardFocusIsAnOutlineRoundTheWholeRowNotOneCell()
    {
        var row = Style("Table.Row");
        Assert.Equal("True", Setter(row, "controls:RowFocus.Track"));
        Assert.Equal("{DynamicResource Brush.accent.primary}", Setter(row, "BorderBrush"));

        var outline = Assert.Single(row.Descendants(Wpf + "Rectangle"), r => (string?)r.Attribute(Xaml + "Name") == "FocusOutline");
        Assert.Equal("{TemplateBinding BorderBrush}", (string?)outline.Attribute("Stroke"));
        Assert.Equal("{DynamicResource Focus.width}", (string?)outline.Attribute("StrokeThickness"));
        Assert.Equal("Collapsed", (string?)outline.Attribute("Visibility"));
        Assert.Equal("False", (string?)outline.Attribute("IsHitTestVisible"));

        // It is a sibling over the row's fill, not inside a cell, and is as wide as the row.
        Assert.Null(outline.Attribute("Width"));
        Assert.Null(outline.Attribute("HorizontalAlignment"));
        Assert.Equal("Grid", outline.Parent!.Name.LocalName);
        Assert.DoesNotContain(outline.Ancestors(), a => a.Name.LocalName == "Border");

        // Shown for keyboard focus only (Spec §3): RowFocus decides, not IsKeyboardFocusWithin, which a click sets too.
        var shown = Assert.Single(TemplateTriggers(row), t => (string?)t.Attribute("Property") == "controls:RowFocus.Shown");
        Assert.Contains(shown.Elements(Wpf + "Setter"), s => (string?)s.Attribute("TargetName") == "FocusOutline" && (string?)s.Attribute("Value") == "Visible");
        Assert.DoesNotContain(row.Descendants(Wpf + "Trigger"), t => (string?)t.Attribute("Property") == "IsKeyboardFocusWithin");

        // The cell draws no ring of its own any more.
        Assert.Equal("{x:Null}", Setter(CellStyle(), "FocusVisualStyle"));
    }

    [Fact]
    public void InHighContrastTheSelectedRowIsAHighlightEdgeFromTheTokens()
    {
        // Design system 12, review finding A-35: not the system flag, which a render with the high-contrast
        // dictionary never sets, but the SelectionEdge, which is Highlight in that dictionary alone.
        var row = Style("Table.Row");
        Assert.DoesNotContain(row.Descendants(Wpf + "MultiDataTrigger"), _ => true);
        var edge = Assert.Single(row.Descendants(Wpf + "Border"), b => (string?)b.Attribute(Xaml + "Name") == "Edge");
        Assert.Equal("{StaticResource SelectionEdge}", (string?)edge.Attribute("Style"));

        var selected = Assert.Single(TemplateTriggers(row), t => (string?)t.Attribute("Property") == "IsSelected");
        Assert.Contains(selected.Elements(Wpf + "Setter"), s => (string?)s.Attribute("TargetName") == "Edge" && (string?)s.Attribute("Value") == "Visible");
        Assert.Null(CellStyle().Element(Wpf + "Style.Triggers"));
    }

    [Fact]
    public void OneColumnTakesTheWidthTheOthersLeaveAndAllFitTheSmallestWindow()
    {
        var history = Load("History", "HistoryPane.xaml");
        var table = Assert.Single(history.Descendants(Wpf + "DataGrid"));
        var columns = table.Element(Wpf + "DataGrid.Columns")!.Elements().ToList();
        Assert.Equal(["Started", "Duration", "Status", "Tool", "Screenshots"], columns.Select(c => (string?)c.Attribute("Header")));

        var star = Assert.Single(columns, c => (string?)c.Attribute("Width") == "*");
        Assert.Equal("Status", (string?)star.Attribute("Header"));

        // At the 960 minimum nothing scrolls sideways: the fixed columns and Status at its least fit.
        var least = columns.Where(c => c != star).Sum(c => double.Parse((string)c.Attribute("Width")!, CultureInfo.InvariantCulture))
            + double.Parse((string)star.Attribute("MinWidth")!, CultureInfo.InvariantCulture);
        Assert.True(least <= TableAtTheMinimum, $"The columns need {least} px and the table has {TableAtTheMinimum} at the smallest window.");

        // Nothing caps the table short of the pane, so the star column has the pane's width to fill.
        Assert.DoesNotContain(table.Ancestors(), a => a.Attribute("MaxWidth") is not null);

        // A cell too narrow for its words ends them in an ellipsis.
        foreach (var text in columns.Where(c => c.Name.LocalName == "DataGridTextColumn"))
        {
            Assert.Equal("{StaticResource History.CellText}", (string?)text.Attribute("ElementStyle"));
        }

        var trimmed = Assert.Single(history.Descendants(Wpf + "Style"), s => (string?)s.Attribute(Xaml + "Key") == "History.CellText");
        Assert.Equal("CharacterEllipsis", Setter(trimmed, "TextTrimming"));
    }

    [Fact]
    public void TheScreenIsOnTheTokensAndTheSpacingScale()
    {
        var history = Load("History", "HistoryPane.xaml");
        var scale = new HashSet<double> { 0, 4, 8, 12, 16, 24, 32, 40 };
        foreach (var attribute in history.Descendants().Attributes().Where(a => a.Name.LocalName is "Margin" or "Padding" && !a.Value.StartsWith('{')))
        {
            var sides = attribute.Value.Split(',').Select(v => Math.Abs(double.Parse(v, CultureInfo.InvariantCulture)));
            Assert.True(sides.All(scale.Contains), $"{attribute.Parent!.Name.LocalName} {attribute.Name.LocalName}=\"{attribute.Value}\" is off the spacing scale.");
        }

        Assert.DoesNotContain(history.Descendants().Attributes(), a => a.Name.LocalName is "CornerRadius" or "FontSize" && !a.Value.StartsWith('{'));
        Assert.DoesNotContain(history.Descendants().Attributes(), a => a.Value.StartsWith('#'));
        Assert.DoesNotContain(history.DescendantNodes().OfType<XComment>(), c => c.Value.Contains('—', StringComparison.Ordinal));
        Assert.DoesNotContain(history.Descendants().Attributes(), a => a.Value.Contains('—', StringComparison.Ordinal));
    }

    private static IEnumerable<XElement> TemplateTriggers(XElement style) =>
        style.Descendants(Wpf + "ControlTemplate.Triggers").Single().Elements(Wpf + "Trigger");

    /// <summary>The cell style the table sets, which lives inside the Table style's setter.</summary>
    private static XElement CellStyle() =>
        Style("Table").Elements(Wpf + "Setter").Single(s => (string?)s.Attribute("Property") == "CellStyle").Descendants(Wpf + "Style").First();

    private static XElement Style(string key) =>
        Load("Theme", "Components.xaml").Elements(Wpf + "Style").Single(s => (string?)s.Attribute(Xaml + "Key") == key);

    private static string? Setter(XElement parent, string property) =>
        (string?)parent.Elements(Wpf + "Setter").SingleOrDefault(s => (string?)s.Attribute("Property") == property)?.Attribute("Value");

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
