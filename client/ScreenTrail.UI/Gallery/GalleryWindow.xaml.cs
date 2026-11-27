using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenTrail.UI.Render;
using ScreenTrail.UI.Theme;

namespace ScreenTrail.UI.Gallery;

/// <summary>
/// ST-016 gallery, the component reference since review finding B-56. <c>ScreenTrail.UI.exe --gallery</c>
/// opens it; <c>--gallery --screenshot &lt;dir&gt;</c> renders every theme to PNG and exits, which is how
/// CI produces the screenshots. Its state matrix is <see cref="GalleryCatalogue"/>, built here: hover,
/// pressed and focus are forced on with <see cref="ForcedState"/>, and the focus ring is drawn the way
/// the shell's focus renders draw it.
/// </summary>
public partial class GalleryWindow : Window
{
    private readonly string? _screenshotDirectory;
    private readonly List<(FrameworkElement Target, GalleryState State)> _forced = [];
    private readonly List<FrameworkElement> _focused = [];

    public GalleryWindow(string? screenshotDirectory = null)
    {
        _screenshotDirectory = screenshotDirectory;
        InitializeComponent();
        _ = WindowRenderer.Intend(this);
        BuildMatrix();
        BuildTones();
        SampleTable.ItemsSource = new[]
        {
            new SessionRow("Draft", "14 min ago", "12 min", "ScreenConnect", "Not published"),
            new SessionRow("Published", "3 h ago", "21 min", "ScreenConnect", "Acme Dental · #48190"),
            new SessionRow("Partial", "Yesterday", "6 min", "Remote Desktop", "Not published"),
        };
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        foreach (var (target, state) in _forced)
        {
            ForcedState.Apply(PartFor(target, state), state);
        }

        if (_screenshotDirectory is null)
        {
            DrawToolTip();
            _ = Rings();
            return;
        }

        Directory.CreateDirectory(_screenshotDirectory);
        ContrastThemeFiles.Report();

        // The progress ring and the skeleton appear only after 300 ms of waiting (design system 10.8).
        await Task.Delay(400).ConfigureAwait(true);
        foreach (var theme in RenderThemes.All)
        {
            RenderThemeApplier.Apply(theme, Application.Current.Resources);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
            DrawToolTip();
            UpdateLayout();
            WindowRenderer.Save(this, Path.Combine(_screenshotDirectory, RenderThemes.FileName("gallery", theme)), prepare: Rings);
        }

        BrandIconSheet.Save(_screenshotDirectory);
        RenderIndex.Write(_screenshotDirectory);
        Application.Current.Shutdown();
    }

    /// <summary>One row per style, one column per state; a state the style does not have is left empty.</summary>
    private void BuildMatrix()
    {
        var columns = GalleryCatalogue.Columns;
        var rows = GalleryCatalogue.Rows;
        Matrix.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Matrix.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c < columns.Count; c++)
        {
            Matrix.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Place(Heading(columns[c].ToString()), 0, c + 1);
        }

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            Matrix.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(Heading(row.Label), r + 1, 0);
            for (var c = 0; c < columns.Count; c++)
            {
                if (!row.States.Contains(columns[c]))
                {
                    continue;
                }

                var (sample, target) = Sample(row, $"Gallery{r}x{c}");
                Put(sample, target, row, columns[c]);
                Place(sample, r + 1, c + 1);
            }
        }
    }

    private void Place(FrameworkElement element, int row, int column)
    {
        element.Margin = new Thickness(0, 0, 16, 12);
        element.HorizontalAlignment = HorizontalAlignment.Left;
        element.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        _ = Matrix.Children.Add(element);
    }

    private TextBlock Heading(string text) =>
        new() { Text = text, Style = (Style)FindResource("Text.Label"), MinHeight = 32 };

    /// <summary>The control a row draws, and the part of it the state goes on.</summary>
    private (FrameworkElement Sample, FrameworkElement Target) Sample(GalleryRow row, string group)
    {
        var style = row.StyleKey is null ? null : (Style)FindResource(row.StyleKey);
        switch (row.Sample)
        {
            case GallerySample.Button:
                {
                    var button = new Button { Content = row.Text, Style = style };
                    return (button, button);
                }

            case GallerySample.IconButton:
                {
                    var button = new Button { Content = FindResource("Icon.Settings"), Style = style, ToolTip = row.Text };
                    AutomationProperties.SetName(button, row.Text);
                    return (button, button);
                }

            case GallerySample.Input or GallerySample.Placeholder:
                {
                    var box = new TextBox { Style = style };
                    box.SetResourceReference(WidthProperty, "Layout.field");
                    if (row.Sample == GallerySample.Input)
                    {
                        box.Text = row.Text;
                    }
                    else
                    {
                        Field.SetPlaceholder(box, row.Text);
                    }

                    AutomationProperties.SetName(box, row.Label);
                    return (box, box);
                }

            case GallerySample.ComboBox:
                {
                    var combo = new ComboBox { ItemsSource = new[] { "Low", "Medium", "High" }, SelectedItem = row.Text };
                    combo.SetResourceReference(WidthProperty, "Layout.field-narrow");
                    AutomationProperties.SetName(combo, row.Label);
                    return (combo, combo);
                }

            case GallerySample.CheckBox or GallerySample.CheckChip:
                {
                    var box = new CheckBox { Content = row.Text, IsChecked = row.IsChecked };
                    if (style is not null)
                    {
                        box.Style = style;
                    }

                    return (box, box);
                }

            case GallerySample.RadioButton:
                {
                    var radio = new RadioButton { Content = row.Text, IsChecked = row.IsChecked, GroupName = group };
                    return (radio, radio);
                }

            case GallerySample.Segment:
                {
                    // The state goes on the segment that is not chosen, so its hover and press show.
                    var chosen = new RadioButton { Content = "Internal", IsChecked = true, GroupName = group, Style = style };
                    var other = new RadioButton { Content = row.Text, GroupName = group, Style = style };
                    var panel = new StackPanel { Orientation = Orientation.Horizontal };
                    _ = panel.Children.Add(chosen);
                    _ = panel.Children.Add(other);
                    return (new Border { Style = (Style)FindResource("Segment.Group"), Child = panel }, other);
                }

            default:
                {
                    var slider = new Slider { Minimum = 1, Maximum = 30, Value = 14 };
                    slider.SetResourceReference(WidthProperty, "Layout.field-narrow");
                    AutomationProperties.SetName(slider, row.Label);
                    return (slider, slider);
                }
        }
    }

    private void Put(FrameworkElement sample, FrameworkElement target, GalleryRow row, GalleryState state)
    {
        switch (state)
        {
            case GalleryState.Disabled:
                sample.IsEnabled = false;
                break;
            case GalleryState.Loading:
                Busy.SetActive(target, true);
                break;
            case GalleryState.Error:
                Field.SetError(target, row.StyleKey == "Input.Mono" ? "That invite code has expired." : "Already used by Microsoft Teams.");
                break;
            case GalleryState.Hover or GalleryState.Pressed:
                _forced.Add((target, state));
                break;
            case GalleryState.Focused:
                _forced.Add((target, state));
                _focused.Add(target);
                break;
            default:
                break;
        }
    }

    /// <summary>A slider's hover is its thumb's: the pointer is over the thumb, not the track.</summary>
    private static FrameworkElement PartFor(FrameworkElement target, GalleryState state)
    {
        if (target is Slider slider && state != GalleryState.Focused)
        {
            _ = slider.ApplyTemplate();
            if (slider.Template?.FindName("PART_Track", slider) is Track { Thumb: { } thumb })
            {
                return thumb;
            }
        }

        return target;
    }

    private void BuildTones()
    {
        foreach (var (tone, words, glyph) in GalleryCatalogue.Badges)
        {
            var badge = new ContentControl { Content = words, Style = (Style)FindResource("Badge"), Margin = new Thickness(0, 0, 8, 0) };
            Badge.SetTone(badge, tone);
            if (glyph is not null)
            {
                Badge.SetGlyph(badge, (string)FindResource(glyph));
            }

            _ = Badges.Children.Add(badge);
        }

        foreach (var (tone, title, words) in GalleryCatalogue.Banners)
        {
            var banner = new ContentControl
            {
                Style = (Style)FindResource("Banner"),
                Margin = new Thickness(0, 0, 0, 8),
                Content = new TextBlock { Text = words, TextWrapping = TextWrapping.Wrap, Style = (Style)FindResource("Text.Body") },
            };
            Banner.SetTone(banner, tone);
            if (title is not null)
            {
                Banner.SetTitle(banner, title);
            }

            _ = Banners.Children.Add(banner);
        }
    }

    /// <summary>
    /// The tooltip in the current theme. A tooltip is drawn in a popup, which a window render never sees,
    /// so it is laid out on its own, drawn to a bitmap and shown in the Feedback row.
    /// </summary>
    private void DrawToolTip()
    {
        var tip = new ToolTip { Content = "Choose a ticket first", Style = (Style)FindResource(typeof(ToolTip)) };
        tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        tip.Arrange(new Rect(tip.DesiredSize));
        tip.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(tip.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(tip.ActualHeight * dpi.DpiScaleY)),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(tip);
        bitmap.Freeze();
        ToolTipSample.Source = bitmap;
    }

    /// <summary>The Focused column's rings, on whichever adorner layer each control is under now.</summary>
    private IDisposable? Rings()
    {
        var added = new List<(AdornerLayer Layer, Adorner Ring)>();
        foreach (var element in _focused)
        {
            if (element.FocusVisualStyle is { } style && AdornerLayer.GetAdornerLayer(element) is { } layer)
            {
                var ring = new FocusRingAdorner(element, style);
                layer.Add(ring);
                layer.UpdateLayout();
                added.Add((layer, ring));
            }
        }

        return new Removal(() => added.ForEach(a => a.Layer.Remove(a.Ring)));
    }

    private void OnTheme(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name } && Enum.TryParse<AppTheme>(name, out var theme))
        {
            ThemeManager.Apply(theme, Application.Current.Resources);
            DrawToolTip();
        }
    }

    private sealed class Removal(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    private sealed record SessionRow(string Status, string Started, string Duration, string Tool, string Client);
}
