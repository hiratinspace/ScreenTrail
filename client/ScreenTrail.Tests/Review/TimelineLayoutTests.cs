using System.Xml.Linq;

namespace ScreenTrail.Tests.Review;

/// <summary>
/// ST-137, the timeline, the enlarged frame and the strip (premium review 2026-11-26, A-21, E-23, E-24,
/// A-22, A-26, A-27, D-21, D-27), read from the XAML and the source.
/// </summary>
public sealed class TimelineLayoutTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Controls = "clr-namespace:ScreenTrail.UI.Controls";

    /// <summary>
    /// Review finding E-24: the toggle is a disclosure button bound to the panel's state, named "Timeline and
    /// transcript", with a chevron glyph rather than a "▸" or "▾" in its words.
    /// </summary>
    [Fact]
    public void TheToggleSaysWhetherThePanelIsOpen()
    {
        var timeline = EnvelopeLayoutTests.Load("Review", "Timeline", "TimelineView.xaml");
        var toggle = timeline.Descendants(Controls + "DisclosureButton").Single();
        Assert.Equal("{Binding Expanded, Mode=OneWay}", (string?)toggle.Attribute("IsExpanded"));
        Assert.Equal("{Binding ToggleCommand}", (string?)toggle.Attribute("Command"));
        Assert.Equal("Timeline and transcript", (string?)toggle.Attribute("AutomationProperties.Name"));
        Assert.Equal("Alt+T", (string?)toggle.Attribute("AutomationProperties.AcceleratorKey"));

        var setters = toggle.Descendants(Wpf + "Setter").Select(s => (string?)s.Attribute("Value")).ToList();
        Assert.Contains("{StaticResource Icon.ChevronRight}", setters);
        Assert.Contains("{StaticResource Icon.ChevronDown}", setters);

        var source = File.ReadAllText(Path.Combine(EnvelopeLayoutTests.Ui(), "Review", "Timeline", "TimelineViewModel.cs"));
        Assert.DoesNotContain("▸", source, StringComparison.Ordinal);
        Assert.DoesNotContain("▾", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Review findings A-21 and E-23: a frame marker is an 8 px round mark in text.muted, Iris only when its
    /// frame is selected, on a 28 x 28 button with no chrome; the track is 28 high, one Tab stop with arrows
    /// between markers; the summary is a tabular Caption. The drawing code has a baseline, flat bands with an
    /// edge and no opacity, and no Iris square at rest.
    /// </summary>
    [Fact]
    public void TheTrackIsABaselineWithQuietMarkersThatAreButtons()
    {
        var timeline = EnvelopeLayoutTests.Load("Review", "Timeline", "TimelineView.xaml");
        var marker = timeline.Descendants(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == "Timeline.FrameMarker");
        Assert.Equal("Button", (string?)marker.Attribute("TargetType"));
        var setters = marker.Elements(Wpf + "Setter").ToDictionary(s => (string)s.Attribute("Property")!, s => (string?)s.Attribute("Value"));
        Assert.Equal("{DynamicResource Layout.hit-target-min}", setters["Width"]);
        Assert.Equal("{DynamicResource Layout.hit-target-min}", setters["Height"]);
        var mark = marker.Descendants(Wpf + "Ellipse").Single();
        Assert.Equal("{DynamicResource Brush.text.muted}", (string?)mark.Attribute("Fill"));
        var selected = marker.Descendants(Wpf + "Trigger").Single(t => (string?)t.Attribute("Property") == "Tag");
        Assert.Equal("{DynamicResource Brush.accent.primary}", (string?)selected.Element(Wpf + "Setter")!.Attribute("Value"));

        var track = timeline.Descendants(Wpf + "Canvas").Single(c => (string?)c.Attribute(X + "Name") == "Track");
        Assert.Equal("{DynamicResource Layout.hit-target-min}", (string?)track.Attribute("Height"));
        Assert.Equal("Once", (string?)track.Attribute("KeyboardNavigation.TabNavigation"));
        Assert.Equal("Cycle", (string?)track.Attribute("KeyboardNavigation.DirectionalNavigation"));

        var summary = timeline.Descendants(Wpf + "TextBlock").Single(t => (string?)t.Attribute("Text") == "{Binding Summary}");
        Assert.Equal("{StaticResource Text.Caption}", (string?)summary.Attribute("Style"));
        Assert.Null(summary.Attribute("FontSize"));

        var code = File.ReadAllText(Path.Combine(EnvelopeLayoutTests.Ui(), "Review", "Timeline", "TimelineView.xaml.cs"));
        Assert.DoesNotContain("Opacity", code, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Brush.accent.primary", code, StringComparison.Ordinal);
        Assert.DoesNotContain("⚑", code, StringComparison.Ordinal);
        Assert.Contains("\"Brush.border.subtle\"", code, StringComparison.Ordinal);
        Assert.Contains("\"Brush.border.warning\"", code, StringComparison.Ordinal);
        Assert.Contains("\"Timeline.FrameMarker\"", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// Review finding A-22: a transcript line is the time to the minute in a 48 px tabular Caption column and
    /// the words in Body on a 24 px line, not "[14:02:10] ..." in monospace; a screen reader hears it with
    /// no brackets.
    /// </summary>
    [Fact]
    public void ATranscriptLineIsATimeAndItsWordsNotALogLine()
    {
        var line = new ScreenTrail.Core.Review.TranscriptLine("t1", "14:02:10", 0, "clearing the queue now", null);
        Assert.Equal("14:02", line.Clock);
        Assert.Equal("14:02:10, clearing the queue now", line.Spoken);

        var timeline = EnvelopeLayoutTests.Load("Review", "Timeline", "TimelineView.xaml");
        var transcript = timeline.Descendants(Wpf + "ListBox").Single(l => (string?)l.Attribute("AutomationProperties.Name") == "Transcript");
        Assert.DoesNotContain(transcript.Descendants(Wpf + "TextBlock"), t => (string?)t.Attribute("Style") == "{StaticResource Text.Mono}");
        var clock = transcript.Descendants(Wpf + "TextBlock").Single(t => (string?)t.Attribute("Text") == "{Binding Clock}");
        Assert.Equal("{StaticResource Text.Caption}", (string?)clock.Attribute("Style"));
        Assert.Equal("{DynamicResource Layout.transcript-clock-width}", (string?)clock.Attribute("Width"));
        Assert.Equal("Tabular", (string?)clock.Attribute("Typography.NumeralAlignment"));
        var words = transcript.Descendants(Wpf + "TextBlock").Single(t => (string?)t.Attribute("Text") == "{Binding Text}");
        Assert.Equal("{StaticResource Text.Body}", (string?)words.Attribute("Style"));
        Assert.Equal("{DynamicResource Leading.section}", (string?)words.Attribute("LineHeight"));
        var name = transcript.Descendants(Wpf + "Setter").Single(s => (string?)s.Attribute("Property") == "AutomationProperties.Name");
        Assert.Equal("{Binding Spoken}", (string?)name.Attribute("Value"));
    }

    /// <summary>
    /// Review finding A-26: the enlarged frame has one 48 px toolbar, Back (an icon button, Alt+1), the frame's
    /// name in Body strong, then Screen text, Blur a region and Close (an icon button, Esc); the picture sits
    /// in a 1 px border.subtle frame under it, and the blur hint is under the picture. The pane's own way back
    /// goes while a frame is enlarged, so there is one.
    /// </summary>
    [Fact]
    public void TheEnlargedFrameHasOneToolbarRow()
    {
        var strip = EnvelopeLayoutTests.Load("Review", "FilmstripView.xaml");
        var view = strip.Descendants(Wpf + "Border").Single(b => (string?)b.Attribute(X + "Name") == "EnlargedView");
        var grid = view.Element(Wpf + "Grid")!;
        var toolbar = grid.Elements(Wpf + "Grid").Single(g => (string?)g.Attribute("Grid.Row") == "0");
        Assert.Equal("{DynamicResource Layout.toolbar-height}", (string?)toolbar.Attribute("MinHeight"));

        var names = toolbar.Elements().Where(e => e.Name.LocalName is "Button" or "ToggleButton").Select(e => (string?)e.Attribute(X + "Name")).ToList();
        Assert.Equal(["BackFromEnlarged", "OcrText", "BlurMode", "CloseEnlarged"], names);
        var back = toolbar.Elements(Wpf + "Button").First();
        Assert.Equal("{StaticResource Button.Icon}", (string?)back.Attribute("Style"));
        Assert.Equal("{Binding BackToNoteCommand}", (string?)back.Attribute("Command"));
        Assert.Equal("Alt+1", (string?)back.Attribute("AutomationProperties.AcceleratorKey"));
        var close = toolbar.Elements(Wpf + "Button").Last();
        Assert.Equal("{StaticResource Button.Icon}", (string?)close.Attribute("Style"));
        Assert.Equal("{StaticResource Icon.Close}", (string?)close.Attribute("Content"));
        Assert.Equal("Esc", (string?)close.Attribute("AutomationProperties.AcceleratorKey"));
        var label = toolbar.Elements(Wpf + "TextBlock").Single();
        Assert.Equal("{StaticResource Text.BodyStrong}", (string?)label.Attribute("Style"));

        var frame = grid.Elements(Wpf + "Border").Single(b => (string?)b.Attribute("Grid.Row") == "1");
        Assert.Equal("{DynamicResource Brush.border.subtle}", (string?)frame.Attribute("BorderBrush"));
        Assert.Contains(frame.Descendants(Wpf + "Image"), i => (string?)i.Attribute(X + "Name") == "EnlargedImage");
        var hint = grid.Elements(Wpf + "TextBlock").Single(t => (string?)t.Attribute("Grid.Row") == "2");
        Assert.Equal("{Binding IsChecked, ElementName=BlurMode, Converter={StaticResource Visible}}", (string?)hint.Attribute("Visibility"));
        Assert.Empty(grid.Elements(Wpf + "WrapPanel"));

        var review = EnvelopeLayoutTests.Load("Review", "ReviewPane.xaml");
        var paneBack = review.Descendants(Wpf + "Button").Single(b => (string?)b.Attribute(X + "Name") == "BackToNote");
        var collapses = paneBack.Descendants(Wpf + "DataTrigger").Single();
        Assert.Equal("{Binding Strip.IsEnlarged}", (string?)collapses.Attribute("Binding"));
    }

    /// <summary>
    /// Review finding A-27: the strip sits 16 in, on the scale, with no negative margin to cancel its items';
    /// an excluded frame is not dimmed but carries an "Excluded" badge with Icon.Hide; the gap tile is a flat
    /// bg.info fill with a dashed border.strong edge and no gradient; the key hint is a Caption.
    /// </summary>
    [Fact]
    public void TheStripIsFlatOnTheGridWithNoOpacityStates()
    {
        var strip = EnvelopeLayoutTests.Load("Review", "FilmstripView.xaml");
        var root = strip.Elements(Wpf + "Grid").Single();
        Assert.Equal("16,16,16,0", (string?)root.Attribute("Margin"));
        var list = strip.Descendants(Wpf + "ListBox").Single(l => (string?)l.Attribute(X + "Name") == "Strip");
        Assert.Equal("0,8,0,0", (string?)list.Attribute("Margin"));

        var frame = strip.Descendants(Wpf + "DataTemplate").Single(t => (string?)t.Attribute(X + "Key") == "FrameTemplate");
        Assert.DoesNotContain(frame.Descendants(Wpf + "Setter"), s => (string?)s.Attribute("Property") == "Opacity");
        var badge = frame.Descendants(Wpf + "ContentControl").Single(c => (string?)c.Attribute(X + "Name") == "ExcludedBadge");
        Assert.Equal("Excluded", (string?)badge.Attribute("Content"));
        Assert.Equal("{StaticResource Icon.Hide}", (string?)badge.Attribute(XName.Get("Badge.Glyph", "clr-namespace:ScreenTrail.UI.Theme")));
        Assert.Equal("8", (string?)badge.Attribute("Margin"));

        var gap = strip.Descendants(Wpf + "DataTemplate").Single(t => (string?)t.Attribute(X + "Key") == "GapTemplate");
        Assert.DoesNotContain(gap.Descendants(), e => e.Name.LocalName.Contains("Gradient", StringComparison.Ordinal) || e.Name.LocalName.EndsWith("OpacityMask", StringComparison.Ordinal));
        var tile = gap.Descendants(Wpf + "Rectangle").Single();
        Assert.Equal("{DynamicResource Brush.bg.info}", (string?)tile.Attribute("Fill"));
        Assert.Equal("{DynamicResource Brush.border.strong}", (string?)tile.Attribute("Stroke"));
        Assert.Equal("4 3", (string?)tile.Attribute("StrokeDashArray"));

        var hint = strip.Descendants(Wpf + "TextBlock").Single(t => ((string?)t.Attribute("Text"))?.StartsWith("Space include", StringComparison.Ordinal) == true);
        Assert.Equal("{StaticResource Text.Caption}", (string?)hint.Attribute("Style"));
        Assert.Null(hint.Attribute("FontSize"));

        var renders = File.ReadAllText(Path.Combine(EnvelopeLayoutTests.Ui(), "Shell", "ShellWindow.Renders.cs"));
        Assert.Contains("\"shell-review-strip\"", renders, StringComparison.Ordinal);
    }

    /// <summary>
    /// Review finding D-21: the enlarged view draws a decoded, frozen ImageSource (the thumbnail until the
    /// full picture is in), never bytes WPF would decode at native size on the UI thread; the picture and its
    /// text are fetched together; and every screenshot is scaled with the high-quality filter.
    /// </summary>
    [Fact]
    public void AnEnlargedFrameOpensAtOnceAndIsDecodedOffTheUiThread()
    {
        var strip = EnvelopeLayoutTests.Load("Review", "FilmstripView.xaml");
        var enlarged = strip.Descendants(Wpf + "Image").Single(i => (string?)i.Attribute(X + "Name") == "EnlargedImage");
        Assert.Equal("{Binding Enlarged.Showing}", (string?)enlarged.Attribute("Source"));
        Assert.All(strip.Descendants(Wpf + "Image"), i => Assert.Equal("HighQuality", (string?)i.Attribute("RenderOptions.BitmapScalingMode")));
        var note = EnvelopeLayoutTests.Load("Review", "NoteEditorView.xaml");
        Assert.All(note.Descendants(Wpf + "Image"), i => Assert.Equal("HighQuality", (string?)i.Attribute("RenderOptions.BitmapScalingMode")));

        var model = File.ReadAllText(Path.Combine(EnvelopeLayoutTests.Ui(), "Review", "FilmstripViewModel.cs"));
        Assert.Contains("Task.WhenAll(image, text)", model, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => Thumbnails.DecodeAt(image, width))", model, StringComparison.Ordinal);
        Assert.Contains("public ImageSource? Showing => BlurPending ? null : Picture ?? Thumbnail;", model, StringComparison.Ordinal);
        var decoder = File.ReadAllText(Path.Combine(EnvelopeLayoutTests.Ui(), "Review", "Thumbnails.cs"));
        Assert.Contains("decoded.Freeze();", decoder, StringComparison.Ordinal);
        Assert.Contains("DecodePixelWidth = width", decoder, StringComparison.Ordinal);
    }

    /// <summary>
    /// Review finding D-27: thumbnails are fetched four at a time and decoded on a worker thread, and a cell
    /// waiting for one shows a skeleton the picture fades in over, through Waiting.FadeIn (code, so reduce
    /// motion is honoured), not a storyboard.
    /// </summary>
    [Fact]
    public void ThumbnailsLoadFourAtATimeOverASkeleton()
    {
        var model = File.ReadAllText(Path.Combine(EnvelopeLayoutTests.Ui(), "Review", "FilmstripViewModel.cs"));
        Assert.Contains("internal const int ThumbnailsInFlight = 4;", model, StringComparison.Ordinal);
        Assert.Contains("new SemaphoreSlim(ThumbnailsInFlight)", model, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(() => Thumbnails.Decode(bytes), ct)", model, StringComparison.Ordinal);

        var strip = EnvelopeLayoutTests.Load("Review", "FilmstripView.xaml");
        var frame = strip.Descendants(Wpf + "DataTemplate").Single(t => (string?)t.Attribute(X + "Key") == "FrameTemplate");
        var skeleton = frame.Descendants(Wpf + "Border").Single(b => (string?)b.Attribute(X + "Name") == "ThumbnailSkeleton");
        Assert.Equal("{StaticResource Skeleton}", (string?)skeleton.Attribute("Style"));
        Assert.Equal("{DynamicResource Radius.control}", (string?)skeleton.Attribute("CornerRadius"));
        var image = frame.Descendants(Wpf + "Image").Single();
        Assert.Equal("{Binding Thumbnail, NotifyOnTargetUpdated=True}", (string?)image.Attribute("Source"));
        Assert.Equal("True", (string?)image.Attribute(XName.Get("Waiting.FadeIn", "clr-namespace:ScreenTrail.UI.Theme")));
        Assert.DoesNotContain(strip.Descendants(), e => e.Name.LocalName == "Storyboard");

        var waiting = File.ReadAllText(Path.Combine(EnvelopeLayoutTests.Ui(), "Theme", "Waiting.cs"));
        Assert.Contains("TimeSpan.FromMilliseconds(120)", waiting, StringComparison.Ordinal);
        Assert.Contains("ThemeManager.ReduceMotion", waiting, StringComparison.Ordinal);
    }
}
