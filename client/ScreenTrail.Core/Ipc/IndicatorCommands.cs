using ScreenTrail.Core.Audit;
using ScreenTrail.Core.Store;
using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Core.Ipc;

/// <summary>
/// What the service believes of an <c>indicator_showing</c> report (security review B-03, INV-4).
///
/// A report used to be believed from any connection, with its place on screen dropped on the way in. It is
/// the one command that turns "a connection" into "a pill on screen", and nothing checked it. Now the
/// service checks what it can:
/// <list type="bullet">
/// <item>One connection reports the indicator at a time. Another is refused while that one has a current
/// report, and takes over only once it has closed or stopped reporting; the audit log records each change
/// of connection (<c>indicator_source_changed</c>, a label only).</item>
/// <item>A pill (a report with an area) is believed only where the process the connection was verified as
/// has a visible, uncloaked top-level window on a monitor at the reported rectangle, give or take
/// <see cref="PillTolerance"/> pixels. The pill put away (zero size) needs no window: there is none to find.</item>
/// </list>
/// A report that is refused is not recorded: whatever the connection reported before still expires on its
/// own. This does not stop code running inside the real UI, which owns the real window.
/// </summary>
/// <param name="windowRequired">
/// Whether a pill needs a window of the verified process. The service sets it; a connection whose
/// verifier held no process then has no pill. Off for hosts that verify nothing (tests over plain pipes).
/// </param>
/// <param name="audit">Where a change of reporting connection is recorded; null records nothing.</param>
/// <param name="sessionId">The session under way, for the audit row; null between sessions.</param>
public sealed class IndicatorCommands(IndicatorReports reports, bool windowRequired, IAuditLog? audit = null, Func<string?>? sessionId = null)
{
    /// <summary>
    /// How far, in pixels on any edge, a window may be from the reported rectangle and still be the pill.
    /// The UI reads its window's rectangle a moment before the service asks Windows, and a pill being
    /// dragged moves in between.
    /// </summary>
    public const int PillTolerance = 8;

    /// <summary>What a refused report is answered with. The UI does not show it; the guard does the rest.</summary>
    public const string NotThePill = "The recording pill was not found on screen where it was reported.";

    /// <summary>What a report from a connection that is not the reporting one is answered with.</summary>
    public const string AnotherReports = "Another connection is reporting the recording indicator.";

    private readonly IndicatorReports _reports = reports ?? throw new ArgumentNullException(nameof(reports));
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, IVerifiedPeer?> _peers = [];
    private Guid? _source;
    private bool _sourceClosed;

    /// <summary>A connection passed the handshake; <paramref name="peer"/> is the process it was verified as.</summary>
    public void Opened(Guid connection, IVerifiedPeer? peer)
    {
        lock (_gate)
        {
            _peers[connection] = peer;
        }
    }

    /// <summary>
    /// A connection ended. What it reported is withdrawn at once (security review A-09): its pill and tray
    /// icon went with it, and the report used to be believed until it expired, so the guard's grace only
    /// began six seconds later. Its peer is let go of by the server straight after this.
    /// </summary>
    public void Closed(Guid connection)
    {
        lock (_gate)
        {
            _reports.Forget(connection);
            _ = _peers.Remove(connection);
            if (_source == connection)
            {
                _sourceClosed = true;
            }
        }
    }

    public async Task<CommandResult> ReportAsync(Guid connection, IndicatorShowingCommand showing, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(showing);
        IndicatorSourceChange? changed = null;
        lock (_gate)
        {
            if (_source is { } source && source != connection && !_sourceClosed && _reports.Current(source))
            {
                return Refused(showing, AnotherReports);
            }

            if (IsPill(showing) && windowRequired)
            {
                _ = _peers.TryGetValue(connection, out var peer);
                if (peer is null || !Shown(showing, peer.Windows()))
                {
                    return Refused(showing, NotThePill);
                }
            }

            _reports.Report(connection, showing.X, showing.Y, showing.Width, showing.Height);
            if (_source != connection)
            {
                if (_source is not null)
                {
                    changed = _sourceClosed ? IndicatorSourceChange.AfterTheLastClosed : IndicatorSourceChange.AfterTheLastLapsed;
                }

                _source = connection;
                _sourceClosed = false;
            }
        }

        if (changed is { } change && audit is not null)
        {
            await audit.RecordAsync(AuditTypes.IndicatorSourceChanged, sessionId?.Invoke(), detail: AuditDetail.Of(change), ct: ct).ConfigureAwait(false);
        }

        return new CommandResult { RequestId = showing.RequestId, Ok = true };
    }

    /// <summary>
    /// Whether one of the windows is on screen where the report says: shown, not minimised, not cloaked, on a
    /// monitor, and each edge within <see cref="PillTolerance"/> of the reported one.
    /// </summary>
    public static bool Shown(IndicatorShowingCommand showing, IEnumerable<PeerWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(showing);
        ArgumentNullException.ThrowIfNull(windows);
        if (!IsPill(showing) || !double.IsFinite(showing.X) || !double.IsFinite(showing.Y))
        {
            return false;
        }

        var right = showing.X + showing.Width;
        var bottom = showing.Y + showing.Height;
        return windows.Any(w => w.Visible && !w.Cloaked && w.OnAMonitor && !w.Bounds.IsEmpty
            && Near(w.Bounds.Left, showing.X) && Near(w.Bounds.Top, showing.Y)
            && Near(w.Bounds.Right, right) && Near(w.Bounds.Bottom, bottom));
    }

    private static bool Near(int edge, double reported) => Math.Abs(edge - reported) <= PillTolerance;

    /// <summary>A rectangle something could be drawn in, as <see cref="IndicatorReports"/> reads it.</summary>
    private static bool IsPill(IndicatorShowingCommand showing) =>
        showing.Width > 0 && showing.Height > 0 && double.IsFinite(showing.Width) && double.IsFinite(showing.Height);

    private static CommandResult Refused(IndicatorShowingCommand showing, string why) =>
        new() { RequestId = showing.RequestId, Ok = false, Error = why };
}
