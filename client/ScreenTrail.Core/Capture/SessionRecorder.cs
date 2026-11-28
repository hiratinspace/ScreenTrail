using ScreenTrail.Core.Detection;
using ScreenTrail.Core.Input;
using ScreenTrail.Core.Privacy;
using ScreenTrail.Core.Sessions;
using ScreenTrail.Core.Store;
using ScreenTrail.Shared.Ipc;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Core.Capture;

/// <summary>
/// Turns captured input into timeline events and screenshots, under the capture rules (ST-025, ST-048).
///
/// This is where the rules meet: the state machine decides whether anything may be recorded at all
/// (INV-6), the scope decision decides whether this particular window may be photographed (INV-5), and
/// the debouncer decides whether a click is a new action or part of one already captured.
///
/// A click that fails the scope test still becomes an event, with no frame and no screen position: ST-023
/// asks for the event, so Review can show "clicks logged, no frames" rather than a silent gap, and INV-6
/// drops where it landed, which says what was pressed in a window nobody may look at. Anything
/// keyboard-derived is dropped instead: INV-6 says out-of-scope means "no typing events written", and a
/// keystroke count taken in a customer's password manager is not made harmless by having no picture
/// beside it.
///
/// It lives in Core, away from the Windows hooks that feed it, because that is what it took to make the
/// rule testable. While the same code sat inside the Windows drain loop, no test project referenced it:
/// the out-of-scope drop could be deleted and the whole suite stayed green (weaknesses P0-4). The hooks
/// and the screen grab are still Windows-only and still behind interfaces; the decisions are here.
/// </summary>
/// <param name="foreground">
/// The window in front now, as Windows says, or null when the caller cannot ask. A click's position is
/// kept only while the window in front is the one the scope decision judged: a click on a background
/// window brings it forward only after it lands, and a window that never takes the foreground (the
/// on-screen keyboard) is clicked while another is in front, so in both the position is about a window
/// nobody judged (INV-6).
/// </param>
/// <param name="now">
/// The hooks' clock now, in the units the signals are stamped in, or null when the caller has none. With
/// it, a signal stamped before the session last came back to Recording is dropped, and keyboard events
/// wait <see cref="KeyboardHoldBack"/> before they are written, so a hold that lands in that time drops
/// them (A-07). The capture loop passes it; without it neither is done.
/// </param>
public sealed class SessionRecorder(
    SessionMachine machine,
    IScreenshotCapturer capturer,
    Func<ScopeDecision?> currentScope,
    ClickDebouncer? debouncer = null,
    Func<nint>? foreground = null,
    LastFrame? lastFrame = null,
    Func<long>? now = null)
{
    /// <summary>
    /// How long a typing burst, Enter or shortcut waits before it is written: the password-field guard's
    /// worst case, its one-second backstop poll and a slow probe. Keys typed into a field the guard has not
    /// yet noticed (a PIN and Enter, quicker than the probe) are dropped when its hold lands inside this
    /// time, instead of being written before the hold existed (A-07, INV-6). Each keeps the time it was
    /// typed, so the timeline reads as before; only the write is later.
    /// </summary>
    public static readonly TimeSpan KeyboardHoldBack = new PasswordFieldOptions().PollEvery + TimeSpan.FromMilliseconds(500);

    private readonly ClickDebouncer _debouncer = debouncer ?? new ClickDebouncer();
    private readonly InputSignalReader _reader = new();
    private readonly Boundaries _boundaries = new(machine, now);
    private readonly List<(SessionEvent Event, long HeldFrom)> _held = [];
    private long _boundariesSeen;
    private long _dropped;
    private long _beforeSession;

    /// <summary>Keyboard signals and events dropped because the window was out of scope (INV-6). Counts only.</summary>
    public long DroppedOutOfScope => Interlocked.Read(ref _dropped);

    /// <summary>Signals stamped before the session began, and so not part of it. Counts only.</summary>
    public long DroppedBeforeSession => Interlocked.Read(ref _beforeSession);

    /// <summary>Whether keyboard events are waiting out <see cref="KeyboardHoldBack"/>, so an empty drain still has something to write.</summary>
    public bool HoldsKeyboardEvents => _held.Count > 0;

    /// <summary>Clicks that shared a frame with an earlier one (ST-025).</summary>
    public long ClicksSharingAFrame => _debouncer.Suppressed;

    /// <summary>Raised after a frame is staged, so the host can log what it cost. Never carries pixels.</summary>
    public event Action<CapturedFrame>? FrameStaged;

    /// <summary>Raised when the window was gone by the time the picture was taken. A missing frame, never a blank one.</summary>
    public event Action<long>? FrameMissed;

    /// <summary>A new session starts with no history, so its first click is captured (ST-029).</summary>
    public void Reset()
    {
        _debouncer.Reset();
        lastFrame?.Reset();

        // Whatever was being typed belonged to no session, or to the last one.
        _reader.Discard();
        _held.Clear();
    }

    /// <summary>
    /// Records what the input hooks saw since the last call, under the rules in force <i>now</i>.
    ///
    /// Signals rather than finished events, because the rules have to be applied before keys are added
    /// up. A typing burst is a count kept across many calls, and an event that says "fourteen characters"
    /// cannot say that they were typed while capture was suppressed for a password field. Until
    /// 2026-09-19 that is what happened: the count ran through pauses, suppression and out-of-scope
    /// windows, and the first click after capture came back wrote it into the session: the length of a
    /// password and the moment it was typed, recorded by the mechanism built not to (INV-6).
    ///
    /// So, per call: if the session is not recording, everything is dropped and the open burst is
    /// forgotten. If the window in front may not have its typing recorded, keyboard signals are dropped
    /// and the open burst is forgotten; clicks still pass, as ST-023 asks. A burst that was legitimately
    /// under way when a boundary arrived is lost with the rest. That costs a note one "typed 9
    /// characters", and the alternative is deciding after the fact which keys were which.
    ///
    /// Anything stamped before the session began is dropped too. The click that brings a remote tool to
    /// the front is what starts the session, so it arrives here with a negative session time; the store
    /// refuses one, and that exception used to end click capture for the life of the service.
    /// </summary>
    public async Task RecordSignalsAsync(
        ReadOnlyMemory<InputSignal> signals,
        Func<long, long> toSessionMs,
        Func<long, long, TimeSpan> elapsed,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(toSessionMs);
        ArgumentNullException.ThrowIfNull(elapsed);

        // The session left Recording since the last call. Whatever was open or waiting then belongs before
        // that boundary, even when every drain while it held was empty and so never reached the check
        // below: a burst kept across a hold was written by the first key or click after it (A-07).
        var left = _boundaries.Left;
        if (left != _boundariesSeen)
        {
            _boundariesSeen = left;
            _reader.Discard();
            _held.Clear();
        }

        if (machine.State != SessionState.Recording)
        {
            _reader.Discard();
            _held.Clear();
            return;
        }

        // The same question RecordAsync asks of a finished event, asked of the keys before they are
        // counted. Nothing known about the window in front is not a reason to count keystrokes.
        var typingAllowed = currentScope()?.MayCaptureFrames == true;
        if (!typingAllowed)
        {
            _reader.Discard();
        }

        // Stamped before the session last came back to Recording: typed or clicked inside a pause or a
        // hold and drained just after it ended. As with what came before the session began, it is not
        // the session's (A-07).
        var recordingSince = _boundaries.RecordingSince;
        var kept = new List<InputSignal>(signals.Length);
        foreach (var signal in signals.Span)
        {
            if (toSessionMs(signal.Timestamp) < 0)
            {
                _ = Interlocked.Increment(ref _beforeSession);
                continue;
            }

            if (signal.Timestamp < recordingSince)
            {
                continue;
            }

            if (!typingAllowed && signal.Kind != InputKind.Click)
            {
                _ = Interlocked.Increment(ref _dropped);
                continue;
            }

            kept.Add(signal);
        }

        if (kept.Count > 0)
        {
            var events = _reader.Read(kept.ToArray(), toSessionMs, elapsed);
            await RecordAsync([.. events], holdKeyboardFrom: now is null ? null : kept[^1].Timestamp, ct).ConfigureAwait(false);
        }

        if (now is not null)
        {
            await WriteHeldAsync(now(), elapsed, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes the keyboard events that have waited out <see cref="KeyboardHoldBack"/>, oldest first. A
    /// boundary since this call began leaves them for the next, which drops them.
    /// </summary>
    private async Task WriteHeldAsync(long present, Func<long, long, TimeSpan> elapsed, CancellationToken ct)
    {
        while (_held.Count > 0 && elapsed(_held[0].HeldFrom, present) >= KeyboardHoldBack)
        {
            if (_boundaries.Left != _boundariesSeen)
            {
                return;
            }

            // Taken off before the write, so a write that throws is not tried again and written twice.
            var next = _held[0].Event;
            _held.RemoveAt(0);
            _ = await machine.TryRecordEventAsync(next, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Records a batch of events. Each is checked against the scope decision in force, and a click that
    /// survives may also produce a frame.
    /// </summary>
    public Task RecordAsync(IReadOnlyList<SessionEvent> events, CancellationToken ct = default) =>
        RecordAsync(events, holdKeyboardFrom: null, ct);

    /// <param name="holdKeyboardFrom">
    /// When not null, keyboard events that pass are held from this hook time rather than written now
    /// (<see cref="KeyboardHoldBack"/>).
    /// </param>
    private async Task RecordAsync(IReadOnlyList<SessionEvent> events, long? holdKeyboardFrom, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (machine.State != SessionState.Recording)
        {
            // Paused, suppressed or idle: the signals are dropped rather than held, so nothing observed
            // while capture was off can arrive late and land in the session (INV-6).
            return;
        }

        var scope = currentScope();
        foreach (var sessionEvent in events)
        {
            // INV-6, decided by the scope decision itself so the rule is one thing in one place. No scope
            // decision yet means nothing is known about the window in front, which is not a reason to
            // record a keystroke count.
            if (scope is null ? IsKeyboard(sessionEvent) : !scope.MayRecord(sessionEvent))
            {
                _ = Interlocked.Increment(ref _dropped);
                continue;
            }

            // A click in a window that may not be photographed loses where it landed (INV-6) and gets no
            // frame, and so does one made while the window in front is not the one judged. The store keeps
            // no position for a click without a frame either; the frame records the cursor itself.
            var admitted = scope?.Admit(sessionEvent) ?? ScopeDecision.WithoutPosition(sessionEvent);
            if (admitted is ClickEvent && foreground is not null && foreground() != scope?.Window)
            {
                admitted = ScopeDecision.WithoutPosition(admitted);
            }

            if (holdKeyboardFrom is { } heldFrom && IsKeyboard(admitted))
            {
                _held.Add((admitted, heldFrom));
                continue;
            }

            _ = await machine.TryRecordEventAsync(admitted, ct).ConfigureAwait(false);
            if (admitted is ClickEvent { X: { } x, Y: { } y } click)
            {
                await CaptureForAsync(click, x, y, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Keyboard-derived events, which INV-6 calls "typing events". A shortcut and an Enter say as much
    /// about what someone typed as a burst count does (Ctrl+C in a password manager is the case), so all
    /// three are treated alike.
    /// </summary>
    private static bool IsKeyboard(SessionEvent sessionEvent) =>
        sessionEvent is TypingBurstEvent or ShortcutEvent or EnterEvent;

    /// <summary>
    /// When the session leaves Recording and when it comes back, as the machine announces each transition.
    /// Counted and stamped here, on the machine's thread; the recorder reads them on its own.
    /// </summary>
    private sealed class Boundaries
    {
        private long _left;
        private long _recordingSince = long.MinValue;
        private bool _recording;

        public Boundaries(SessionMachine machine, Func<long>? now) =>
            machine.StateChanged += snapshot =>
            {
                var recording = snapshot.State == CaptureStates.Recording;
                if (recording && !_recording && now is not null)
                {
                    _ = Interlocked.Exchange(ref _recordingSince, now());
                }
                else if (!recording)
                {
                    _ = Interlocked.Increment(ref _left);
                }

                _recording = recording;
            };

        /// <summary>How many times the session has left Recording.</summary>
        public long Left => Interlocked.Read(ref _left);

        /// <summary>The hook time the session last came back to Recording; the smallest value when unknown.</summary>
        public long RecordingSince => Interlocked.Read(ref _recordingSince);
    }

    private async Task CaptureForAsync(ClickEvent click, long x, long y, CancellationToken ct)
    {
        // Re-read rather than reuse the decision from the top of the batch: the technician may have moved
        // to another window between two events, and a picture is taken against the window scope is about.
        if (FrameGate.WindowFor(currentScope()) is not { } window)
        {
            return;
        }

        if (!_debouncer.ShouldCapture())
        {
            return;
        }

        // A click on a screen that looks like the last frame kept adds no step, only a duplicate (LastFrame).
        // One StretchBlt into a 17x16 grid decides it, before any full capture is paid for. A grid that
        // could not be taken decides nothing, and the click is photographed as before.
        SceneHash? hash = null;
        if (lastFrame is not null && capturer.CaptureSceneGrid(window) is { } grid)
        {
            hash = PerceptualHash.OfGrid(grid);
            if (lastFrame.Repeats(hash.Value))
            {
                return;
            }
        }

        var frame = capturer.CaptureForegroundWindow(window);
        if (frame is null)
        {
            FrameMissed?.Invoke(click.TsMs);
            return;
        }

        // Staged with redaction_pending, the only state a frame can be born in: nothing outside the
        // redaction worker can read one until it has been through ST-041 (INV-1).
        var staged = await machine.TryStageFrameAsync(
            new StagedFrame(
                Guid.NewGuid().ToString("N")[..12],
                click.TsMs,
                FrameTrigger.Click,
                frame.Width,
                frame.Height,
                new Point { X = x, Y = y },
                frame.Image),
            ct).ConfigureAwait(false);

        // Only a frame that was accepted was staged. A refused one is counted against the session by
        // the machine, and the host's log line about what the frame cost would otherwise describe a
        // frame that went nowhere (2026-10-12 review, S-1; the scene recorder already did this).
        if (staged)
        {
            if (hash is { } kept)
            {
                lastFrame?.Kept(kept);
            }

            FrameStaged?.Invoke(frame);
        }
    }
}
