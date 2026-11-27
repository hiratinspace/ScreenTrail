using System.Globalization;
using ScreenTrail.Core.Capabilities;
using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Core.Hud;

/// <summary>Which design token colours the pill's left segment. Never the only signal (Spec §7).</summary>
public enum HudTone
{
    /// <summary>Capture is running. <c>state.recording</c>.</summary>
    Recording,

    /// <summary>Capture is stopped and will resume. <c>state.paused</c>.</summary>
    Paused,

    /// <summary>In a session, deliberately not capturing this window. <c>state.scope</c>.</summary>
    Scope,

    /// <summary>Nothing is wrong and nothing is happening. <c>text.muted</c>.</summary>
    Idle,
}

/// <param name="Glyph">Spec §7: the shape carries the state as well as the colour does. One of
/// <see cref="HudGlyphs"/>: a vector shape the pill draws, or the name of an icon in Theme/Icons.xaml, never a
/// character (review finding C-H3).</param>
/// <param name="Text">The left segment, in the technician's words.</param>
/// <param name="Tooltip">Longer wording on hover; null when the text says everything.</param>
public sealed record HudSegment(HudTone Tone, string Glyph, string Text, string? Tooltip = null);

/// <summary>
/// The pill's state marks (design system 8 and 10.10, review finding C-H3). Recording is an 8 px dot and not
/// recording an 8 px ring, both drawn as vectors; finishing and drafting are the progress ring; the rest are
/// Fluent icons by concept. Unicode symbols rendered in the text face at the wrong weight, could fall back to
/// the emoji font, and auto-paused (⏸) looked like a second Pause button beside the real one.
/// </summary>
public static class HudGlyphs
{
    public const string Dot = "dot";
    public const string Ring = "ring";
    public const string Busy = "busy";
    public const string Pause = "Icon.Pause";
    public const string Lock = "Icon.Lock";
    public const string Hide = "Icon.Hide";
    public const string Unknown = "Icon.Unknown";
    public const string Mic = "Icon.Mic";
    public const string Cloud = "Icon.Cloud";
}

/// <param name="Mic">Null when a microphone is present and working; the meter is shown instead.</param>
/// <param name="Cloud">Null when online. Spec §5 S2's grey cloud otherwise.</param>
public sealed record HudSnapshot(
    HudSegment State,
    HudSegment? Mic,
    HudSegment? Cloud,
    int PendingRedactions,
    bool Visible)
{
    /// <summary>How long the session has recorded, when it is recording; null otherwise.</summary>
    public long? RecordingMs { get; init; }

    /// <summary>
    /// How long the session has run, while one runs: recording, paused or suppressed, frozen while paused.
    /// Null outside a session. The pill shows it in its own place after the state word in every one of
    /// those states, so the session's length never vanishes while the technician decides whether to resume
    /// (review finding C-H4).
    /// </summary>
    public long? SessionMs { get; init; }

    /// <summary>The session's clock, mm:ss or h:mm:ss; null outside a session.</summary>
    public string? Timer => SessionMs is { } ms ? HudState.Elapsed(ms) : null;

    /// <summary>
    /// What a screen reader says for the pill (Spec §7: "Recording, 12 minutes 41 seconds, 3 items
    /// redacted"). The pill's words for every other state, then the microphone, the connection and the
    /// redaction backlog when they are worth saying. It was the fixed words "Capture state" until ST-084,
    /// which told Narrator nothing about the one thing the pill is for.
    /// </summary>
    public string Spoken
    {
        get
        {
            var parts = new List<string> { Announcement };
            if (SessionMs is { } ms)
            {
                parts.Add(HudState.SpokenDuration(ms));
            }

            if (PendingRedactions > 0)
            {
                // "Masking", the word the tooltip and the rest of the product use (review finding E-05).
                parts.Add(PendingRedactions == 1 ? "masking 1 screenshot" : string.Create(CultureInfo.InvariantCulture, $"masking {PendingRedactions} screenshots"));
            }

            if (Mic is { } mic)
            {
                parts.Add(mic.Tooltip ?? mic.Text);
            }

            if (Cloud is { } cloud)
            {
                parts.Add(cloud.Tooltip ?? cloud.Text);
            }

            return string.Join(", ", parts);
        }
    }

    /// <summary>
    /// The state without the clock: what is announced when it changes. The clock changes every second and
    /// is read when the technician asks, never interrupting them.
    /// </summary>
    public string Announcement => RecordingMs is not null ? "Recording" : State.Tooltip is { } why ? $"{State.Text}. {why}" : State.Text;
}

/// <summary>
/// What the recording pill says (ST-072, Spec §5 S2).
///
/// Derived from the service's own state, the capability probe and connectivity: never stored, and never
/// guessed at by the UI. INV-4 makes this the most safety-critical string in the product: it is the thing
/// a technician looks at to know whether a customer's screen is being recorded, and the one place where
/// being out of date is indistinguishable from lying.
///
/// So when the pieces disagree, this errs towards saying capture is happening. A HUD that wrongly says
/// "recording" costs a moment of confusion; one that wrongly says "paused" is the silent-capture path the
/// whole design forbids.
/// </summary>
public static class HudState
{
    /// <param name="capture">The last thing the service said. Null when the UI has never heard from it.</param>
    /// <param name="capabilities">What Windows currently allows (ST-021).</param>
    /// <param name="online">Whether a cloud draft could be requested right now.</param>
    /// <param name="hidden">The technician right-clicked the pill away for this session.</param>
    public static HudSnapshot For(
        CaptureStateSnapshot? capture,
        CapabilityReport? capabilities = null,
        bool online = true,
        bool hidden = false)
    {
        var state = Segment(capture);

        return new HudSnapshot(
            state,
            Mic(capabilities),
            online ? null : new HudSegment(HudTone.Idle, HudGlyphs.Cloud, "Offline", "Offline: draft will be created when connected."),
            capture?.PendingRedactions ?? 0,

            // Hidden only ever hides the pill, and only while capture is known to be off. Spec v0.4.1 Q1
            // settled that the HUD stays up during screen sharing, because an auto-hiding indicator is a
            // silent-capture path wearing a convenience's clothes. The tray icon remains either way.
            Visible: !hidden || !KnownIdle(capture))
        {
            RecordingMs = capture?.State == CaptureStates.Recording ? Math.Max(0, capture.ElapsedMs ?? 0) : null,
            SessionMs = capture switch
            {
                { State: CaptureStates.Recording } => Math.Max(0, capture.ElapsedMs ?? 0),
                { State: CaptureStates.Paused or CaptureStates.Suppressed, ElapsedMs: { } ms } => Math.Max(0, ms),
                _ => null,
            },
        };
    }

    /// <summary>"12 minutes 41 seconds", the clock in words for a screen reader.</summary>
    public static string SpokenDuration(long milliseconds)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        static string Unit(int n, string unit) => string.Create(CultureInfo.InvariantCulture, $"{n} {unit}{(n == 1 ? string.Empty : "s")}");
        var parts = new List<string>();
        if (span.TotalHours >= 1)
        {
            parts.Add(Unit((int)span.TotalHours, "hour"));
        }

        if (span.TotalMinutes >= 1)
        {
            parts.Add(Unit(span.Minutes, "minute"));
        }

        parts.Add(Unit(span.Seconds, "second"));
        return string.Join(' ', parts);
    }

    /// <summary>
    /// Whether the service has positively said that nothing is being captured.
    ///
    /// The distinction the rest of this class turns on, applied to the one decision that can take the
    /// indicator off the screen. Not-recording and don't-know both draw a grey pill, and only the first
    /// of them may be dismissed: a technician who hides the pill between sessions is asking for quiet,
    /// not for the indicator to stay gone through a dropped pipe, a service restart, or a session that
    /// began while the UI was reconnecting (ST-048, weaknesses P0-3).
    ///
    /// A session that has ended rests in draft_ready or draft_failed, not idle: the machine stays there
    /// until something discards the draft or the service restarts. Both are the service saying, in so
    /// many words, that it is not capturing, so both count (2026-10-12 review, S-39). Without them the
    /// pill warned "may be recording" after every session and could not be put away.
    ///
    /// An unrecognised state string counts as unknown for the same reason. A service newer than this UI
    /// is exactly the case where guessing "idle" is least defensible.
    /// </summary>
    public static bool KnownIdle(CaptureStateSnapshot? capture) =>
        capture is not null && capture.State is CaptureStates.Idle or CaptureStates.DraftReady or CaptureStates.DraftFailed or CaptureStates.DraftPending;

    /// <summary>
    /// Whether the UI may tell the service that the indicator is on screen (INV-4, <c>indicator_showing</c>).
    ///
    /// Decided here, beside <see cref="For"/>, because the service asks this before it starts a session
    /// as well as during one, and the two answers have to agree. The pill up means the claim is about the
    /// pill, and it holds only when the pill is somewhere a monitor is: a pill restored onto a screen that
    /// has since been unplugged is drawn into empty coordinate space and indicates nothing (ST-072).
    ///
    /// The pill down is only ever the technician's own doing while capture is known to be off, and the
    /// tray icon stays, which is the tray-only arrangement INV-4 allows when it is chosen. Reporting goes
    /// on in that state. It used to stop, and since the service refuses to start a session nothing
    /// indicates, hiding the pill between sessions refused every later start, by hotkey or by a remote
    /// tool taking the foreground, until the UI was restarted (2026-10-12 review, T-154). The pill comes
    /// back the moment the session starts, because <see cref="For"/> will not hide it then.
    /// </summary>
    /// <param name="hud">What the pill is showing, from <see cref="For"/>.</param>
    /// <param name="pillOnScreen">Whether the pill has a size and its middle lands on a real monitor.</param>
    public static bool Indicates(HudSnapshot hud, bool pillOnScreen)
    {
        ArgumentNullException.ThrowIfNull(hud);
        return !hud.Visible || pillOnScreen;
    }

    /// <summary>
    /// The session the service says is under way, or null between sessions and whenever that is not known.
    ///
    /// The shell clears its "hide the pill" wish when this names a session it has not seen running, because
    /// hiding is for one session and no longer (Spec §5 S2). The tray's item used to set the wish for the
    /// life of the process, so one click put the pill away after every later session too (T-154).
    /// </summary>
    public static string? SessionUnderWay(CaptureStateSnapshot? capture) =>
        capture is { State: CaptureStates.Recording or CaptureStates.Paused or CaptureStates.Suppressed, SessionId: { Length: > 0 } id }
            ? id
            : null;

    /// <summary>mm:ss, or h:mm:ss once a session runs past the hour.</summary>
    public static string Elapsed(long? milliseconds)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds ?? 0));
        return span.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{span.Minutes:00}:{span.Seconds:00}");
    }

    private static HudSegment Segment(CaptureStateSnapshot? capture)
    {
        if (capture is null)
        {
            // The UI has not heard from the service. It cannot say capture is off, because it does not
            // know, and "unknown" is the honest word for the state INV-4 cares most about.
            return new HudSegment(
                HudTone.Idle,
                HudGlyphs.Unknown,
                "Capture state unknown",
                "Not connected to the capture service. It may still be recording.");
        }

        return capture.State switch
        {
            // The word, always: the clock has its own place after it (SessionMs, review finding C-H4).
            CaptureStates.Recording => new HudSegment(HudTone.Recording, HudGlyphs.Dot, "Recording"),

            CaptureStates.Paused => Paused(capture),

            // Suppression is never the technician's doing, so it always says what caused it: they have not
            // pressed anything, and a pill that just says "paused" invites them to look for what they did.
            CaptureStates.Suppressed => Suppressed(capture),

            CaptureStates.Finalizing => new HudSegment(HudTone.Paused, HudGlyphs.Busy, "Finishing up"),

            CaptureStates.Idle => new HudSegment(HudTone.Idle, HudGlyphs.Ring, "Not recording"),

            // Where a session rests once it is over. Not capturing, and said so: these used to fall
            // through to "unknown, may be recording", which the pill then showed after every session and
            // refused to put away (S-39). The words agree with the tray's, which says the same two things.
            CaptureStates.DraftReady => new HudSegment(HudTone.Idle, HudGlyphs.Ring, "Not recording: draft ready", "Nothing is being captured. The note is ready to review."),

            CaptureStates.DraftFailed => new HudSegment(HudTone.Idle, HudGlyphs.Ring, "Not recording: draft failed", "Nothing is being captured. The note could not be drafted; open ScreenTrail to see why."),

            // The session is over and its note is on its way from the backend (ST-013).
            CaptureStates.DraftPending => new HudSegment(HudTone.Idle, HudGlyphs.Busy, "Not recording: drafting the note", "Nothing is being captured. The note is being drafted and appears in Review when it arrives."),

            // A state this build has never heard of: a service newer than this UI. KnownIdle already
            // says why guessing "idle" is least defensible here, and this is the same reasoning applied
            // to the words on the pill rather than to whether it is shown (2026-09-19 review).
            _ => new HudSegment(
                HudTone.Idle,
                HudGlyphs.Unknown,
                "Capture state unknown",
                "This ScreenTrail is older than the capture service. It may be recording."),
        };
    }

    private static HudSegment Paused(CaptureStateSnapshot capture) =>
        capture.Reason is CaptureReasons.User or null
            // The word alone, so it never trims; the chord, as it is bound, in the tooltip and the spoken
            // name (review finding E-03, design system 10.10).
            ? new HudSegment(HudTone.Paused, HudGlyphs.Pause, "Paused", Input.HotkeyText.For(Input.HotkeyAction.PauseOrResume) is { Length: > 0 } chord ? $"Press {chord} to resume." : null)
            : Suppressed(capture);

    private static HudSegment Suppressed(CaptureStateSnapshot capture) => capture.Reason switch
    {
        // The words are SuppressionWords', which the tray and the shell say too (review finding E-02).
        CaptureReasons.ElevatedWindow => new HudSegment(
            HudTone.Paused,
            HudGlyphs.Lock,
            Shell.SuppressionWords.Live(capture.Reason),

            // Spec v0.4.1 Q5 fixes this wording, so it stays though the state word is now "admin window"
            // (E-02). It explains a limitation rather than implying a choice: Windows will not let us
            // capture an elevated window, and the technician cannot fix that.
            "Elevated window: screen not captured."),

        CaptureReasons.OutOfScope => new HudSegment(
            HudTone.Scope,
            HudGlyphs.Hide,
            Shell.SuppressionWords.Live(capture.Reason, capture.ScopeProcess),
            "Clicks are still on the timeline; no screenshots are taken."),

        _ => new HudSegment(HudTone.Paused, HudGlyphs.Lock, Shell.SuppressionWords.Live(capture.Reason, capture.ScopeProcess)),
    };

    private static HudSegment? Mic(CapabilityReport? capabilities)
    {
        if (capabilities is null)
        {
            return null;
        }

        // FirstOrDefault, not the indexer: a report from a newer or older service may not carry the row,
        // and the pill must not throw over a segment that is a nicety.
        var microphone = capabilities.Checks.FirstOrDefault(c => c.Capability == Capability.Microphone);
        return microphone is null || microphone.State is CapabilityState.Ok
            ? null
            : new HudSegment(HudTone.Idle, HudGlyphs.Mic, "No mic", "No microphone. Capture continues without voice.");
    }
}

/// <summary>
/// The suppression reasons as they appear on the wire, matching <c>CaptureStateReason</c> in
/// session.v1.json. Constants rather than the generated enum, because the IPC snapshot carries a string
/// and an unrecognised one has to fall through to something sensible rather than throw at a technician.
/// </summary>
public static class CaptureReasons
{
    public const string User = "user";
    public const string PasswordField = "password_field";
    public const string ExcludedApp = "excluded_app";
    public const string ElevatedWindow = "elevated_window";
    public const string SensitiveContext = "sensitive_context";
    public const string OutOfScope = "out_of_scope";
}
