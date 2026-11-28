using System.IO.Pipes;
using System.Runtime.Versioning;
using ScreenTrail.Core.Detection;
using ScreenTrail.Core.Ipc;
using ScreenTrail.Platform.Ipc;
using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Tests.Windows.Ipc;

/// <summary>
/// The pill a connection reports, checked against the windows of the process its handshake verified
/// (security review B-03), over a real pipe.
///
/// A client that passes the handshake and reports a rectangle where its process has no window must not
/// count as the pill. The one that does have a visible window there must, or every real session would be
/// suppressed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PillWindowTests
{
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(30);

    public static bool OnWindows => OperatingSystem.IsWindows();

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs Windows")]
    public async Task AVerifiedClientReportingWhereItHasNoWindowIsNotThePill()
    {
        Assert.SkipUnless(PipePeer.Available, "Windows PowerShell not found");
        var ct = TestContext.Current.CancellationToken;
        var name = IpcPipeNames.ForUser($"test-pill-{Guid.NewGuid():N}");
        using var peer = PipePeer.Connecting(name);
        await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using (var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            waiting.CancelAfter(Soon);
            await pipe.WaitForConnectionAsync(waiting.Token);
        }

        var connectedAt = DateTime.UtcNow;

        // Anchored at the peer's own executable, so the handshake's check passes it, as B-01 or B-02 would
        // have let a stranger pass.
        var check = await ((IClientVerifier)new WindowsClientVerifier(PipePeer.Executable)).CheckAsync(pipe, connectedAt, ct);
        Assert.Null(check.Refusal);
        using var verified = Assert.IsAssignableFrom<IVerifiedPeer>(check.Peer);

        var reports = new IndicatorReports();
        var indicators = new IndicatorCommands(reports, windowRequired: true);
        var connection = Guid.NewGuid();
        indicators.Opened(connection, verified);

        var answer = await indicators.ReportAsync(connection, new IndicatorShowingCommand { RequestId = 1, X = 0, Y = 0, Width = 1, Height = 1 }, ct);

        peer.AssertStillRunning();
        Assert.False(answer.Ok);
        Assert.False(reports.PillShowing);
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs Windows")]
    public async Task AVerifiedClientWithAVisibleWindowWhereItReportsIsThePill()
    {
        Hardware.RequireDesktop();
        var ct = TestContext.Current.CancellationToken;
        var name = IpcPipeNames.ForUser($"test-pill-{Guid.NewGuid():N}");
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var accepting = server.WaitForConnectionAsync(ct);
        await client.ConnectAsync(Soon, ct);
        await accepting;
        var connectedAt = DateTime.UtcNow;

        using var window = DesktopWindow.CreateOrdinary("pill-window-test");
        window.ShowAboveAt(200, 200, 220, 36);
        var (left, top, right, bottom) = window.Bounds;

        // This process is the client, anchored at itself, so the check passes it and holds it.
        var check = await ((IClientVerifier)new WindowsClientVerifier(Environment.ProcessPath!)).CheckAsync(server, connectedAt, ct);
        Assert.Null(check.Refusal);
        using var verified = Assert.IsAssignableFrom<IVerifiedPeer>(check.Peer);
        Assert.Contains(verified.Windows(), w => w.Bounds == new ScreenRect(left, top, right, bottom) && w.Visible && !w.Cloaked && w.OnAMonitor);

        var reports = new IndicatorReports();
        var indicators = new IndicatorCommands(reports, windowRequired: true);
        var connection = Guid.NewGuid();
        indicators.Opened(connection, verified);

        var there = await indicators.ReportAsync(connection, new IndicatorShowingCommand { RequestId = 1, X = left, Y = top, Width = right - left, Height = bottom - top }, ct);
        Assert.True(there.Ok);
        Assert.True(reports.PillShowing);

        // The same connection claiming a place it has no window is not believed, and does not replace the
        // report that was.
        var elsewhere = await indicators.ReportAsync(connection, new IndicatorShowingCommand { RequestId = 2, X = left + 600, Y = top + 400, Width = right - left, Height = bottom - top }, ct);
        Assert.False(elsewhere.Ok);
    }
}
