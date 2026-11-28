namespace ScreenTrail.Core.Ipc;

/// <summary>
/// What the service knows about whether anything on screen says capture is happening (INV-4, ST-085).
///
/// It used to know one thing: how many clients were connected to the pipe. <b>A connection is not a
/// pill.</b> The window could be positioned on a monitor that had since been unplugged, the tray icon
/// could be in Windows 11's overflow flyout, and the UI could have frozen while still holding the socket
/// open, and in all three the service went on recording a customer's screen while counting itself
/// indicated (2026-09-20 review).
///
/// A report is the UI saying "I am showing the pill", repeated while it is true. Reports expire, so the
/// interesting case (a UI that has stopped painting but not stopped running) stops counting on its own.
///
/// A report with a zero width and height says something narrower: the technician chose tray-only between
/// sessions, and the pill returns as a session starts (owner decision, 2026-10-25). It is accepted here
/// like any other, so a session the service starts by itself is not refused for want of a pill the
/// technician put away. It is not a pill, and <see cref="PillShowing"/> says so: once a session runs,
/// the guard counts only a report with the pill's rectangle, gives the pill its grace from the start to
/// come back, and suppresses after that. So a session that starts on the pill put away runs without a pill
/// for at most that grace, not one report's lifetime plus it. A pill that was reported and then stops
/// being reported still counts until its report expires, so for a pill lost during a session the bound is
/// still one report's lifetime plus the grace.
///
/// <b>What this is not.</b> This class keeps what was reported and lets it expire; it does not judge a
/// claim. The service checks a claim before it reaches here (<see cref="IndicatorCommands"/>, security
/// review B-03): a pill is believed only from the connection that is reporting, and only where the process
/// that connection was verified as has a visible window at the reported place. That does not stop code
/// running inside the real UI, which can draw what it likes.
/// </summary>
public sealed class IndicatorReports(TimeProvider? time = null)
{
    /// <summary>
    /// How long a report counts for.
    ///
    /// Longer than the UI's reporting interval by enough that one missed message is not a suppressed
    /// session, and short enough that a frozen UI is noticed within a few seconds rather than a few
    /// minutes. The guard's own grace period runs after this, so the two add up.
    /// </summary>
    public static readonly TimeSpan GoodFor = TimeSpan.FromSeconds(6);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, (DateTimeOffset At, bool Pill, ReportedRectangle Rectangle)> _reports = [];

    /// <summary>Whether anything is currently claiming to show the indicator.</summary>
    public bool Showing
    {
        get
        {
            lock (_gate)
            {
                Forget();
                return _reports.Count > 0;
            }
        }
    }

    /// <summary>How many reports are being remembered. For the test that says they do not accumulate.</summary>
    public int Tracked
    {
        get
        {
            lock (_gate)
            {
                return _reports.Count;
            }
        }
    }

    /// <summary>
    /// Whether a current report has the pill's own rectangle: the pill up and on a monitor, rather than
    /// put away. What a running session needs.
    /// </summary>
    public bool PillShowing
    {
        get
        {
            lock (_gate)
            {
                Forget();
                return _reports.Values.Any(report => report.Pill);
            }
        }
    }

    /// <summary>
    /// One window saying it is showing the indicator, now, with the pill's size.
    ///
    /// Only a rectangle with an area is the pill. Anything else, zero, negative or not a number, is read
    /// as the pill put away, which is the reading that stops counting once a session runs. A window's
    /// latest report replaces its last, so a pill followed by a zero-size report is the pill gone.
    /// </summary>
    public void Report(Guid window, double width, double height) => Report(window, 0, 0, width, height);

    /// <summary>One window's report with where the pill is, as <c>indicator_showing</c> carries it.</summary>
    public void Report(Guid window, double x, double y, double width, double height)
    {
        lock (_gate)
        {
            Forget();
            _reports[window] = (_time.GetUtcNow(), width > 0 && height > 0, new ReportedRectangle(x, y, width, height));
        }
    }

    /// <summary>The rectangle a window last reported, while that report is current; null otherwise.</summary>
    public ReportedRectangle? LastReported(Guid window)
    {
        lock (_gate)
        {
            Forget();
            return _reports.TryGetValue(window, out var report) ? report.Rectangle : null;
        }
    }

    /// <summary>Whether a window has a report that has not expired, the pill or the pill put away.</summary>
    public bool Current(Guid window)
    {
        lock (_gate)
        {
            Forget();
            return _reports.ContainsKey(window);
        }
    }

    /// <summary>
    /// That window has gone.
    ///
    /// The pipe notices a disconnect sooner than the report would have expired, and a window that has
    /// closed is not showing anything. Waiting for the expiry instead would leave a few seconds in which
    /// the service believed a pill that is not there.
    /// </summary>
    public void Forget(Guid window)
    {
        lock (_gate)
        {
            _ = _reports.Remove(window);
        }
    }

    /// <summary>
    /// Drops what has expired.
    ///
    /// On every use rather than on a timer: there are at most a handful of entries, and a client that
    /// reported once and vanished without a clean disconnect would otherwise be remembered for the life
    /// of the service. Always under the lock.
    /// </summary>
    private void Forget()
    {
        if (_reports.Count == 0)
        {
            return;
        }

        var stale = _time.GetUtcNow() - GoodFor;
        foreach (var window in _reports.Where(entry => entry.Value.At <= stale).Select(entry => entry.Key).ToList())
        {
            _ = _reports.Remove(window);
        }
    }
}

/// <summary>The rectangle an <c>indicator_showing</c> report carried, as it was sent.</summary>
public readonly record struct ReportedRectangle(double X, double Y, double Width, double Height);
