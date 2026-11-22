using System.Globalization;
using System.Text;
using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Core.Shell;

/// <param name="Label">The left column. Fixed wording, never derived from anything captured.</param>
public sealed record DiagnosticsLine(string Label, string Value);

/// <summary>What the diagnostics panel's headline says about capture, drawn with a glyph beside its words.</summary>
public enum DiagnosticsState
{
    /// <summary>The service did not answer: nothing is known.</summary>
    Unknown,

    /// <summary>A remote session is in front and being captured.</summary>
    Capturing,

    /// <summary>Capture is suppressed (a password field, an excluded app, an elevated window).</summary>
    Paused,

    /// <summary>Nothing is being captured: no session in front, or the window is out of scope.</summary>
    NotCapturing,
}

/// <summary>
/// What the "What's being captured right now?" panel says, row by row (ST-071, Spec §5 S1), including
/// when the service cannot be asked.
///
/// The panel is the thing a technician turns round and shows a customer. When the service is not
/// answering it used to show a <see cref="Diagnostics"/> built from zeros, so the customer read "0 frames
/// dropped" and "Local-only: no", neither of which anybody knew (2026-10-12 review, T-152). "We cannot
/// ask" and "none" are different claims, and only the first is true then. So an unanswered panel says
/// unknown on every row, here, where it can be tested without a window (ADR-0002).
///
/// Every row is built from <see cref="Diagnostics"/>, which has nowhere to put content, so the panel
/// cannot show a window title or a line of OCR even by mistake, and neither can the clipboard text (INV-10).
/// </summary>
public sealed class DiagnosticsPanel
{
    public const string Unknown = "unknown";

    public const string NotConnected = "Not connected to the capture service. Capture state unknown.";

    /// <summary>The row for the title-change hook's fallbacks (ST-031).</summary>
    public const string HookFallbacks = "Hook fallbacks";

    /// <summary>
    /// What a fallback means, said beside a count above zero so a technician or an admin can tell whether
    /// it matters: nothing is missed, the idle service only does more work. At zero there is nothing to say.
    /// </summary>
    public const string HookFallbacksMeaning = "watched every window's title instead of only the one in front; nothing missed, a little more idle CPU";

    /// <summary>The processor row (ST-031).</summary>
    public const string Cpu = "CPU";

    /// <summary>The memory row.</summary>
    public const string Memory = "Memory";

    /// <summary>What the processor row says before the service has two readings to give a share from.</summary>
    public const string Measuring = "measuring";

    /// <summary>
    /// The row that says capture has given way to keep the PC responsive (ST-031), shown only then, in
    /// words about the screenshots a technician or a customer can see change, never the throttle's names.
    /// </summary>
    public const string Capture = "Capture";

    /// <summary>What the copied text says about capture when it has not given way.</summary>
    public const string CaptureAsUsual = "as usual";

    /// <summary>
    /// How often the open panel asks the service again, so CPU, memory and the rest are live while it is
    /// on screen. Only while it is open: the idle service is not woken for a panel nobody is looking at.
    /// </summary>
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(2);

    private readonly Diagnostics? _diagnostics;

    private DiagnosticsPanel(Diagnostics? diagnostics)
    {
        _diagnostics = diagnostics;
        Rows = diagnostics is null ? UnansweredRows() : AnsweredRows(diagnostics);
    }

    /// <summary>The panel when the service is not answering. Every count is unknown rather than zero.</summary>
    public static DiagnosticsPanel Unanswered { get; } = new(null);

    public static DiagnosticsPanel From(Diagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new DiagnosticsPanel(diagnostics);
    }

    /// <summary>The panel for one answer to <c>get_diagnostics</c>, or for none: every row unknown (T-152).</summary>
    public static DiagnosticsPanel FromReply(DiagnosticsReported? reported) =>
        reported is null ? Unanswered : From(Diagnostics.From(reported));

    /// <summary>
    /// The processor row's value: the service's share of the whole PC, as Task Manager counts it, so a
    /// technician can hold the two side by side. "measuring" until there is a reading to give.
    /// </summary>
    public static string CpuValue(double? percent) => percent is { } value
        ? value.ToString("0.0", CultureInfo.InvariantCulture) + "% of this PC"
        : Measuring;

    public static string MemoryValue(long workingSetBytes) =>
        (Math.Max(0, workingSetBytes) / (1024 * 1024)).ToString(CultureInfo.InvariantCulture) + " MB";

    /// <summary>
    /// What capture has given up to stay inside the processor budget, in words a customer can read over a
    /// shoulder, or null when it has given up nothing. Click screenshots are never skipped, so every step
    /// short of the last says "between clicks". A level this build does not know still says that capture
    /// has eased off rather than nothing.
    /// </summary>
    public static string? CaptureWords(string? level) => level switch
    {
        null or CaptureLevels.Full => null,
        CaptureLevels.FewerSamples => "Taking fewer screenshots between clicks to keep your PC responsive",
        CaptureLevels.FewestSamples => "Taking far fewer screenshots between clicks to keep your PC responsive",
        CaptureLevels.OcrOneAtATime => "Taking far fewer screenshots between clicks and checking them one at a time to keep your PC responsive",
        CaptureLevels.OcrClicksOnly => "Taking screenshots only on clicks and marked moments to keep your PC responsive",
        _ => "Taking fewer screenshots to keep your PC responsive",
    };

    /// <summary>Whether the service answered. False means every row below says unknown.</summary>
    public bool Answered => _diagnostics is not null;

    /// <summary>What the panel says about capture, in one line a customer can read over a shoulder.</summary>
    public string Headline => _diagnostics?.Headline ?? NotConnected;

    public IReadOnlyList<DiagnosticsLine> Rows { get; }

    /// <summary>
    /// The headline as a state, so the panel draws it with its glyph and colour beside the words (Spec
    /// §1.1, §7). Paused when capture is suppressed; not capturing when the scope decision says so (its
    /// words begin "Not capturing", as ScopePolicy and ServiceDiagnostics write them); unknown unanswered.
    /// </summary>
    public DiagnosticsState State => _diagnostics switch
    {
        null => DiagnosticsState.Unknown,
        { Suppression: not null } => DiagnosticsState.Paused,
        { Scope: var scope } when scope.StartsWith("Not capturing", StringComparison.Ordinal) => DiagnosticsState.NotCapturing,
        _ => DiagnosticsState.Capturing,
    };

    /// <summary>Whether local-only mode is on (Spec §5 S1's badge), or null when the service did not answer.</summary>
    public bool? LocalOnly => _diagnostics?.LocalOnly;

    /// <summary>Said beside Copy diagnostics once it has run.</summary>
    public const string Copied = "Copied to the clipboard: states and counts only, no screen text.";

    /// <summary>
    /// The clipboard text. The service's own when it answered; when it did not, a note saying so and
    /// nothing that reads as a measurement.
    /// </summary>
    public string ToClipboardText(DateTimeOffset at)
    {
        if (_diagnostics is { } diagnostics)
        {
            return diagnostics.ToClipboardText(at);
        }

        var text = new StringBuilder()
            .Append("ScreenTrail diagnostics ").Append(at.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture)).AppendLine()
            .AppendLine(NotConnected);
        foreach (var row in Rows)
        {
            text.Append(row.Label.ToLowerInvariant().PadRight(17)).AppendLine(row.Value);
        }

        return text.ToString();
    }

    /// <summary>The row an installed copy with no machine-wide backend address shows (contract v12), and only then.</summary>
    public const string Backend = "Backend";

    private static IReadOnlyList<DiagnosticsLine> AnsweredRows(Diagnostics d) => d.BackendNote is { } note
        ? [.. EveryAnsweredRow(d), new(Backend, note)]
        : EveryAnsweredRow(d);

    private static IReadOnlyList<DiagnosticsLine> EveryAnsweredRow(Diagnostics d) => CaptureWords(d.CaptureLevel) is { } eased
        ? [new("Scope", d.Scope), new(Capture, eased), .. RowsAfterScope(d)]
        : [new("Scope", d.Scope), .. RowsAfterScope(d)];

    private static IReadOnlyList<DiagnosticsLine> RowsAfterScope(Diagnostics d) =>
    [
        new("Microphone", d.Microphone ?? "none"),
        new("Suppressed", d.Suppression ?? "no"),
        new("Redaction queue", d.RedactionBacklog.ToString(CultureInfo.InvariantCulture)),
        new("Frames dropped", d.FramesDropped.ToString(CultureInfo.InvariantCulture)),
        new("Keystrokes held", d.KeystrokesDropped.ToString(CultureInfo.InvariantCulture)),
        new("Egress blocked", d.EgressBlocked.ToString(CultureInfo.InvariantCulture)),
        new(HookFallbacks, HookFallbacksValue(d.NameHookRefusals)),
        new("Local-only", d.LocalOnly ? "yes" : "no"),
        new("Policy", d.PolicyVersion),
        new(Cpu, CpuValue(d.CpuPercent)),
        new(Memory, MemoryValue(d.WorkingSetBytes)),
        new("Client", d.ClientVersion),
    ];

    /// <summary>The count, unknown when the service could not know it, and what it means when it is not 0.</summary>
    private static string HookFallbacksValue(long? refusals) => refusals switch
    {
        null => Unknown,
        0 => "0",
        { } count => $"{count.ToString(CultureInfo.InvariantCulture)} ({HookFallbacksMeaning})",
    };

    private static IReadOnlyList<DiagnosticsLine> UnansweredRows() =>
    [
        new("Scope", "Not connected to the capture service"),
        new("Microphone", Unknown),
        new("Suppressed", Unknown),
        new("Redaction queue", Unknown),
        new("Frames dropped", Unknown),
        new("Keystrokes held", Unknown),
        new("Egress blocked", Unknown),
        new(HookFallbacks, Unknown),
        new("Local-only", Unknown),
        new("Policy", Unknown),
        new(Cpu, Unknown),
        new(Memory, Unknown),
        new("Client", Unknown),
    ];
}
