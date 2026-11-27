using System.Xml.Linq;

namespace ScreenTrail.Tests.Accessibility;

/// <summary>
/// The note as it will land, as the XAML draws it (Spec v0.6.0 §5 S3), read off the files so it holds on
/// the Mac and on every commit: each step's frame is a button into the strip's commands, an inferred step
/// has Looks right with its key, the masks and the count are drawn, the v0.5 screenshot column is gone,
/// and the strip opens over the note. What the commands do is the view models'.
/// </summary>
public sealed class NoteControlsTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Controls = "clr-namespace:ScreenTrail.UI.Controls";
    private static readonly XNamespace Theme = "clr-namespace:ScreenTrail.UI.Theme";

    [Fact]
    public void AStepsFrameIsAButtonThatOpensItAndTakesSpace()
    {
        var note = Load("Review", "NoteEditorView.xaml");

        var frame = Assert.Single(Buttons(note), b => (string?)b.Attribute("Tag") == "{Binding Id}");
        Assert.Equal("{Binding DataContext.ShowFrameCommand, RelativeSource={RelativeSource AncestorType=UserControl}}", (string?)frame.Attribute("Command"));
        Assert.Equal("OnFrameKeyDown", (string?)frame.Attribute("PreviewKeyDown"));
        Assert.Equal("{Binding SpokenName}", (string?)frame.Attribute("AutomationProperties.Name"));
        Assert.Equal("{StaticResource StepFrame}", (string?)frame.Attribute("Style"));

        // The picture is the strip's own thumbnail, and every mask is drawn over it in a token colour.
        Assert.Contains(frame.Descendants(Wpf + "Image"), i => (string?)i.Attribute("Source") == "{Binding Item.Thumbnail}");
        var mask = Assert.Single(frame.Descendants(Wpf + "Rectangle"));
        Assert.Equal("{DynamicResource Brush.shell.bg}", (string?)mask.Attribute("Fill"));
        Assert.Equal("{Binding Offset}", (string?)mask.Attribute("Margin"));
    }

    [Fact]
    public void AStepsFrameSaysDelInItsHelpAndTheNoteCarriesTheUndo()
    {
        // Spec v0.6.8 §5 S3: Del on a step's frame deletes it, as in the strip, with the same five-second
        // undo, shown on the note, where the technician is looking.
        var note = Load("Review", "NoteEditorView.xaml");

        var frame = Assert.Single(Buttons(note), b => (string?)b.Attribute("Tag") == "{Binding Id}");
        Assert.Equal("{Binding DataContext.FrameHelp, RelativeSource={RelativeSource AncestorType=UserControl}}", (string?)frame.Attribute("AutomationProperties.HelpText"));
        // Its name first ("Screenshot 2. Enter or click to enlarge, ..."), since R-02 took it from under the image.
        Assert.Equal("{Binding Tip}", (string?)frame.Attribute("ToolTip"));

        // Ctrl+Z is heard on the way down, before a text box takes it for its own undo.
        Assert.Equal("OnNoteKeyDown", (string?)note.Attribute("PreviewKeyDown"));

        var toast = Assert.Single(note.Descendants(Wpf + "ContentControl"), b => (string?)b.Attribute("Style") == "{StaticResource Toast}");
        Assert.Equal("{Binding HasUndoToast, Converter={StaticResource Visible}}", (string?)toast.Attribute("Visibility"));
        var words = Assert.Single(toast.Descendants(Wpf + "TextBlock"), t => (string?)t.Attribute("Text") == "{Binding UndoToast}");
        Assert.Equal("Assertive", (string?)words.Attribute(Controls + "LiveRegion.Setting"));
        var undo = Assert.Single(toast.Descendants(Wpf + "Button"));
        Assert.Equal("{Binding UndoDeleteCommand}", (string?)undo.Attribute("Command"));
        Assert.Equal("Undo", (string?)undo.Attribute("AutomationProperties.Name"));
        Assert.Equal("Ctrl+Z", (string?)undo.Attribute("AutomationProperties.AcceleratorKey"));
    }

    [Fact]
    public void TheTwoButtonsUnderAnEnlargedScreenshotAreTheSameHeight()
    {
        // "OCR text" is a Button.Secondary and "Blur a region" a Pill.Toggle. Both had the control
        // height as a minimum only, and the row's bottom margin was on one of them, so the row stretched
        // the other 8 px taller. Both now take the control height itself, and the same margin above and below.
        var strip = Load("Review", "FilmstripView.xaml");

        var ocr = Assert.Single(Buttons(strip), b => (string?)b.Attribute("Command") == "{Binding ToggleOcrTextCommand}");
        var blur = Assert.Single(strip.Descendants(Wpf + "ToggleButton"), b => (string?)b.Attribute(Xaml + "Name") == "BlurMode");

        Assert.Equal("{DynamicResource Layout.control-height}", (string?)ocr.Attribute("Height"));
        Assert.Equal("{DynamicResource Layout.control-height}", (string?)blur.Attribute("Height"));
        Assert.Equal(VerticalMargin(ocr), VerticalMargin(blur));

        // What they do is unchanged.
        Assert.Equal("{Binding ToggleOcrTextCommand}", (string?)ocr.Attribute("Command"));
        Assert.Equal("OnBlurModeChanged", (string?)blur.Attribute("Checked"));
        Assert.Equal("OnBlurModeChanged", (string?)blur.Attribute("Unchecked"));
    }

    private static (string Top, string Bottom) VerticalMargin(XElement element)
    {
        var sides = ((string?)element.Attribute("Margin") ?? "0").Split(',');
        return sides.Length switch
        {
            1 => (sides[0], sides[0]),
            2 => (sides[1], sides[1]),
            _ => (sides[1], sides[3]),
        };
    }

    [Fact]
    public void AnInferredStepSaysSoAndHasLooksRightWithAltCAndEdit()
    {
        var note = Load("Review", "NoteEditorView.xaml");

        var confirm = Assert.Single(Buttons(note), b => (string?)b.Attribute("Command") == "{Binding DataContext.ConfirmStepCommand, RelativeSource={RelativeSource AncestorType=UserControl}}");
        Assert.Equal("{StaticResource Button.Secondary}", (string?)confirm.Attribute("Style"));
        Assert.Equal("Alt+C", (string?)confirm.Attribute("AutomationProperties.AcceleratorKey"));
        Assert.Equal("{Binding ConfirmName}", (string?)confirm.Attribute("AutomationProperties.Name"));
        Assert.Contains(confirm.Descendants(Wpf + "TextBlock"), t => (string?)t.Attribute("Text") == "Looks right");

        var edit = Assert.Single(Buttons(note), b => (string?)b.Attribute("Content") == "Edit");
        Assert.Equal("{Binding EditName}", (string?)edit.Attribute("AutomationProperties.Name"));

        // Review finding B-34: one line under the step, a Warning badge with its glyph and the words (never
        // colour alone), then Looks right and Edit, both compact, 8 apart; and no side stripe or its column.
        var line = note.Descendants(Wpf + "WrapPanel").Single(w => (string?)w.Attribute(Xaml + "Name") == "InferredLine");
        var badge = line.Elements(Wpf + "ContentControl").Single();
        Assert.Equal("{StaticResource Badge}", (string?)badge.Attribute("Style"));
        Assert.Equal("Warning", (string?)badge.Attribute(Theme + "Badge.Tone"));
        Assert.Equal("{StaticResource Icon.Warning}", (string?)badge.Attribute(Theme + "Badge.Glyph"));
        Assert.Equal("{Binding InferredLine}", (string?)badge.Attribute("Content"));
        Assert.Equal("{Binding VerificationTooltip}", (string?)badge.Attribute("ToolTip"));
        Assert.Equal("0,0,8,0", (string?)badge.Attribute("Margin"));
        Assert.Equal([confirm, edit], line.Elements(Wpf + "Button"));
        Assert.All(line.Elements(Wpf + "Button"), b => Assert.Equal("True", (string?)b.Attribute(Theme + "Density.Compact")));
        Assert.DoesNotContain(note.Descendants(Wpf + "Border"), b => (string?)b.Attribute("Background") == "{DynamicResource Brush.confidence.low}");
        Assert.DoesNotContain(note.Descendants(Wpf + "ColumnDefinition"), c => (string?)c.Attribute("Width") == "9");
    }

    [Fact]
    public void TheStripsUndoToastIsOverTheStripNotInItsFooter()
    {
        // Review finding D-24: in the footer, the toast made the footer about 38 px taller for five seconds
        // and the thumbnails jumped twice. Over the strip's bottom edge nothing moves.
        var strip = Load("Review", "FilmstripView.xaml");
        var toast = Assert.Single(strip.Descendants(Wpf + "ContentControl"), b => (string?)b.Attribute("Visibility") == "{Binding HasUndoToast, Converter={StaticResource Visible}}");
        var layer = toast.Parent!;

        Assert.Equal("StripToastLayer", (string?)layer.Attribute(Xaml + "Name"));
        Assert.Equal("1", (string?)layer.Attribute("Grid.Row"));
        Assert.Equal("Bottom", (string?)toast.Attribute("VerticalAlignment"));
        Assert.DoesNotContain(toast.Ancestors(), e => (string?)e.Attribute(Xaml + "Name") == "StripFooter");
    }

    [Fact]
    public void UnderTheNoteAreTheMaskingCountAndTheSaveState()
    {
        var note = Load("Review", "NoteEditorView.xaml");

        Assert.Contains(note.Descendants(Wpf + "TextBlock"), t => (string?)t.Attribute("Text") == "{Binding MaskingLabel}");
        var save = Assert.Single(note.Descendants(Wpf + "TextBlock"), t => (string?)t.Attribute("Text") == "{Binding SaveLabel}");
        Assert.Equal("Polite", (string?)save.Attribute(Controls + "LiveRegion.Setting"));
    }

    [Fact]
    public void TheSectionsAreTheTicketsFourInOrderEditedInPlace()
    {
        var note = Load("Review", "NoteEditorView.xaml");
        var headings = note.Descendants(Wpf + "TextBlock")
            .Select(t => (string?)t.Attribute("Text"))
            .Where(t => t is "Problem" or "Steps" or "Result" or "Follow-ups")
            .ToList();

        Assert.Equal(["Problem", "Steps", "Result", "Follow-ups"], headings);
        Assert.All(note.Descendants(Wpf + "TextBox"), box => Assert.Equal("{StaticResource Input.InPlace}", (string?)box.Attribute("Style")));
    }

    [Fact]
    public void TheScreenshotColumnIsGoneAndTheStripOpensOverTheNote()
    {
        var review = Load("Review", "ReviewPane.xaml");

        Assert.Empty(review.Descendants(Wpf + "GridSplitter"));
        var strip = Assert.Single(review.Descendants(Wpf + "Border"), b => (string?)b.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "StripPane");
        Assert.Equal("{Binding Strip.Shown, Converter={StaticResource Visible}}", (string?)strip.Attribute("Visibility"));
        Assert.Contains(strip.Descendants(Wpf + "Button"), b => (string?)b.Attribute("AutomationProperties.Name") == "Back to the note" && (string?)b.Attribute("AutomationProperties.AcceleratorKey") == "Alt+1");
    }

    [Theory]
    [InlineData("Review", "NoteEditorView.xaml")]
    [InlineData("Review", "ReviewPane.xaml")]
    public void TheNoteUsesTheTokensAndPlainText(string folder, string file)
    {
        var root = Load(folder, file);

        Assert.DoesNotContain(root.Descendants().Attributes(), a => a.Value.StartsWith('#'));
        Assert.DoesNotContain(root.Descendants().Attributes(), a => a.Name.LocalName is "FontSize" or "Foreground" or "Background" or "BorderBrush" or "Fill" && !a.Value.StartsWith('{') && a.Value != "Transparent");
        Assert.DoesNotContain(root.Descendants().Attributes(), a => a.Value.Contains('\u2014', StringComparison.Ordinal));
    }

    private static IEnumerable<XElement> Buttons(XElement root) => root.Descendants(Wpf + "Button");

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

    /// <summary>Review finding B-36: what was said is a Caption in text.secondary beside its rule, not monospace.</summary>
    [Fact]
    public void AQuoteIsACaptionBesideItsRuleNotMonospace()
    {
        var note = Load("Review", "NoteEditorView.xaml");
        var quote = note.Descendants(Wpf + "Border").Single(b => (string?)b.Attribute(Xaml + "Name") == "StepQuote");
        Assert.Equal("2,0,0,0", (string?)quote.Attribute("BorderThickness"));
        Assert.Equal("{DynamicResource Brush.border.strong}", (string?)quote.Attribute("BorderBrush"));
        Assert.Equal("12,0,0,0", (string?)quote.Attribute("Padding"));
        var words = quote.Elements(Wpf + "TextBlock").Single();
        Assert.Equal("{StaticResource Text.Caption}", (string?)words.Attribute("Style"));
        Assert.Equal("{DynamicResource Brush.text.secondary}", (string?)words.Attribute("Foreground"));
        Assert.DoesNotContain(note.Descendants(Wpf + "TextBlock"), t => (string?)t.Attribute("Style") == "{StaticResource Text.Mono}");
    }

    /// <summary>
    /// Review findings B-35 and R-02: a step's frame is 96 x 60 with only a state worth reading under it,
    /// and under a narrow note the evidence moves under the step's words, the frame then its time in a
    /// tabular Caption 8 from it; at 560 and up it stays beside them, the time in Text.Numeric.
    /// </summary>
    [Fact]
    public void AStepStacksUnderANarrowNoteAndItsFrameIsSmall()
    {
        var note = Load("Review", "NoteEditorView.xaml");
        var frames = note.Descendants(Controls + "Repeater").Single(r => (string?)r.Attribute(Xaml + "Name") == "StepFrames");
        var button = frames.Descendants(Wpf + "Button").First();
        Assert.Equal("{DynamicResource Layout.step-frame-width}", (string?)button.Attribute("Width"));
        Assert.Equal("{DynamicResource Layout.step-frame-height}", (string?)button.Attribute("Height"));
        var caption = frames.Descendants(Wpf + "WrapPanel").Single(w => (string?)w.Attribute(Xaml + "Name") == "FrameCaption");
        Assert.Equal("{Binding HasCaption, Converter={StaticResource Visible}}", (string?)caption.Attribute("Visibility"));
        Assert.Contains(caption.Descendants(Wpf + "TextBlock"), t => (string?)t.Attribute("Text") == "{Binding StateWord}");

        const string Narrow = "{Binding DataContext.IsNarrow, RelativeSource={RelativeSource AncestorType=UserControl}}";
        var evidence = note.Descendants(Wpf + "Grid").Single(g => (string?)g.Attribute(Xaml + "Name") == "Evidence");
        var stacked = evidence.Descendants(Wpf + "DataTrigger").First(t => (string?)t.Attribute("Binding") == Narrow);
        Assert.Contains(stacked.Elements(Wpf + "Setter"), s => (string?)s.Attribute("Property") == "Grid.Row" && (string?)s.Attribute("Value") == "1");

        var time = evidence.Descendants(Wpf + "TextBlock").Single(t => (string?)t.Attribute(Xaml + "Name") == "StepTime");
        var style = time.Descendants(Wpf + "Style").Single();
        Assert.Equal("{StaticResource Text.Numeric}", (string?)style.Attribute("BasedOn"));
        var under = style.Descendants(Wpf + "DataTrigger").Single();
        Assert.Contains(under.Elements(Wpf + "Setter"), s => (string?)s.Attribute("Property") == "Margin" && (string?)s.Attribute("Value") == "8,0,0,0");
        Assert.Contains(under.Elements(Wpf + "Setter"), s => (string?)s.Attribute("Property") == "FontSize" && (string?)s.Attribute("Value") == "{DynamicResource Size.sm}");

        var code = File.ReadAllText(Path.Combine(Review.EnvelopeLayoutTests.Ui(), "Review", "NoteEditorViewModel.cs"));
        Assert.Contains("public const double NarrowBelow = 560;", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// Review finding B-37: a draft on its way is a 20 px progress ring and the words in Body where the note
    /// will be, then the note's shape in skeleton, not an Iris disc with a still glyph.
    /// </summary>
    [Fact]
    public void ADraftOnItsWayIsARingAndTheNotesShapeInSkeleton()
    {
        var note = Load("Review", "NoteEditorView.xaml");
        var pane = note.Descendants(Wpf + "StackPanel").Single(p => (string?)p.Attribute(Xaml + "Name") == "DraftPendingPane");
        Assert.DoesNotContain(pane.Descendants(Wpf + "Border"), b => (string?)b.Attribute("Style") == "{StaticResource EmptyState.Disc}");

        var ring = pane.Descendants(Wpf + "Control").Single(c => (string?)c.Attribute(Xaml + "Name") == "DraftingRing");
        Assert.Equal("{StaticResource ProgressRing}", (string?)ring.Attribute("Style"));
        Assert.Equal("{DynamicResource Icon.lg}", (string?)ring.Attribute("Width"));
        Assert.Contains(pane.Descendants(Wpf + "TextBlock"), t => (string?)t.Attribute("Text") == "{Binding DraftPendingMessage}" && (string?)t.Attribute("Style") == "{StaticResource Text.Body}");
        Assert.Equal(3, pane.Descendants(Wpf + "Border").Count(b => (string?)b.Attribute("Style") == "{StaticResource Skeleton}"));
    }
}
