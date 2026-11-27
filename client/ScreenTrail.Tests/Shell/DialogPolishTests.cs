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

    /// <summary>
    /// Review finding E-21: the shortcut sheet has a visible way in, a Keyboard shortcuts row in the rail's
    /// foot with the "?" key cap at its right, and F1 opens it as well; the sheet lists F1.
    /// </summary>
    [Fact]
    public void TheShortcutSheetHasARailRowAndF1()
    {
        var shell = EmptyStateTests.View("Shell", "ShellWindow.xaml");
        var row = Named(shell, "ShortcutsItem");
        Assert.Equal("{StaticResource Rail.FootItem}", (string?)row.Attribute("Style"));
        Assert.Equal("{x:Static shell:ShellWindow.ShortcutsCommand}", (string?)row.Attribute("Command"));
        Assert.Equal("Keyboard shortcuts", (string?)row.Attribute("AutomationProperties.Name"));
        Assert.Equal("F1", (string?)row.Attribute("AutomationProperties.AcceleratorKey"));
        var cap = row.Descendants(Wpf + "Border").Single(b => (string?)b.Attribute("Style") == "{StaticResource KeyCap}");
        Assert.Equal("2", (string?)cap.Attribute("Grid.Column"));
        Assert.Equal("?", (string?)cap.Descendants(Wpf + "TextBlock").Single().Attribute("Text"));

        Assert.Contains(shell.Descendants(Wpf + "KeyBinding"), k => (string?)k.Attribute("Key") == "F1" && (string?)k.Attribute("Command") == "{x:Static shell:ShellWindow.ShortcutsCommand}");
        Assert.Contains("CommandBinding(ShortcutsCommand", Code("Shell", "ShellWindow.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("new(\"F1\", \"This sheet\")", Code("Shell", "ShortcutSheet.xaml.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Review finding E-25: the phrase box and both buttons carry what is lost as their help text, so where
    /// focus starts Narrator reads the consequence, not only the question and "Type DISCARD to confirm".
    /// </summary>
    [Theory]
    [InlineData("Typed")]
    [InlineData("Cancel")]
    [InlineData("Confirm")]
    public void WhereFocusStartsReadsWhatIsLost(string name)
    {
        Assert.Equal("{Binding Consequence}", (string?)Named(Dialog(), name).Attribute("AutomationProperties.HelpText"));
    }

    /// <summary>The access key a label offers: the letter after its one single underscore, upper case.</summary>
    private static char? Key(string? label) => label is null ? null : ScreenTrail.Core.Shell.AccessKeys.KeyOf(label);

    /// <summary>
    /// Review finding E-20: every button with words of its own in a window other than the shell has an access
    /// key, unique in that window, and keeps its plain words as its accessible name. The dialog's buttons are
    /// worded at run time and keyed by AccessKeys (ConfirmCopyTests).
    /// </summary>
    [Theory]
    [InlineData("Onboarding", "OnboardingWindow.xaml")]
    [InlineData("Diagnostics", "DiagnosticsWindow.xaml")]
    [InlineData("Shell", "ShortcutSheet.xaml")]
    public void EveryButtonInAWindowHasItsOwnAccessKey(string folder, string file)
    {
        var buttons = EmptyStateTests.View(folder, file).Descendants(Wpf + "Button")
            .Select(b => (Content: (string?)b.Attribute("Content"), Name: (string?)b.Attribute("AutomationProperties.Name")))
            .Where(b => b.Content is not null && !b.Content.StartsWith('{'))
            .ToList();

        Assert.NotEmpty(buttons);
        Assert.All(buttons, b =>
        {
            Assert.True(Key(b.Content) is not null, $"{file}: \"{b.Content}\" has no access key");
            Assert.False(string.IsNullOrEmpty(b.Name) || b.Name.Contains('_', StringComparison.Ordinal), $"{file}: \"{b.Content}\" is not named by its plain words");
        });
        var keys = buttons.Select(b => Key(b.Content)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    /// <summary>
    /// Review finding E-20: a Settings field's label is a Label whose Target is the field, with an access key,
    /// so Alt and the letter puts focus in the field. Across the Settings page (its save bar included) every
    /// key is different, and none is one of Review's Alt keys (1, 2, 3, T, C), which share the window.
    /// </summary>
    [Fact]
    public void SettingsFieldsAreReachedByTheirLabelsKeys()
    {
        string[][] panes =
        [
            ["Settings/Activation", "ActivationPane.xaml"],
            ["Settings/Capture", "CapturePane.xaml"],
            ["Settings/Privacy", "PrivacyPane.xaml"],
            ["Settings/Integrations", "IntegrationsPane.xaml"],
            ["Settings", "SettingsPane.xaml"],
        ];
        var keys = new List<char?>();
        foreach (var pane in panes)
        {
            var view = EmptyStateTests.View(pane[0], pane[1]);
            Assert.DoesNotContain(view.Descendants(Wpf + "TextBlock"), t => (string?)t.Attribute("Style") == "{StaticResource Text.Label}" && view.Descendants().Any(e => e.Name.LocalName is "TextBox" or "ComboBox" && (string?)e.Attribute("AutomationProperties.Name") == (string?)t.Attribute("Text")));
            foreach (var label in view.Descendants(Wpf + "Label"))
            {
                Assert.Equal("{StaticResource Field.Label}", (string?)label.Attribute("Style"));
                var target = (string?)label.Attribute("Target");
                Assert.StartsWith("{Binding ElementName=", target, StringComparison.Ordinal);
                Assert.Contains(view.Descendants(), e => (string?)e.Attribute(Xaml + "Name") == target![21..^1]);
                keys.Add(Key((string?)label.Attribute("Content")));
            }

            keys.AddRange(view.Descendants(Wpf + "Button").Select(b => (string?)b.Attribute("Content")).Where(c => c is not null && !c.StartsWith('{')).Select(Key).Where(k => k is not null));
            keys.AddRange(view.Descendants(Wpf + "AccessText").Select(a => Key((string?)a.Attribute("Text"))));
        }

        Assert.True(keys.Count >= 11, $"Only {keys.Count} keys on the Settings page.");
        Assert.All(keys, k => Assert.NotNull(k));
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.DoesNotContain(keys, k => k is '1' or '2' or '3' or 'T' or 'C');
    }

    /// <summary>Review finding E-20: All sessions' buttons have keys too, clear of Review's.</summary>
    [Fact]
    public void AllSessionsButtonsHaveAccessKeys()
    {
        var history = EmptyStateTests.View("History", "HistoryPane.xaml");
        var contents = history.Descendants(Wpf + "Button").Select(b => (string?)b.Attribute("Content")).Where(c => c is not null && !c.StartsWith('{')).ToList();
        Assert.Contains("_Open", contents);
        Assert.Contains("_Mark as published", contents);
        Assert.Contains("D_iscard selected", contents);
        Assert.All(contents, c => Assert.DoesNotContain(Key(c), new char?[] { null, '1', '2', '3', 'T', 'C' }));
    }
}
