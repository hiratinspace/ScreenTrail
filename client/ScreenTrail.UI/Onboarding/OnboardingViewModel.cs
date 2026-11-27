using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenTrail.Core.Onboarding;
using ScreenTrail.Core.Shell;
using ScreenTrail.UI.Settings.Activation;
using ScreenTrail.UI.Settings.Integrations;

namespace ScreenTrail.UI.Onboarding;

/// <summary>One remote tool with its toggle, in the "Your remote tools" step.</summary>
public sealed partial class WizardToolViewModel(string id, string displayName, bool enabled, Action<string, bool> changed) : ObservableObject
{
    public string DisplayName => displayName;

    [ObservableProperty]
    public partial bool Enabled { get; set; } = enabled;

    partial void OnEnabledChanged(bool value) => changed(id, value);
}

/// <summary>
/// The onboarding wizard (ST-083, Spec §5 S8), bound over <see cref="OnboardingWizard"/>, which holds
/// every rule. The Activate and Connect steps reuse Settings' own view models; Permissions, Tools and
/// Try it are the wizard's.
/// </summary>
public sealed partial class OnboardingViewModel : ObservableObject
{
    private readonly OnboardingWizard _wizard;
    private readonly Action _opened;

    /// <param name="opened">What Finish does after the wizard closes: open the shell on Review.</param>
    public OnboardingViewModel(OnboardingWizard wizard, Action opened)
    {
        _wizard = wizard ?? throw new ArgumentNullException(nameof(wizard));
        _opened = opened ?? throw new ArgumentNullException(nameof(opened));
        Activation = new ActivationViewModel(wizard.Activation);
        Integrations = new IntegrationsViewModel(wizard.Integrations);

        // The activation card is Settings' own view model and acts on the wizard's panel by itself, so
        // nothing here ran after it activated and Continue stayed off (2026-10-12 review, T-166). Its
        // activated state is what the step's blocker reads, so a change to it is a reason to refresh.
        // The card belongs to this view model, so the handler lives exactly as long as both.
        Activation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ActivationViewModel.ActivatedVisibility))
            {
                Refresh();
            }
        };
        Refresh();
    }

    public event Action? Closed;

    public ActivationViewModel Activation { get; }

    public IntegrationsViewModel Integrations { get; }

    /// <summary>
    /// The step list on the frame (Spec §5 S8, as built v0.6.7): every step by name, the ones behind
    /// ticked, the one in hand marked. Progress to read, not navigation.
    /// </summary>
    public ObservableCollection<SetupStepRow> Steps { get; } = [];

    public ObservableCollection<PermissionRow> Permissions { get; } = [];

    public ObservableCollection<WizardToolViewModel> Tools { get; } = [];

    [ObservableProperty]
    public partial OnboardingStep Step { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>"Step 3 of 7": where the technician is, in words, over the step's title (ST-084).</summary>
    [ObservableProperty]
    public partial string Progress { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Blocker { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Visibility BlockerVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Visibility NoteVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial bool CanContinue { get; set; }

    [ObservableProperty]
    public partial bool CanGoBack { get; set; }

    /// <summary>Review finding C-O7: Back is not there where it cannot act (Welcome, Done), rather than a dead control.</summary>
    [ObservableProperty]
    public partial Visibility BackVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility SkipVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility ContinueVisibility { get; set; } = Visibility.Visible;

    [ObservableProperty]
    public partial Visibility FinishVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial bool TestRunning { get; set; }

    [ObservableProperty]
    public partial bool CanStartTest { get; set; }

    /// <summary>
    /// Review finding C-O6: one filled button per state of the test. Before it, Start test session; while
    /// it runs, Stop and draft, with the recording dot, "Recording" and the timer as the pill shows them;
    /// after it, the outcome with its check and "Run the test again" as a Secondary, so Continue is the one
    /// filled button left. Start used to stay filled beside a filled Continue.
    /// </summary>
    [ObservableProperty]
    public partial Visibility StartTestVisibility { get; set; } = Visibility.Visible;

    [ObservableProperty]
    public partial Visibility StopTestVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility AgainTestVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility TestRunningVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility TestDoneVisibility { get; set; } = Visibility.Collapsed;

    /// <summary>The running test's time, "0:07", as the pill says it; ticks each second while it runs.</summary>
    [ObservableProperty]
    public partial string TestElapsed { get; set; } = string.Empty;

    /// <summary>What the card says once the test has stopped.</summary>
    public static string TestDrafting => OnboardingWizard.TestDrafting;

    /// <summary>Done's line on the tray icon and the overflow (review finding C-T8).</summary>
    public static string TrayHint => OnboardingWizard.TrayHint;

    /// <summary>The Settings page the tray line's button opens, through <see cref="OpenFixCommand"/>'s allowlist.</summary>
    public static string TaskbarSettings => OnboardingWizard.TaskbarSettings;

    private System.Windows.Threading.DispatcherTimer? _testClock;

    private System.Windows.Threading.DispatcherTimer? _serviceCheck;

    /// <summary>The ring beside the blocker while the Permissions step waits on the service (review finding C-I4).</summary>
    [ObservableProperty]
    public partial Visibility WaitingVisibility { get; set; } = Visibility.Collapsed;

    /// <summary>
    /// Review finding C-I4: while Permissions waits on a service that is starting, it checks again every two
    /// seconds, so the step turns green when the service answers without a click on Check again.
    /// </summary>
    private void FollowTheService()
    {
        WaitingVisibility = _wizard.WaitingOnService ? Visibility.Visible : Visibility.Collapsed;
        if (_wizard.WaitingOnService)
        {
            _serviceCheck ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromSeconds(2), System.Windows.Threading.DispatcherPriority.Background, async (_, _) => await CheckServiceAgainAsync().ConfigureAwait(true), System.Windows.Threading.Dispatcher.CurrentDispatcher);
            _serviceCheck.Start();
        }
        else
        {
            _serviceCheck?.Stop();
        }
    }

    private bool _checkingService;

    private async Task CheckServiceAgainAsync()
    {
        if (_checkingService || !_wizard.WaitingOnService)
        {
            return;
        }

        _checkingService = true;
        try
        {
            await _wizard.RecheckAsync().ConfigureAwait(true);
        }
        finally
        {
            _checkingService = false;
        }

        Refresh();
    }

    private void FollowTheTest()
    {
        var test = _wizard.Test;
        StartTestVisibility = test == TestState.NotStarted ? Visibility.Visible : Visibility.Collapsed;
        StopTestVisibility = test == TestState.Running ? Visibility.Visible : Visibility.Collapsed;
        AgainTestVisibility = test == TestState.Done ? Visibility.Visible : Visibility.Collapsed;
        TestRunningVisibility = StopTestVisibility;
        TestDoneVisibility = AgainTestVisibility;
        TestElapsed = _wizard.TestElapsed;
        if (test == TestState.Running)
        {
            _testClock ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromSeconds(1), System.Windows.Threading.DispatcherPriority.Background, (_, _) => TestElapsed = _wizard.TestElapsed, System.Windows.Threading.Dispatcher.CurrentDispatcher);
            _testClock.Start();
        }
        else
        {
            _testClock?.Stop();
        }
    }

    public Visibility WelcomeVisibility => Step == OnboardingStep.Welcome ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ActivateVisibility => Step == OnboardingStep.Activate ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PermissionsVisibility => Step == OnboardingStep.Permissions ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ToolsVisibility => Step == OnboardingStep.Tools ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ConnectVisibility => Step == OnboardingStep.Connect ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TryItVisibility => Step == OnboardingStep.TryIt ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DoneVisibility => Step == OnboardingStep.Done ? Visibility.Visible : Visibility.Collapsed;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _wizard.LoadStepAsync(ct).ConfigureAwait(true);
        if (_wizard.Step == OnboardingStep.Activate)
        {
            await Activation.LoadAsync(ct).ConfigureAwait(true);
        }

        if (_wizard.Step == OnboardingStep.Connect)
        {
            await Integrations.LoadAsync(ct).ConfigureAwait(true);
        }

        Refresh();
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        // On Remote tools, Continue saves the choices; a failed save stays on the step and says why.
        if (await _wizard.ContinueAsync().ConfigureAwait(true))
        {
            await LoadAsync().ConfigureAwait(true);
        }
        else
        {
            Refresh();
        }
    }

    [RelayCommand]
    private async Task BackAsync()
    {
        _wizard.Back();
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SkipAsync()
    {
        _wizard.Skip();
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RecheckAsync()
    {
        await _wizard.RecheckAsync().ConfigureAwait(true);
        Refresh();
    }

    /// <summary>The Windows Settings deep link beside a blocked check, e.g. <c>ms-settings:privacy-microphone</c>.</summary>
    [RelayCommand]
    private static void OpenFix(string? link)
    {
        if (ExternalLinks.TryFixLink(link, out var target))
        {
            _ = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
    }

    [RelayCommand]
    private async Task StartTestAsync()
    {
        _ = await _wizard.StartTestAsync().ConfigureAwait(true);
        Refresh();
    }

    [RelayCommand]
    private async Task StopTestAsync()
    {
        _ = await _wizard.StopTestAsync().ConfigureAwait(true);
        Refresh();
    }

    [RelayCommand]
    private void Finish()
    {
        _wizard.Finish();
        Closed?.Invoke();
        _opened();
    }

    /// <summary>Refreshed after a child panel acted, so the step's Continue follows the activation's outcome.</summary>
    public void Refresh()
    {
        Step = _wizard.Step;
        Title = Step switch
        {
            OnboardingStep.Welcome => "Welcome to ScreenTrail",
            OnboardingStep.Activate => "Activate this PC",
            // Each title starts with its name in the step list (review finding E-17).
            OnboardingStep.Permissions => "Permissions: what this PC allows",
            OnboardingStep.Tools => "Your remote tools",
            OnboardingStep.Connect => "Connect your PSA and Hudu",
            OnboardingStep.TryIt => "Try it",
            _ => "Done: you're set",
        };
        Progress = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Step {_wizard.StepNumber} of {_wizard.StepCount}");
        Steps.Clear();
        foreach (var row in SetupSteps.For(Step))
        {
            Steps.Add(row);
        }

        Permissions.Clear();
        foreach (var permission in _wizard.Permissions)
        {
            Permissions.Add(permission);
        }

        Tools.Clear();
        foreach (var tool in _wizard.Capture.Tools)
        {
            Tools.Add(new WizardToolViewModel(tool.Id, tool.DisplayName, tool.Enabled, (id, enabled) => _wizard.Capture.SetTool(id, enabled)));
        }

        Blocker = _wizard.Blocker ?? string.Empty;
        BlockerVisibility = Blocker.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        Note = _wizard.Note ?? string.Empty;

        // Done's note is the step itself: the page says it in full, so the foot does not say it again.
        NoteVisibility = Note.Length == 0 || Step == OnboardingStep.Done ? Visibility.Collapsed : Visibility.Visible;
        CanContinue = _wizard.CanContinue;
        CanGoBack = _wizard.CanGoBack;
        BackVisibility = CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        SkipVisibility = _wizard.CanSkip ? Visibility.Visible : Visibility.Collapsed;
        ContinueVisibility = Step == OnboardingStep.Done ? Visibility.Collapsed : Visibility.Visible;
        FinishVisibility = Step == OnboardingStep.Done ? Visibility.Visible : Visibility.Collapsed;
        TestRunning = _wizard.TestRunning;
        CanStartTest = !_wizard.TestRunning;
        FollowTheTest();
        FollowTheService();
        OnPropertyChanged(nameof(WelcomeVisibility));
        OnPropertyChanged(nameof(ActivateVisibility));
        OnPropertyChanged(nameof(PermissionsVisibility));
        OnPropertyChanged(nameof(ToolsVisibility));
        OnPropertyChanged(nameof(ConnectVisibility));
        OnPropertyChanged(nameof(TryItVisibility));
        OnPropertyChanged(nameof(DoneVisibility));
    }
}
