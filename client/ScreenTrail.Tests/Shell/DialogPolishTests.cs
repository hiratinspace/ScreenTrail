using System.Xml.Linq;
using ScreenTrail.Tests.Theme;

namespace ScreenTrail.Tests.Shell;

/// <summary>
/// The dialog, the shortcut sheet and the diagnostics panel, from the premium review (ST-141): what each is
/// drawn with and how the keyboard reaches it.
/// </summary>
public sealed class DialogPolishTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XElement Dialog() => EmptyStateTests.View("Shell", "ConfirmWindow.xaml");

    private static string Code(string folder, string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(directory!.FullName, "client", "ScreenTrail.UI")))
        {
            directory = directory.Parent;
        }

        return File.ReadAllText(Path.Combine(directory.FullName, "client", "ScreenTrail.UI", folder, file));
    }

    private static XElement Named(XElement view, string name) =>
        view.Descendants().Single(e => (string?)e.Attribute(Xaml + "Name") == name);

    /// <summary>
    /// Review finding B-50: the question is on the surface with 24 around it, and the buttons on a footer of
    /// their own, bg.base under a border.subtle rule, 24,16, right aligned 8 apart.
    /// </summary>
    [Fact]
    public void TheDialogIsASurfaceOverAFooter()
    {
        var dialog = Dialog();
        Assert.Equal("{DynamicResource Brush.bg.surface}", (string?)Named(dialog, "Body").Attribute("Background"));
        Assert.Equal("{DynamicResource Padding.pane}", (string?)Named(dialog, "Body").Elements().Single().Attribute("Margin"));

        var footer = Named(dialog, "Footer");
        Assert.Equal("{DynamicResource Brush.bg.base}", (string?)footer.Attribute("Background"));
        Assert.Equal("{DynamicResource Brush.border.subtle}", (string?)footer.Attribute("BorderBrush"));
        Assert.Equal("0,1,0,0", (string?)footer.Attribute("BorderThickness"));
        Assert.Equal("{DynamicResource Padding.dialog-footer}", (string?)footer.Attribute("Padding"));
        var buttons = footer.Elements(Wpf + "StackPanel").Single();
        Assert.Equal("Right", (string?)buttons.Attribute("HorizontalAlignment"));
        Assert.Equal(2, buttons.Elements(Wpf + "Button").Count());
        Assert.Equal("8,0,0,0", (string?)Named(dialog, "Confirm").Attribute("Margin"));
    }

    /// <summary>
    /// Review finding B-51: the kind's glyph is 20 with 12 after it, and what follows and the phrase box start
    /// at the question's text, so the glyph hangs; the explanation is Secondary 8 under the question, and a
    /// plain question's glyph is text.secondary rather than Iris.
    /// </summary>
    [Fact]
    public void TheGlyphHangsBesideTheQuestionAndTheWordsUnderIt()
    {
        XNamespace controls = "clr-namespace:ScreenTrail.UI.Controls";
        var question = Named(Dialog(), "Question");
        var mark = question.Elements(controls + "Glyph").Single();
        Assert.Equal("{DynamicResource Icon.lg}", (string?)mark.Attribute("FontSize"));
        Assert.Equal("0,2,12,0", (string?)mark.Attribute("Margin"));

        var consequence = Named(question, "ConsequenceBox");
        Assert.Equal(("1", "1", "0,8,0,0"), ((string?)consequence.Attribute("Grid.Row"), (string?)consequence.Attribute("Grid.Column"), (string?)consequence.Attribute("Margin")));
        Assert.Equal("{StaticResource Text.Secondary}", (string?)consequence.Descendants(Wpf + "TextBlock").Single().Attribute("Style"));
        Assert.Equal("1", (string?)Named(question, "TypedPart").Attribute("Grid.Column"));

        var code = Code("Shell", "ConfirmWindow.xaml.cs");
        Assert.DoesNotContain("Brush.accent.primary", code, StringComparison.Ordinal);
        Assert.Contains("\"Brush.text.secondary\"", code, StringComparison.Ordinal);
    }

    /// <summary>Review finding B-52: a notice's reason is plain Secondary under the heading, not a warning banner too.</summary>
    [Fact]
    public void ANoticeSaysItsReasonOnceWithoutABanner()
    {
        var code = Code("Shell", "ConfirmWindow.xaml.cs");
        Assert.DoesNotContain("\"Banner\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Banner.SetTone", code, StringComparison.Ordinal);
        Assert.Contains("words.Action is null ? \"Brush.state.paused\"", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// Review finding B-54: a diagnostics value is Body 14 in text.primary beside its Label, tabular for a
    /// count and mono for an identifier; the label column's width is a token; capturing is the drawn 8 px
    /// recording dot, not a "●" in the UI face; the status card is the Info banner.
    /// </summary>
    [Fact]
    public void DiagnosticsValuesReadFirstAndTheRecordingMarkIsDrawn()
    {
        var panel = EmptyStateTests.View("Diagnostics", "DiagnosticsWindow.xaml");
        var row = panel.Descendants(Wpf + "DataTemplate").Single();
        var value = Named(row, "Value");
        Assert.Equal("{StaticResource Text.Body}", (string?)value.Attribute("Style"));
        Assert.Null(value.Attribute("FontSize"));
        Assert.Contains(row.Descendants(Wpf + "TextBlock"), t => (string?)t.Attribute("Width") == "{DynamicResource Layout.diagnostics-label-width}" && (string?)t.Attribute("Style") == "{StaticResource Text.Label}");
        var faces = row.Descendants(Wpf + "Setter").Where(s => (string?)s.Attribute("Property") == "Style").Select(s => (string?)s.Attribute("Value")).ToList();
        Assert.Equal(["{StaticResource Text.Numeric}", "{StaticResource Text.Mono}"], faces);

        Assert.DoesNotContain(panel.Descendants().Attributes(), a => a.Value.Contains('●', StringComparison.Ordinal));
        var dot = Named(panel, "RecordingMark");
        Assert.Equal("{StaticResource StateDot}", (string?)dot.Descendants(Wpf + "Style").Single().Attribute("BasedOn"));
        Assert.DoesNotContain(panel.Descendants(Wpf + "ColumnDefinition"), c => (string?)c.Attribute("Width") == "128");
    }

    /// <summary>Review finding B-55: the rows card hides while unanswered, and one Caption says why under the headline.</summary>
    [Fact]
    public void TheUnansweredPanelHidesItsRowsBehindOneLine()
    {
        var panel = EmptyStateTests.View("Diagnostics", "DiagnosticsWindow.xaml");
        Assert.Contains(panel.Descendants(Wpf + "Border"), b => (string?)b.Attribute("Style") == "{StaticResource Card}" && (string?)b.Attribute("Visibility") == "{Binding RowsVisibility}");
        Assert.Contains(panel.Descendants(Wpf + "TextBlock"), t => (string?)t.Attribute("Style") == "{StaticResource Text.Caption}" && (string?)t.Attribute("Text") == "{Binding InPlaceOfRows}");
        Assert.Contains(panel.Descendants(Wpf + "Button"), b => (string?)b.Attribute("Command") == "{Binding CopyCommand}");
    }

    /// <summary>
    /// Review finding D-12: the dialog, the shortcut sheet and diagnostics are their surface, caption and
    /// body alike, so a small window is one sheet rather than a grey band over a lighter body.
    /// </summary>
    [Theory]
    [InlineData("Shell", "ConfirmWindow.xaml")]
    [InlineData("Shell", "ShortcutSheet.xaml")]
    [InlineData("Diagnostics", "DiagnosticsWindow.xaml")]
    public void ASmallWindowIsItsSurfaceCaptionAndBody(string folder, string file)
    {
        XNamespace theme = "clr-namespace:ScreenTrail.UI.Theme";
        var window = EmptyStateTests.View(folder, file);
        Assert.Equal("Surface", (string?)window.Attribute(theme + "TitleBar.Caption"));
        Assert.Equal("{DynamicResource Brush.bg.surface}", (string?)window.Attribute("Background"));
        // bg.base is the dialog's footer alone (B-50), never a body.
        Assert.All(
            window.Descendants().Where(e => (string?)e.Attribute("Background") == "{DynamicResource Brush.bg.base}"),
            e => Assert.Equal("Footer", (string?)e.Attribute(Xaml + "Name")));
    }
}
