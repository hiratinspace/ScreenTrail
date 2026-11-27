using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Animation;
using ScreenTrail.Core.Onboarding;
using ScreenTrail.Core.Settings;
using ScreenTrail.Core.Shell;
using ScreenTrail.UI.Theme;

namespace ScreenTrail.UI.Onboarding;

/// <summary>Collapses an element whose bound value is null: the Fix button beside a check that has no deep link.</summary>
public sealed class NullToCollapsed : IValueConverter
{
    public static NullToCollapsed Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// The onboarding wizard's window (ST-083). Layout only; every rule is <see cref="OnboardingWizard"/>'s.
/// Runs at first start and again from the tray.
/// </summary>
public partial class OnboardingWindow : Window
{
    public OnboardingWindow(OnboardingViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        InitializeComponent();
        DataContext = model;
        model.Closed += Close;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OnboardingViewModel.Step))
            {
                ShowNewStep();
            }
        };
        Loaded += async (_, _) => await model.LoadAsync();
    }

    /// <summary>
    /// Review finding D-30: the steps share one scroll view, so a step scrolled down at the minimum size
    /// handed its offset to the next, which opened part way down, and the steps swapped with no transition.
    /// A new step opens at its top and fades in over motion.state (design system 9.2, pane switch); with
    /// reduce motion on, or in a render, it is there at once.
    /// </summary>
    private void ShowNewStep()
    {
        StepScroller.ScrollToTop();
        if (Waiting.AtOnce || ThemeManager.ReduceMotion)
        {
            StepContent.BeginAnimation(OpacityProperty, null);
            StepContent.Opacity = 1;
            return;
        }

        StepContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, ThemeManager.Motion(new Duration(Waiting.Fade))));
    }

    /// <summary>The live wizard over the pipe, wired to Settings' own panels and the shell's preferences.</summary>
    public static OnboardingWindow ForConnection(CaptureConnection connection, ShellPreferencesStore preferences, Action opened)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(preferences);
        var wizard = new OnboardingWizard(
            new ActivationPanel(new PipeDevice(connection), Environment.MachineName),
            new PipeCapabilities(connection),
            new CapturePanel(new PipeCapture(connection)),
            new IntegrationsPanel(new PipeIntegrations(connection)),
            new PipeTestSession(connection),
            at => _ = preferences.Save(preferences.Load() with { OnboardedAt = at }));
        return new OnboardingWindow(new OnboardingViewModel(wizard, opened));
    }
}
