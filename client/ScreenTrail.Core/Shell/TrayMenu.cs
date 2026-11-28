using System.Globalization;
using ScreenTrail.Core.Hud;
using ScreenTrail.Core.Input;
using ScreenTrail.Core.Notifications;
using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Core.Shell;

/// <summary>What a row of the tray's quick menu does when it is chosen. Each is something the shell already does.</summary>
public enum TrayCommand
{
    /// <summary>Not a command: the state line and the separators.</summary>
    None,

    /// <summary>
    /// <c>start</c> with <c>origin: tray</c> (contract v13): a session in the remote tool window most recently
    /// in front, as Ctrl+Alt+R would have started it there (Spec §5 S1, as built v0.7.2).
    /// </summary>
    Start,

    /// <summary><c>pause</c>, the same as the pill's Pause and Ctrl+Alt+P.</summary>
    Pause,

    /// <summary><c>resume</c>, the same as the pill's Resume and Ctrl+Alt+P.</summary>
    Resume,

    /// <summary><c>stop</c>: stop and draft.</summary>
    Stop,

    /// <summary>The typed-confirmation discard of the session the service is on (Spec §3, v0.4.1 Q3).</summary>
    Discard,

    /// <summary>The shell, on Review of the newest draft, or on Review when there is none.</summary>
    OpenLastDraft,

    /// <summary>The shell, on All sessions.</summary>
    History,

    /// <summary>The Diagnostics panel, "What's being captured right now?" (ST-071).</summary>
    Diagnostics,

    /// <summary>Put the pill away until the next session starts (INV-4: only between sessions).</summary>
    HidePill,

    /// <summary>Bring the pill back after it was put away.</summary>
    ShowPill,

    /// <summary>The shell, on Settings.</summary>
    Settings,

    /// <summary>The setup wizard, again (ST-083).</summary>
    Setup,

    /// <summary>Quit, finishing a session first if one is being captured.</summary>
    Quit,

    /// <summary>The capture service, started again, as the main window's banner's Start does (review finding E-13).</summary>
    StartService,

    /// <summary><c>retry_store</c> (contract v19): the service tries its store again after it would not open (F-10).</summary>
    RetryStore,
}

/// <summary>What kind of row it is, which is how the host draws it.</summary>
public enum TrayRowKind
{
    /// <summary>The first row: what capture is doing, in words, with the tray's own glyph. Never interactive.</summary>
    State,

    /// <summary>A line that says how to do something the menu cannot do. Never interactive.</summary>
    Hint,

    /// <summary>Something to choose. Disabled when the service would refuse it now.</summary>
    Item,

    Separator,
}

/// <param name="Text">The words, with <c>&amp;</c> before the access key; an <c>&amp;</c> that is text is not used.</param>
/// <param name="Hotkey">The default global chord that does the same, shown on the right ("Ctrl+Alt+P"), or null.</param>
/// <param name="Detail">The state row's second line, what waits between sessions (<see cref="TrayPresence.Counts"/>), or null.</param>
public sealed record TrayRow(TrayRowKind Kind, TrayCommand Command, string Text, string? Hotkey, bool Enabled, string? Detail = null)
{
    /// <summary>The words without the access key's marker: what a test compares.</summary>
    public string Name => Text.Replace("&", string.Empty, StringComparison.Ordinal);

    /// <summary>What a screen reader says: the words, then the second line when there is one.</summary>
    public string Spoken => Detail is { } more ? $"{Name}. {more}" : Name;

    /// <summary>The access key, upper case, or null for a row that has none.</summary>
    public char? AccessKey => Text.IndexOf('&', StringComparison.Ordinal) is var at and >= 0 && at + 1 < Text.Length
        ? char.ToUpperInvariant(Text[at + 1])
        : null;

    internal static TrayRow Line(TrayRowKind kind, string text) => new(kind, TrayCommand.None, text, null, false);

    internal static readonly TrayRow Separator = new(TrayRowKind.Separator, TrayCommand.None, string.Empty, null, false);
}

/// <summary>
/// The tray's quick menu (Spec §5 S1), row by row, for the state the service last reported.
///
/// The host draws exactly these rows in this order and does nothing else with the state, so the order,
/// the words, what is disabled and the INV-4 rule for the pill are all held here, where the Mac suite
/// tests them. What each command does is the shell's, unchanged: the tray offers only what the pill,
/// the hotkeys and the shell already do.
/// </summary>
public static class TrayMenu
{
    /// <summary>
    /// Between sessions the first block is the spec's "Start capture Ctrl+Alt+R" (v0.7.2). The service starts
    /// it in the remote tool window most recently in front, since the menu itself is in front as it is
    /// chosen, and the shell says why in a balloon when it starts nothing.
    /// </summary>
    public const string StartText = "&Start capture";

    /// <summary>The chord shown beside Start capture, as the other rows show theirs: as it is bound now.</summary>
    public static string StartHotkey => Chord(HotkeyAction.StartCapture);

    public const string DiscardText = "&Discard session…";

    /// <summary>The rail's name for the table (review finding E-07; it was "Session history").</summary>
    public const string HistoryText = "&All sessions";

    /// <summary>The rail's name for the panel (review finding E-13; it was "What's being captured right now?").</summary>
    public const string DiagnosticsText = "Dia&gnostics";

    public const string HidePillText = "Hide the recording pi&ll until the next session";

    /// <summary>The same row while a session may be under way, when the pill stays (INV-4).</summary>
    public const string HidePillNotNowText = "Hide the recording pi&ll (only between sessions)";

    public const string ShowPillText = "Show the recording pi&ll";

    public const string SettingsText = "S&ettings";

    public const string SetupText = "Set &up ScreenTrail…";

    public const string QuitText = "&Quit ScreenTrail";

    public const string OpenText = "&Open ScreenTrail";

    /// <summary>"Open next draft (2 ready)", or Open ScreenTrail when no draft is known to be ready.</summary>
    public static string OpenLastDraftText(int ready) => ready > 0
        ? string.Create(CultureInfo.InvariantCulture, $"&Open next draft ({ready} ready)")
        : OpenText;

    /// <param name="snapshot">What the shell last heard.</param>
    /// <param name="pillHidden">Whether the technician has put the pill away between sessions.</param>
    public static IReadOnlyList<TrayRow> For(ShellSnapshot snapshot, bool pillHidden)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var presence = TrayPresence.From(snapshot);
        var connected = snapshot.Connection == ServiceConnection.Connected;
        var capture = connected ? snapshot.Capture : null;
        var rows = new List<TrayRow> { TrayRow.Line(TrayRowKind.State, presence.StateLine) with { Detail = presence.Counts } };

        // The first block. While a session is under way, or the UI does not know whether one is: Pause or
        // Resume, Stop and draft, Discard, each enabled only where the service accepts it now (the pill's
        // own rule). Between sessions it is Start capture, which the service accepts from every resting
        // state as the hotkey's start does, with Discard while there is a session to discard (a draft
        // finishing, ready or failed). Resting is known only from a connected service, so Start capture
        // is never offered into a dropped pipe.
        var resting = capture?.State is CaptureStates.Idle or CaptureStates.DraftPending or CaptureStates.DraftReady or CaptureStates.DraftFailed;
        if (capture?.State == CaptureStates.StoreUnavailable)
        {
            // The store would not open (F-10): nothing can be captured or discarded, and the service takes
            // two things, a retry, offered here, and "Delete everything", which is in Settings with its
            // typed confirmation.
            rows.Add(new TrayRow(TrayRowKind.Item, TrayCommand.RetryStore, StoreUnavailableWords.RetryText, null, Enabled: true));
        }
        else if (resting)
        {
            rows.Add(new TrayRow(TrayRowKind.Item, TrayCommand.Start, StartText, StartHotkey, Enabled: true));
            if (capture!.State != CaptureStates.Idle)
            {
                rows.Add(new TrayRow(TrayRowKind.Item, TrayCommand.Discard, DiscardText, null, Enabled: true));
            }
        }
        else if (Unreachable(snapshot.Connection) is not null)
        {
            // Nothing can be sent, so every row that sends is disabled. The state line above them says
            // why once ("Capture service not running"); each row repeating it in brackets made four
            // copies of one reason (review finding E-13, which replaces v0.6.12's per-row reasons). With the
            // service not running, the first row offers to start it, as the main window's banner does;
            // not while the first connection is still being made, when it is starting already.
            if (snapshot.Connection != ServiceConnection.Connecting)
            {
                rows.Add(new TrayRow(TrayRowKind.Item, TrayCommand.StartService, CaptureServiceStart.RowText, null, Enabled: true));
            }

            rows.Add(new TrayRow(TrayRowKind.Item, TrayCommand.Pause, "&Pause", HudControls.Pause.Hotkey, Enabled: false));
            rows.Add(new TrayRow(TrayRowKind.Item, TrayCommand.Stop, "&Stop and draft", HudControls.Stop.Hotkey, Enabled: false));
            rows.Add(new TrayRow(TrayRowKind.Item, TrayCommand.Discard, DiscardText, null, Enabled: false));
        }
        else
        {
            var offered = HudControls.For(capture);
            var resume = offered.Contains(HudControls.Resume);
            rows.Add(resume
                ? new TrayRow(TrayRowKind.Item, TrayCommand.Resume, "&Resume", HudControls.Resume.Hotkey, Enabled: true)
                : new TrayRow(TrayRowKind.Item, TrayCommand.Pause, "&Pause", HudControls.Pause.Hotkey, offered.Contains(HudControls.Pause)));
            rows.Add(new TrayRow(TrayRowKind.Item, TrayCommand.Stop, "&Stop and draft", HudControls.Stop.Hotkey, offered.Contains(HudControls.Stop)));

            // The service discards from any state with a session, finishing up included.
            rows.Add(new TrayRow(TrayRowKind.Item, TrayCommand.Discard, DiscardText, null, capture is not null && capture.State != CaptureStates.Idle));
        }

        rows.Add(TrayRow.Separator);
        rows.Add(new TrayRow(TrayRowKind.Item, TrayCommand.OpenLastDraft, OpenLastDraftText(presence.DraftsReady), null, Enabled: true));
        rows.Add(new TrayRow(TrayRowKind.Item, TrayCommand.History, HistoryText, null, Enabled: true));
        rows.Add(new TrayRow(TrayRowKind.Item, TrayCommand.Diagnostics, DiagnosticsText, null, Enabled: true));

        // INV-4 (owner decision 2026-10-25): the pill may be put away only while capture is known to be off,
        // and comes back by itself when the next session starts. While a session may be under way the row
        // stays, disabled, and says when it can be used, rather than taking a wish the shell would ignore.
        var knownIdle = connected && HudState.KnownIdle(snapshot.KnownCapture);
        rows.Add(knownIdle && pillHidden
            ? new TrayRow(TrayRowKind.Item, TrayCommand.ShowPill, ShowPillText, null, Enabled: true)
            : new TrayRow(TrayRowKind.Item, TrayCommand.HidePill, HidePillWords(knownIdle, snapshot.Connection), null, knownIdle));

        rows.Add(TrayRow.Separator);
        rows.Add(new TrayRow(TrayRowKind.Item, TrayCommand.Settings, SettingsText, null, Enabled: true));
        rows.Add(new TrayRow(TrayRowKind.Item, TrayCommand.Setup, SetupText, null, Enabled: true));
        rows.Add(new TrayRow(TrayRowKind.Item, TrayCommand.Quit, QuitText, null, Enabled: true));
        return rows;
    }

    /// <summary>
    /// Whether the tray's Discard has to stop capture before it asks (Spec v0.4.1 Q3): while a session is
    /// being captured no modal may appear (Spec §4), so the session is stopped first and moves to finishing
    /// up, then the typed confirmation is shown.
    /// </summary>
    public static bool DiscardStopsFirst(CaptureStateSnapshot? capture) => HudState.SessionUnderWay(capture) is not null;

    /// <summary>
    /// The tray's Discard: stop first while a session is being captured, and only once the service has
    /// answered, ask. The question is asked whatever the stop's answer: the service discards from any state
    /// with a session, and a refused stop means the session is no longer recording anyway.
    /// </summary>
    /// <param name="capture">What capture is doing as the item is chosen.</param>
    /// <param name="stop">Sends <c>stop</c> and waits for the service's answer.</param>
    /// <param name="ask">The typed confirmation and, when it is typed, the discard.</param>
    public static async Task DiscardAsync(CaptureStateSnapshot? capture, Func<Task> stop, Func<Task> ask)
    {
        ArgumentNullException.ThrowIfNull(stop);
        ArgumentNullException.ThrowIfNull(ask);
        if (DiscardStopsFirst(capture))
        {
            await stop().ConfigureAwait(true);
        }

        await ask().ConfigureAwait(true);
    }

    /// <summary>What the balloon says when the service gives no reason of its own for starting nothing.</summary>
    public static string StartRefused => $"Nothing was started. Open the remote session, then choose Start capture{Sessions.HandStart.OrPress()}.";

    /// <summary>
    /// The tray's Start capture: sends the tray's <c>start</c> and says, in a balloon, why nothing started
    /// when the service refused (Spec §5 S1, as built v0.7.2). A start that worked says nothing here: the
    /// pill and the icon say it (INV-4). Nor does a refusal once a session is under way after all (a start
    /// from elsewhere won the race), because nothing interrupts a recording (Spec §4); the pill says that too.
    /// </summary>
    /// <param name="send">Sends <c>start</c> with <c>origin: tray</c> and returns the service's answer.</param>
    /// <param name="captureNow">What capture is doing once the answer is in.</param>
    /// <returns>The balloon to show, or null for none.</returns>
    public static async Task<Notification?> StartAsync(Func<Task<CommandResult>> send, Func<CaptureStateSnapshot?> captureNow)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(captureNow);
        var result = await send().ConfigureAwait(true);
        if (result.Ok || captureNow() is { State: CaptureStates.Recording or CaptureStates.Paused or CaptureStates.Suppressed or CaptureStates.Finalizing })
        {
            return null;
        }

        var words = string.IsNullOrWhiteSpace(result.Error) ? StartRefused : result.Error;
        return new Notification(words, NotificationAction.None, $"tray-start-refused:{words}", "Capture not started");
    }

    /// <summary>The reason a row cannot be chosen while the service is not answering (the state line says "Capture service not running").</summary>
    public const string NotRunning = "capture service not running";

    /// <summary>The reason while the first connection is still being made (the state line says "Connecting to the capture service").</summary>
    public const string StillConnecting = "still connecting to the capture service";

    /// <summary>Why nothing can be sent now, in the words a disabled row gives, or null while the service answers.</summary>
    public static string? Unreachable(ServiceConnection connection) => connection switch
    {
        ServiceConnection.Connected => null,
        ServiceConnection.Connecting => StillConnecting,
        _ => NotRunning,
    };

    private static string HidePillWords(bool knownIdle, ServiceConnection connection) =>
        knownIdle ? HidePillText
        : Unreachable(connection) is not null ? "Hide the recording pi&ll"
        : HidePillNotNowText;

    private static string Chord(HotkeyAction action) => HotkeyText.For(action);
}
