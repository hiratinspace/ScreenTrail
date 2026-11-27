using ScreenTrail.Core.Settings;
using ScreenTrail.Core.Shell;
using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Core.Onboarding;

/// <summary>Spec §5 S8's seven steps, in its order.</summary>
public enum OnboardingStep
{
    Welcome,
    Activate,
    Permissions,
    Tools,
    Connect,
    TryIt,
    Done,
}

/// <summary>The Try it step's test session: before it, running, or stopped and drafting.</summary>
public enum TestState
{
    NotStarted,
    Running,
    Done,
}

/// <summary>The service's capability checks, for the Permissions step. Over the pipe in the running application.</summary>
public interface ICapabilitiesGateway
{
    /// <summary>Null when the service did not answer.</summary>
    Task<IReadOnlyList<CapabilityStatus>?> CheckAsync(CancellationToken ct = default);
}

/// <summary>The sixty-second test session of the "Try it" step: a real session through the service, drafted the real way.</summary>
public interface ITestSession
{
    /// <summary>
    /// Why the last start was refused, in the service's words, or null. The wizard shows it, because a
    /// Start button that does nothing and says nothing leaves a first-run technician with no way on.
    /// </summary>
    string? Refusal => null;

    Task<bool> StartAsync(CancellationToken ct = default);

    Task<bool> StopAsync(CancellationToken ct = default);
}

/// <summary>How much a permission's state matters (review finding C-O4), which picks its glyph and colour.</summary>
public enum PermissionSeverity
{
    /// <summary>Allowed: the check in state.published.</summary>
    Ok,

    /// <summary>Not allowed, and sessions still work without it (the microphone): the warning in state.paused.</summary>
    OptionalMissing,

    /// <summary>Not allowed, and capture needs it (screen capture, input hooks): the close glyph in state.danger.</summary>
    Blocking,
}

/// <param name="FixLink">A Windows Settings deep link when there is one, so the fix is one click.</param>
public sealed record PermissionRow(string Capability, string Label, bool Ok, string Message, string? FixLink)
{
    /// <summary>What a missing microphone means, said under its row rather than in the step's footer (review finding C-O4).</summary>
    public const string MicrophoneConsequence = "Sessions are drafted from screenshots alone, without narration, until this is on.";

    public string Mark => Ok ? "✓" : "✗";

    /// <summary>
    /// The microphone missing is a worse session, not a broken setup, so it is not drawn as the red failure
    /// a blocked screen capture is (review finding C-O4).
    /// </summary>
    public PermissionSeverity Severity => Ok ? PermissionSeverity.Ok
        : Capability == "microphone" ? PermissionSeverity.OptionalMissing
        : PermissionSeverity.Blocking;

    /// <summary>What follows from the row's state, under its message, or empty.</summary>
    public string Consequence => Severity == PermissionSeverity.OptionalMissing ? MicrophoneConsequence : string.Empty;

    /// <summary>What the fix button says: the page it opens, for the microphone, whose page is Windows' privacy settings.</summary>
    public string FixLabel => Capability == "microphone" ? "Open Windows privacy settings" : "Fix in Windows Settings";

    /// <summary>The fix button for a screen reader, with the check it is for.</summary>
    public string FixName => $"{FixLabel}: {Label}";
}

/// <summary>
/// The onboarding wizard's rules (ST-083, Spec §5 S8), composed from the panels Settings already has:
/// activation, the tools, the integrations (shown read only: the owner connects them). Each step says what it takes to continue and why not. A
/// blocked microphone is a worse session rather than a blocked setup, so it is shown with its fix and
/// re-checked but does not stop the technician; blocked screen capture does, because there is nothing
/// to capture with. Finishing is remembered so the wizard does not run again unasked; the tray runs it
/// again on request.
/// </summary>
public sealed class OnboardingWizard(
    ActivationPanel activation,
    ICapabilitiesGateway capabilities,
    CapturePanel capture,
    IntegrationsPanel integrations,
    ITestSession testSession,
    Action<DateTimeOffset> finished,
    TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private static readonly (string Capability, string Label)[] Labels =
    [
        ("microphone", "Microphone"),
        ("screen_capture", "Screen capture"),
        ("input_hooks", "Keyboard and mouse hooks"),
    ];

    private readonly ICapabilitiesGateway _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    private readonly ITestSession _testSession = testSession ?? throw new ArgumentNullException(nameof(testSession));
    private readonly Action<DateTimeOffset> _finished = finished ?? throw new ArgumentNullException(nameof(finished));
    private bool _testDone;
    private string? _testRefused;

    private string? _toolsRefused;

    /// <summary>What the step says when the service refused the test session and gave no reason of its own.</summary>
    public const string TestNotStarted = "The test session did not start. Check that ScreenTrail's tray icon says it is connected, then try again.";

    /// <summary>
    /// The Connect step's first line (Spec §5 S8 step 5, v0.6.15). Integration credentials can only be
    /// changed by the tenant owner, in the web admin, so the step shows what is connected and says who
    /// connects the rest; there is nothing to type here, and the step can be skipped or continued as before.
    /// </summary>
    public const string ConnectByOwner = "Your ScreenTrail admin connects ConnectWise and Hudu in the web admin. You can publish once they have.";

    public ActivationPanel Activation { get; } = activation ?? throw new ArgumentNullException(nameof(activation));

    public CapturePanel Capture { get; } = capture ?? throw new ArgumentNullException(nameof(capture));

    public IntegrationsPanel Integrations { get; } = integrations ?? throw new ArgumentNullException(nameof(integrations));

    public OnboardingStep Step { get; private set; } = OnboardingStep.Welcome;

    public int StepCount { get; } = Enum.GetValues<OnboardingStep>().Length;

    public int StepNumber => (int)Step + 1;

    public IReadOnlyList<PermissionRow> Permissions { get; private set; } = [];

    public bool PermissionsKnown { get; private set; }

    public bool TestRunning { get; private set; }

    /// <summary>Where the Try it step's test is (review finding C-O6), which picks the card's state and its one filled button.</summary>
    public TestState Test => TestRunning ? TestState.Running : _testDone ? TestState.Done : TestState.NotStarted;

    /// <summary>When the running test started, for the card's timer; null when none is running.</summary>
    public DateTimeOffset? TestStartedAt { get; private set; }

    /// <summary>How long the running test has run, as the pill says it ("0:07", "1:02"); empty when none is running.</summary>
    public string TestElapsed => TestStartedAt is { } at ? Elapsed(_time.GetUtcNow() - at) : string.Empty;

    /// <summary>What the card says once the test has stopped.</summary>
    /// <summary>
    /// Review finding C-T8: Windows 11 puts a new tray icon in the overflow flyout, and between sessions the
    /// tray icon is the indicator (INV-4), so Done offers the Settings page that pins it.
    /// </summary>
    public const string TrayHint = "Between sessions ScreenTrail shows only its tray icon. Windows may tuck a new icon behind the arrow on the taskbar; turn ScreenTrail on under Other system tray icons to keep it in sight.";

    /// <summary>The taskbar page of Windows Settings, where Other system tray icons are.</summary>
    public const string TaskbarSettings = "ms-settings:taskbar";

    /// <summary>
    /// The Permissions step before the service has answered (review finding C-I4): an opened app starts
    /// the service itself, and the step checks again every two seconds until it answers.
    /// </summary>
    public const string WaitingForService = "Starting the capture service, then checking what this PC allows. This takes a few seconds.";

    /// <summary>Whether the step is waiting on the service, with the ring beside its words, and checking again.</summary>
    public bool WaitingOnService => Step == OnboardingStep.Permissions && !PermissionsKnown;

    public const string TestDrafting = "The draft is on its way. It opens in Review when it is ready, usually within a minute.";

    private static string Elapsed(TimeSpan span)
    {
        var seconds = Math.Max(0, (long)span.TotalSeconds);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }

    public bool Finished { get; private set; }

    public bool CanGoBack => Step > OnboardingStep.Welcome && Step < OnboardingStep.Done;

    /// <summary>
    /// Skip is offered only where there is something left undone to skip (review finding C-O7): Activate
    /// before this PC is activated, because its own words offer Settings later, and a device enrolled from
    /// the environment (the M1 runbook) has no code to enter. Once activated there is nothing to skip, and
    /// Connect is read only (the owner connects it, Spec §5 S8 step 5), so Skip and Continue did the same
    /// thing there; both steps continue with Continue alone.
    /// </summary>
    public bool CanSkip => Step is OnboardingStep.Activate && !Activation.Activated;

    /// <summary>Why the technician cannot continue yet, or null.</summary>
    public string? Blocker => Step switch
    {
        OnboardingStep.Activate when !Activation.Activated => "Enter the code from your invite, or come back to this from Settings later.",
        OnboardingStep.Permissions when !PermissionsKnown => WaitingForService,
        OnboardingStep.Permissions when Permissions.Any(p => !p.Ok && p.Capability == "screen_capture") =>
            "Screen capture is blocked, so there is nothing to capture with. Fix that first.",
        OnboardingStep.TryIt when !_testDone => TestRunning
            ? "Recording. Open any window, click around, say one sentence, then stop."
            : "Start the test session, do a few things, and stop it.",
        _ => null,
    };

    /// <summary>What the step says beside its content, or null: the microphone's consequence, the test's outcome.</summary>
    public string? Note => Step switch
    {
        OnboardingStep.Tools when _toolsRefused is { } notSaved => notSaved,
        OnboardingStep.TryIt when !_testDone && _testRefused is { } refused => refused,
        OnboardingStep.Done => "ScreenTrail starts when you open a remote session. Look for the pill.",
        _ => null,
    };

    public bool CanContinue => Blocker is null && Step < OnboardingStep.Done;

    /// <summary>Asks the service for what the step needs. Called on arrival at each step.</summary>
    public async Task LoadStepAsync(CancellationToken ct = default)
    {
        switch (Step)
        {
            case OnboardingStep.Activate:
                await Activation.LoadAsync(ct).ConfigureAwait(false);
                break;
            case OnboardingStep.Permissions:
                await RecheckAsync(ct).ConfigureAwait(false);
                break;
            case OnboardingStep.Tools:
                await Capture.LoadAsync(ct).ConfigureAwait(false);
                break;
            case OnboardingStep.Connect:
                await Integrations.LoadAsync(ct).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>Runs the checks again; after a fix in Windows Settings, the button says "Check again" (AC3).</summary>
    public async Task RecheckAsync(CancellationToken ct = default)
    {
        var checks = await _capabilities.CheckAsync(ct).ConfigureAwait(false);
        PermissionsKnown = checks is not null;
        Permissions = checks is null
            ? []
            : [.. Labels.Select(l =>
            {
                var check = checks.FirstOrDefault(c => c.Capability == l.Capability);
                return check is null
                    ? new PermissionRow(l.Capability, l.Label, false, "Not checked.", null)
                    : new PermissionRow(l.Capability, l.Label, check.State == "ok", check.Message, check.FixLink);
            })];
    }

    /// <summary>What the Tools step says when Continue could not save the choices, before the service's reason.</summary>
    public const string ToolsNotSaved = "Your remote tools were not saved.";

    /// <summary>
    /// Continue. On the Tools step it saves the choices first: the step used to have its own Save beside
    /// Continue, and Continue alone silently dropped an unticked tool (review findings C-O5, E-09). If the
    /// save fails the wizard stays on the step and says why. Returns whether the wizard moved on.
    /// </summary>
    public async Task<bool> ContinueAsync(CancellationToken ct = default)
    {
        if (!CanContinue)
        {
            return false;
        }

        if (Step == OnboardingStep.Tools && Capture.HasUnsavedEdits && !await Capture.SaveAsync(ct).ConfigureAwait(false))
        {
            _toolsRefused = Capture.Problem is { Length: > 0 } why ? $"{ToolsNotSaved} {why}" : $"{ToolsNotSaved} Try again.";
            return false;
        }

        _toolsRefused = null;
        Next();
        return true;
    }

    public void Next()
    {
        if (CanContinue)
        {
            Step += 1;
        }
    }

    public void Back()
    {
        if (CanGoBack)
        {
            Step -= 1;
        }
    }

    public void Skip()
    {
        if (CanSkip)
        {
            Step += 1;
        }
    }

    /// <summary>Jumps to a step: the tray's "Set up again" lands on Welcome, Settings' "check permissions" on Permissions.</summary>
    public void Go(OnboardingStep step) => Step = step;

    /// <summary>
    /// Starts the test session, or says why it did not: the service's reason, such as the recording pill
    /// not being on screen yet (INV-4), shown as the step's note until a start works.
    /// </summary>
    public async Task<bool> StartTestAsync(CancellationToken ct = default)
    {
        if (TestRunning)
        {
            return false;
        }

        if (!await _testSession.StartAsync(ct).ConfigureAwait(false))
        {
            _testRefused = _testSession.Refusal is { Length: > 0 } why ? why : TestNotStarted;
            return false;
        }

        _testRefused = null;
        TestRunning = true;

        // A second test, after "Run the test again", is a test of its own: Continue waits for it to stop.
        _testDone = false;
        TestStartedAt = _time.GetUtcNow();
        return true;
    }

    public async Task<bool> StopTestAsync(CancellationToken ct = default)
    {
        if (!TestRunning || !await _testSession.StopAsync(ct).ConfigureAwait(false))
        {
            return false;
        }

        TestRunning = false;
        TestStartedAt = null;
        _testDone = true;
        return true;
    }

    public void Finish()
    {
        Finished = true;
        _finished(DateTimeOffset.UtcNow);
    }
}

/// <summary>The Permissions step's gateway, over the pipe.</summary>
public sealed class PipeCapabilities(CaptureConnection connection) : ICapabilitiesGateway
{
    private readonly CaptureConnection _connection = connection ?? throw new ArgumentNullException(nameof(connection));

    public async Task<IReadOnlyList<CapabilityStatus>?> CheckAsync(CancellationToken ct = default)
    {
        var reported = await _connection.RequestAsync<CapabilitiesReported>(id => new GetCapabilitiesCommand { RequestId = id }, ct).ConfigureAwait(false);
        return reported?.Checks;
    }
}

/// <summary>The "Try it" step's session, over the pipe: the same start and stop the hotkeys send.</summary>
public sealed class PipeTestSession(CaptureConnection connection) : ITestSession
{
    private readonly CaptureConnection _connection = connection ?? throw new ArgumentNullException(nameof(connection));

    public string? Refusal { get; private set; }

    public async Task<bool> StartAsync(CancellationToken ct = default)
    {
        var result = await _connection.SendAsync(id => new StartCommand { RequestId = id }, ct).ConfigureAwait(false);
        Refusal = result.Ok ? null : result.Error;
        return result.Ok;
    }

    public async Task<bool> StopAsync(CancellationToken ct = default) =>
        (await _connection.SendAsync(id => new StopCommand { RequestId = id }, ct).ConfigureAwait(false)).Ok;
}
