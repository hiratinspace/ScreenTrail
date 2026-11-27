using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ScreenTrail.UI.Theme;

/// <summary>
/// The waiting marks (design system 10.8), started from code and not by a storyboard in XAML: a storyboard
/// declared in a style runs whatever Windows' animation setting says, and the motion audit forbids one
/// anywhere in the app (Spec §2.3, §7).
/// <list type="bullet">
/// <item><c>theme:Waiting.Appear</c>: shown, the element stays transparent for 300 ms and then is there in
/// one step, so a fast answer never flashes it. A delay is not motion, so reduce motion keeps it.</item>
/// <item><c>theme:Waiting.Spin</c>: shown, the element's <see cref="RotateTransform"/> turns once a second,
/// the one thing allowed to loop. With reduce motion on it stays still; the words beside it say what is
/// happening.</item>
/// <item><c>theme:Waiting.FadeIn</c>, on an Image whose Source binding has NotifyOnTargetUpdated: a picture
/// arriving fades in over 120 ms (motion.state) above the skeleton it replaces, rather than snapping in
/// (review finding D-27). With reduce motion on, or in a render, it is there at once.</item>
/// </list>
/// </summary>
internal static class Waiting
{
    /// <summary>How long a wait is before its mark shows.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(300);

    private static readonly TimeSpan Turn = TimeSpan.FromSeconds(1);

    /// <summary>A picture's fade in over its skeleton: motion.state.</summary>
    internal static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(120);

    /// <summary>
    /// Shows a waiting mark at once rather than after <see cref="Delay"/>. The render harness turns it on:
    /// a picture taken within 300 ms of a pane appearing showed its skeletons missing (the draft-pending
    /// note rendered nearly blank and failed CI's drawn-something check), and a render is meant to show
    /// the state as a person sees it once it has settled.
    /// </summary>
    internal static bool AtOnce { get; set; }

    /// <summary>Finishes every pending <c>Appear</c> under <paramref name="root"/> now, for the render harness.</summary>
    internal static void ShowNow(DependencyObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root is UIElement element && element.GetValue(TimerProperty) is DispatcherTimer running)
        {
            running.Stop();
            element.ClearValue(TimerProperty);
            element.Opacity = 1;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            ShowNow(VisualTreeHelper.GetChild(root, i));
        }
    }

    public static readonly DependencyProperty AppearProperty = DependencyProperty.RegisterAttached(
        "Appear", typeof(bool), typeof(Waiting), new FrameworkPropertyMetadata(false, OnAppearChanged));

    public static readonly DependencyProperty SpinProperty = DependencyProperty.RegisterAttached(
        "Spin", typeof(bool), typeof(Waiting), new FrameworkPropertyMetadata(false, OnSpinChanged));

    public static readonly DependencyProperty FadeInProperty = DependencyProperty.RegisterAttached(
        "FadeIn", typeof(bool), typeof(Waiting), new FrameworkPropertyMetadata(false, OnFadeInChanged));

    public static bool GetFadeIn(DependencyObject element) => (bool)element.GetValue(FadeInProperty);

    public static void SetFadeIn(DependencyObject element, bool value) => element.SetValue(FadeInProperty, value);

    private static void OnFadeInChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.TargetUpdated -= OnFadeTarget;
        if (e.NewValue is true)
        {
            element.TargetUpdated += OnFadeTarget;
        }
    }

    private static void OnFadeTarget(object? sender, System.Windows.Data.DataTransferEventArgs e)
    {
        if (sender is not System.Windows.Controls.Image image || e.Property != System.Windows.Controls.Image.SourceProperty)
        {
            return;
        }

        if (image.Source is null || AtOnce || ThemeManager.ReduceMotion)
        {
            image.BeginAnimation(UIElement.OpacityProperty, null);
            image.Opacity = 1;
            return;
        }

        image.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, ThemeManager.Motion(new Duration(Fade))));
    }

    private static readonly DependencyProperty TimerProperty = DependencyProperty.RegisterAttached(
        "Timer", typeof(DispatcherTimer), typeof(Waiting), new FrameworkPropertyMetadata(null));

    public static bool GetAppear(DependencyObject element) => (bool)element.GetValue(AppearProperty);

    public static void SetAppear(DependencyObject element, bool value) => element.SetValue(AppearProperty, value);

    public static bool GetSpin(DependencyObject element) => (bool)element.GetValue(SpinProperty);

    public static void SetSpin(DependencyObject element, bool value) => element.SetValue(SpinProperty, value);

    private static void OnAppearChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.IsVisibleChanged -= OnAppearVisibility;
        if (e.NewValue is true)
        {
            element.IsVisibleChanged += OnAppearVisibility;
            Appear(element);
        }
    }

    private static void OnAppearVisibility(object sender, DependencyPropertyChangedEventArgs e) => Appear((UIElement)sender);

    private static void Appear(UIElement element)
    {
        if (element.GetValue(TimerProperty) is DispatcherTimer running)
        {
            running.Stop();
            element.ClearValue(TimerProperty);
        }

        if (AtOnce)
        {
            element.Opacity = 1;
            return;
        }

        element.Opacity = 0;
        if (!element.IsVisible)
        {
            return;
        }

        var timer = new DispatcherTimer(DispatcherPriority.Normal, element.Dispatcher) { Interval = Delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            element.ClearValue(TimerProperty);
            element.Opacity = 1;
        };
        element.SetValue(TimerProperty, timer);
        timer.Start();
    }

    private static void OnSpinChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.IsVisibleChanged -= OnSpinVisibility;
        if (e.NewValue is true)
        {
            element.IsVisibleChanged += OnSpinVisibility;
            Spin(element);
        }
    }

    private static void OnSpinVisibility(object sender, DependencyPropertyChangedEventArgs e) => Spin((UIElement)sender);

    private static void Spin(UIElement element)
    {
        if (element.RenderTransform is not RotateTransform rotate || rotate.IsFrozen)
        {
            return;
        }

        if (!element.IsVisible || ThemeManager.ReduceMotion)
        {
            rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            rotate.Angle = 0;
            return;
        }

        rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, ThemeManager.Motion(new Duration(Turn)))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        });
    }
}
