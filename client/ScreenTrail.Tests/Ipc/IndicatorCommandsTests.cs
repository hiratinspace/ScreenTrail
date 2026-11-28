using ScreenTrail.Core.Audit;
using ScreenTrail.Core.Detection;
using ScreenTrail.Core.Ipc;
using ScreenTrail.Core.Store;
using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Tests.Ipc;

/// <summary>
/// What the service believes of an <c>indicator_showing</c> report (security review B-03, INV-4).
///
/// A report is a claim that the recording pill is on screen. The service now checks the part of it that it
/// can: the claim is believed only from the connection that is reporting the indicator, and only where the
/// process that connection was verified as has a visible window at the reported place. Rectangles here are
/// in the physical virtual-screen pixels the UI reports from its own window.
/// </summary>
public sealed class IndicatorCommandsTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 28, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The pill as the UI reports it: its window's own rectangle.</summary>
    private static readonly ScreenRect Pill = new(1500, 900, 1720, 936);

    [Fact]
    public async Task TheRectanglesPlaceReachesTheReports()
    {
        // X and Y used to be dropped on the way in, so nothing could ever be checked against them.
        var reports = new IndicatorReports(new ManualTime(At));
        var indicators = new IndicatorCommands(reports, windowRequired: false);
        var ui = Guid.NewGuid();
        indicators.Opened(ui, peer: null);

        Assert.True((await ReportAsync(indicators, ui, Pill)).Ok);

        Assert.Equal(new ReportedRectangle(1500, 900, 220, 36), reports.LastReported(ui));
    }

    [Fact]
    public async Task APillWhereTheVerifiedProcessHasNoWindowIsNotThePill()
    {
        // The finding's case: a connection that passed the handshake reports a rectangle where its process
        // has no window at all.
        var reports = new IndicatorReports(new ManualTime(At));
        var indicators = new IndicatorCommands(reports, windowRequired: true);
        var stranger = Guid.NewGuid();
        indicators.Opened(stranger, new FakePeer());

        var answer = await ReportAsync(indicators, stranger, new ScreenRect(0, 0, 1, 1));

        Assert.False(answer.Ok);
        Assert.False(reports.PillShowing);
        Assert.False(reports.Showing);
    }

    [Fact]
    public async Task APillWithNoVerifiedProcessIsNotThePillWhereAWindowIsRequired()
    {
        var reports = new IndicatorReports(new ManualTime(At));
        var indicators = new IndicatorCommands(reports, windowRequired: true);
        var ui = Guid.NewGuid();
        indicators.Opened(ui, peer: null);

        Assert.False((await ReportAsync(indicators, ui, Pill)).Ok);
        Assert.False(reports.PillShowing);
    }

    [Fact]
    public async Task APillMatchingAVisibleWindowOfTheVerifiedProcessIsThePill()
    {
        var reports = new IndicatorReports(new ManualTime(At));
        var indicators = new IndicatorCommands(reports, windowRequired: true);
        var ui = Guid.NewGuid();
        indicators.Opened(ui, new FakePeer(new PeerWindow(Pill, Visible: true, Cloaked: false, OnAMonitor: true)));

        Assert.True((await ReportAsync(indicators, ui, Pill)).Ok);
        Assert.True(reports.PillShowing);
    }

    [Fact]
    public async Task APillAFewPixelsFromWhereItWasReportedIsStillThePill()
    {
        // A pill being dragged moves between the UI reading its rectangle and the service asking Windows.
        var reports = new IndicatorReports(new ManualTime(At));
        var indicators = new IndicatorCommands(reports, windowRequired: true);
        var ui = Guid.NewGuid();
        var moved = new ScreenRect(Pill.Left + 3, Pill.Top - 2, Pill.Right + 3, Pill.Bottom - 2);
        indicators.Opened(ui, new FakePeer(new PeerWindow(moved, Visible: true, Cloaked: false, OnAMonitor: true)));

        Assert.True((await ReportAsync(indicators, ui, Pill)).Ok);
        Assert.True(reports.PillShowing);
    }

    public static TheoryData<string, PeerWindow> WindowsThatAreNotThePill => new()
    {
        { "elsewhere", new PeerWindow(new ScreenRect(100, 100, 320, 136), Visible: true, Cloaked: false, OnAMonitor: true) },
        { "another size", new PeerWindow(new ScreenRect(1500, 900, 1900, 1000), Visible: true, Cloaked: false, OnAMonitor: true) },
        { "hidden or minimised", new PeerWindow(Pill, Visible: false, Cloaked: false, OnAMonitor: true) },
        { "cloaked", new PeerWindow(Pill, Visible: true, Cloaked: true, OnAMonitor: true) },
        { "on no monitor", new PeerWindow(Pill, Visible: true, Cloaked: false, OnAMonitor: false) },
    };

    [Theory]
    [MemberData(nameof(WindowsThatAreNotThePill))]
    public async Task AWindowThatIsNotOnScreenWhereTheReportSaysIsNotThePill(string why, PeerWindow window)
    {
        var reports = new IndicatorReports(new ManualTime(At));
        var indicators = new IndicatorCommands(reports, windowRequired: true);
        var ui = Guid.NewGuid();
        indicators.Opened(ui, new FakePeer(window));

        Assert.False((await ReportAsync(indicators, ui, Pill)).Ok, why);
        Assert.False(reports.PillShowing, why);
    }

    [Fact]
    public async Task ThePillPutAwayNeedsNoWindow()
    {
        // Tray-only between sessions is a zero-size report, and there is no pill window to find.
        var reports = new IndicatorReports(new ManualTime(At));
        var indicators = new IndicatorCommands(reports, windowRequired: true);
        var ui = Guid.NewGuid();
        indicators.Opened(ui, new FakePeer());

        Assert.True((await ReportAsync(indicators, ui, new ScreenRect(0, 0, 0, 0))).Ok);
        Assert.True(reports.Showing);
        Assert.False(reports.PillShowing);
    }

    [Fact]
    public async Task ASecondConnectionIsNotBelievedWhileTheFirstIsReporting()
    {
        var reports = new IndicatorReports(new ManualTime(At));
        var indicators = new IndicatorCommands(reports, windowRequired: true);
        var ui = Guid.NewGuid();
        var other = Guid.NewGuid();
        var window = new PeerWindow(Pill, Visible: true, Cloaked: false, OnAMonitor: true);
        indicators.Opened(ui, new FakePeer(window));
        indicators.Opened(other, new FakePeer(window));
        Assert.True((await ReportAsync(indicators, ui, Pill)).Ok);

        var second = await ReportAsync(indicators, other, Pill);

        Assert.False(second.Ok);
        Assert.False(reports.Current(other));
        Assert.True(reports.Current(ui));
    }

    [Fact]
    public async Task AnotherConnectionTakesOverOnceTheFirstHasLapsedAndTheLogSaysSo()
    {
        var clock = new ManualTime(At);
        var reports = new IndicatorReports(clock);
        var audit = new RecordingAudit();
        var indicators = new IndicatorCommands(reports, windowRequired: false, audit, () => "s1");
        var ui = Guid.NewGuid();
        var other = Guid.NewGuid();
        indicators.Opened(ui, peer: null);
        indicators.Opened(other, peer: null);
        Assert.True((await ReportAsync(indicators, ui, Pill)).Ok);
        Assert.Empty(audit.Rows);

        clock.Advance(IndicatorReports.GoodFor + TimeSpan.FromSeconds(1));
        Assert.True((await indicators.ReportAsync(other, Showing(Pill), TestContext.Current.CancellationToken)).Ok);

        var row = Assert.Single(audit.Rows);
        Assert.Equal((AuditTypes.IndicatorSourceChanged, "s1", nameof(IndicatorSourceChange.AfterTheLastLapsed)), row);
    }

    [Fact]
    public async Task AConnectionThatClosedIsReplacedAndTheLogSaysSo()
    {
        var reports = new IndicatorReports(new ManualTime(At));
        var audit = new RecordingAudit();
        var indicators = new IndicatorCommands(reports, windowRequired: false, audit, () => null);
        var ui = Guid.NewGuid();
        var restarted = Guid.NewGuid();
        indicators.Opened(ui, peer: null);
        Assert.True((await ReportAsync(indicators, ui, Pill)).Ok);

        indicators.Closed(ui);
        reports.Forget(ui);
        indicators.Opened(restarted, peer: null);
        Assert.True((await indicators.ReportAsync(restarted, Showing(Pill), TestContext.Current.CancellationToken)).Ok);

        var row = Assert.Single(audit.Rows);
        Assert.Equal((AuditTypes.IndicatorSourceChanged, (string?)null, nameof(IndicatorSourceChange.AfterTheLastClosed)), row);
    }

    [Fact]
    public async Task TheSameConnectionReportingAgainWritesNothing()
    {
        var reports = new IndicatorReports(new ManualTime(At));
        var audit = new RecordingAudit();
        var indicators = new IndicatorCommands(reports, windowRequired: false, audit, () => "s1");
        var ui = Guid.NewGuid();
        indicators.Opened(ui, peer: null);

        for (var i = 0; i < 5; i++)
        {
            Assert.True((await ReportAsync(indicators, ui, Pill)).Ok);
        }

        Assert.Empty(audit.Rows);
    }

    private static IndicatorShowingCommand Showing(ScreenRect rect) => new()
    {
        RequestId = 1,
        X = rect.Left,
        Y = rect.Top,
        Width = rect.Right - rect.Left,
        Height = rect.Bottom - rect.Top,
    };

    private static Task<CommandResult> ReportAsync(IndicatorCommands indicators, Guid connection, ScreenRect rect) =>
        indicators.ReportAsync(connection, Showing(rect), TestContext.Current.CancellationToken);

    private sealed class FakePeer(params PeerWindow[] windows) : IVerifiedPeer
    {
        public IReadOnlyList<PeerWindow> Windows() => windows;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingAudit : IAuditLog
    {
        private readonly List<(string Type, string? SessionId, string? Detail)> _rows = [];

        public IReadOnlyList<(string Type, string? SessionId, string? Detail)> Rows
        {
            get
            {
                lock (_rows)
                {
                    return [.. _rows];
                }
            }
        }

        public Task RecordAsync(string type, string? sessionId = null, long? count = null, string? detail = null, CancellationToken ct = default)
        {
            lock (_rows)
            {
                _rows.Add((type, sessionId, detail));
            }

            return Task.CompletedTask;
        }
    }
}
