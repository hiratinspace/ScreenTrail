using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ScreenTrail.UI.Theme;

/// <summary>
/// The motion helper (review finding D-32, design system 9.4): what DS 9.2 says should move, as attached
/// properties that build their animation in code, so no style or template declares a storyboard that
/// would run whatever the reduce motion setting said.
///
/// <see cref="FadeInProperty"/>: opacity 0 to 1 over motion.state (120 ms, linear) each time the element
/// becomes visible, for banners and bars that appear (DS 9.2 "Banner or Save bar appearing").
/// <see cref="EnterProperty"/>: the same fade with a rise from the given offset over motion.pane (180 ms,
/// ease-enter), for toasts (DS 9.2 "Toast in").
///
/// Nothing moves under reduce motion (<see cref="ThemeManager.ReduceMotion"/>, DS 9.5) or in a render
/// (<see cref="Waiting.AtOnce"/>), and the element is simply shown.
/// </summary>
public static class Motion
{
    public static readonly DependencyProperty FadeInProperty = DependencyProperty.RegisterAttached(
        "FadeIn", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnChanged));

    public static bool GetFadeIn(UIElement element) => (bool)element.GetValue(FadeInProperty);

    public static void SetFadeIn(UIElement element, bool value) => element.SetValue(FadeInProperty, value);

    /// <summary>The rise, in pixels, the element enters from: positive rises from below. Zero is off.</summary>
    public static readonly DependencyProperty EnterProperty = DependencyProperty.RegisterAttached(
        "Enter", typeof(double), typeof(Motion), new PropertyMetadata(0d, OnChanged));

    public static double GetEnter(UIElement element) => (double)element.GetValue(EnterProperty);

    public static void SetEnter(UIElement element, double value) => element.SetValue(EnterProperty, value);

    /// <summary>Whether anything may move now: reduce motion off and not a render.</summary>
    public static bool Moves => !ThemeManager.ReduceMotion && !Waiting.AtOnce;

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.IsVisibleChanged -= OnVisibleChanged;
        if (GetFadeIn(element) || GetEnter(element) != 0)
        {
            element.IsVisibleChanged += OnVisibleChanged;
        }
    }

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element || e.NewValue is not true)
        {
            return;
        }

        if (!Moves)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            return;
        }

        var rise = GetEnter(element);
        var duration = Duration(element, rise != 0 ? "Motion.pane" : "Motion.state");
        var spline = rise != 0 && element.TryFindResource("Motion.ease-enter") is KeySpline ease ? ease : null;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration));
        if (rise != 0)
        {
            var move = element.RenderTransform as TranslateTransform;
            if (move is null || move.IsFrozen)
            {
                move = new TranslateTransform();
                element.RenderTransform = move;
            }

            var travel = new DoubleAnimationUsingKeyFrames { Duration = duration };
            travel.KeyFrames.Add(new DiscreteDoubleKeyFrame(rise, KeyTime.FromPercent(0)));
            travel.KeyFrames.Add(spline is null
                ? new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1))
                : new SplineDoubleKeyFrame(0, KeyTime.FromPercent(1), spline));
            move.BeginAnimation(TranslateTransform.YProperty, travel);
        }
    }

    private static Duration Duration(FrameworkElement element, string key) =>
        ThemeManager.Motion(element.TryFindResource(key) is Duration token ? token : new Duration(TimeSpan.FromMilliseconds(120)));
}
