using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using ScreenTrail.Core.Review;

namespace ScreenTrail.UI.Review.Timeline;

/// <summary>
/// The scrubber (ST-076). Layout only: which markers exist and where they sit are
/// <see cref="TimelineViewModel"/>'s, from <see cref="SessionTimeline"/>; this turns fractions into
/// pixels each time the track is measured, and a click on a frame square into the model's command.
///
/// The markers' colours are resource references, not brushes looked up once: drawn with the brush of the
/// palette at the time, they kept the dark colours when Windows switched to high contrast (ST-084).
/// </summary>
public partial class TimelineView : UserControl
{
    // The track is 28 high, the height of a frame marker's button; everything sits on the baseline at 14.
    private const double Baseline = 14;

    public TimelineView()
    {
        InitializeComponent();
        Loaded += (_, _) => Draw();
        DataContextChanged += OnDataContextChanged;
        Track.SizeChanged += (_, _) => Draw();
    }

    private TimelineViewModel? Model => DataContext as TimelineViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is TimelineViewModel old)
        {
            old.PropertyChanged -= OnModelChanged;
        }

        if (e.NewValue is TimelineViewModel model)
        {
            model.PropertyChanged += OnModelChanged;
        }

        Draw();
    }

    // The selected frame's marker turns Iris; nothing else on the track moves, so only the tags change.
    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineViewModel.SelectedFrameId) && Model is { } model)
        {
            foreach (var marker in Track.Children.OfType<Button>())
            {
                marker.Tag = Equals(marker.CommandParameter, model.SelectedFrameId) ? "Selected" : null;
            }
        }
    }

    private void Draw()
    {
        Track.Children.Clear();
        var width = Track.ActualWidth;
        if (Model is not { } model || width <= 0)
        {
            return;
        }

        // The baseline (review finding A-21): a 1 px border.subtle line the markers sit on, so they read as
        // points in time on one track rather than specks.
        var line = new Rectangle { Width = width, Height = 1, IsHitTestVisible = false };
        line.SetResourceReference(Shape.FillProperty, "Brush.border.subtle");
        Canvas.SetTop(line, Baseline);
        Track.Children.Add(line);

        // Bands first, so points draw over them: a frame inside a scope band is still a frame. Flat fills
        // with a 1 px edge, no opacity: bg.warning for a pause, bg.info for time out of scope.
        foreach (var marker in model.Markers.Where(m => m.IsBand))
        {
            var scope = marker.Kind == TimelineMarkerKind.OutOfScope;
            var band = new Rectangle
            {
                Width = Math.Max(2, marker.Span * width),
                Height = 12,
                RadiusX = 2,
                RadiusY = 2,
                StrokeThickness = 1,
                ToolTip = marker.Tooltip,
            };
            band.SetResourceReference(Shape.FillProperty, scope ? "Brush.bg.info" : "Brush.bg.warning");
            band.SetResourceReference(Shape.StrokeProperty, scope ? "Brush.border.subtle" : "Brush.border.warning");
            Canvas.SetLeft(band, marker.Position * width);
            Canvas.SetTop(band, Baseline - 6);
            Track.Children.Add(band);
        }

        foreach (var marker in model.Markers.Where(m => !m.IsBand))
        {
            var x = marker.Position * width;
            FrameworkElement shape = marker.Kind switch
            {
                TimelineMarkerKind.Frame => Frame(x, marker, model),
                TimelineMarkerKind.Marked => Flag(x),
                TimelineMarkerKind.Narration => Tick(x),
                _ => Dot(x),
            };
            shape.ToolTip = marker.Tooltip;
            Track.Children.Add(shape);
        }
    }

    /// <summary>
    /// A screenshot (review finding E-23): a 28 x 28 button centred on its 8 px mark, named for when it was
    /// taken, so it meets the hit-target rule and a keyboard and a screen reader reach it as the pointer
    /// does. Enter, Space or a click is the jump a transcript line makes.
    /// </summary>
    private Button Frame(double x, MarkerItem marker, TimelineViewModel model)
    {
        var button = new Button
        {
            Style = (Style)FindResource("Timeline.FrameMarker"),
            Command = model.JumpToCommand,
            CommandParameter = marker.FrameId,
            Tag = marker.FrameId is not null && marker.FrameId == model.SelectedFrameId ? "Selected" : null,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, marker.Name);
        var half = (double)FindResource("Layout.hit-target-min") / 2;
        Canvas.SetLeft(button, x - half);
        Canvas.SetTop(button, Baseline - half);
        return button;
    }

    // A marked moment: Icon.Flag at 12 in text.secondary, standing on the baseline. A Glyph, so a screen
    // reader is not handed the private-use character; the tooltip says what it is.
    private static Controls.Glyph Flag(double x)
    {
        var flag = new Controls.Glyph();
        flag.SetResourceReference(TextBlock.TextProperty, "Icon.Flag");
        flag.SetResourceReference(TextBlock.FontFamilyProperty, "Font.icon");
        flag.SetResourceReference(TextBlock.FontSizeProperty, "Icon.sm");
        flag.SetResourceReference(TextBlock.ForegroundProperty, "Brush.text.secondary");
        Canvas.SetLeft(flag, x - 6);
        Canvas.SetTop(flag, Baseline - 12);
        return flag;
    }

    // Something said with no screenshot near it: a 1 x 8 line in text.muted, centred on the baseline.
    private static Rectangle Tick(double x)
    {
        var tick = new Rectangle { Width = 1, Height = 8 };
        tick.SetResourceReference(Shape.FillProperty, "Brush.text.muted");
        Canvas.SetLeft(tick, x - 0.5);
        Canvas.SetTop(tick, Baseline - 4);
        return tick;
    }

    private static Ellipse Dot(double x)
    {
        var dot = new Ellipse { Width = 4, Height = 4 };
        dot.SetResourceReference(Shape.FillProperty, "Brush.text.muted");
        Canvas.SetLeft(dot, x - 2);
        Canvas.SetTop(dot, Baseline - 1.5);
        return dot;
    }
}
