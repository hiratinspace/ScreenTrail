using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using ScreenTrail.Core.Ipc;
using ScreenTrail.Platform.Ipc;

namespace ScreenTrail.Tests.Windows.Ipc;

/// <summary>
/// The verifiers' VerifyAsync, over a real pipe to a real other process (2026-10-12 review, T-80).
///
/// Only <c>Verify(path)</c> had a test, which is the rule. What decides which path the rule is given is
/// VerifyAsync: which end of the pipe it asks Windows about, and how it turns that process into a file.
/// Ask about the wrong end and every check here verifies this process instead of the peer.
///
/// Each verifier gets two anchors, and the pair is what makes a swapped question visible whichever way
/// the build is signed. Anchored at the peer's own executable, the peer passes (same publisher, or same
/// directory for an unsigned file), and this process does not. Anchored at this process, the peer fails,
/// and this process would pass.
///
/// Once connected, the peer's DACL is cut down to the right to ask what it is. A VerifyAsync that went
/// back to <c>Process.MainModule</c> would read the same path off an ordinary peer; against this one it is
/// refused, and the peer anchored at itself stops passing.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsVerifierPipeTests
{
    /// <summary>For a PowerShell peer to start and reach the pipe.</summary>
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(30);

    /// <summary>For one step on a pipe that is already connected, and for a verifier's check.</summary>
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(5);

    /// <summary>For a connect through the SMB server to give up by itself (it then skips the test).</summary>
    private static readonly TimeSpan SmbConnect = TimeSpan.FromSeconds(10);

    public static bool OnWindows => OperatingSystem.IsWindows();

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs Windows")]
    public async Task TheUiAsksAboutTheServerAndNotAboutItself()
    {
        Assert.SkipUnless(PipePeer.Available, "Windows PowerShell not found");
        var name = PipeName();
        using var peer = PipePeer.Serving(name);
        await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await PipeSteps.Within(Soon, "the connect to the PowerShell peer", t => pipe.ConnectAsync(t));
        Restrict(peer, nameof(TheUiAsksAboutTheServerAndNotAboutItself));

        var anchoredAtPeer = await PipeSteps.Within(Step, "the check anchored at the peer", t => new WindowsServerVerifier(PipePeer.Executable).VerifyAsync(pipe, t).AsTask());
        var anchoredHere = await PipeSteps.Within(Step, "the check anchored here", t => new WindowsServerVerifier(Environment.ProcessPath!).VerifyAsync(pipe, t).AsTask());

        peer.AssertStillRunning();
        Assert.Null(anchoredAtPeer);
        Assert.NotNull(anchoredHere);
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs Windows")]
    public async Task TheServiceAsksAboutTheClientAndNotAboutItself()
    {
        Assert.SkipUnless(PipePeer.Available, "Windows PowerShell not found");
        var name = PipeName();
        // The peer first, so it is disposed last: it leaves by itself once this end closes. It waits for
        // the pipe to exist before connecting.
        using var peer = PipePeer.Connecting(name);
        await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await PipeSteps.Within(Soon, "the PowerShell peer's connect", t => pipe.WaitForConnectionAsync(t));

        Restrict(peer, nameof(TheServiceAsksAboutTheClientAndNotAboutItself));

        var anchoredAtPeer = await PipeSteps.Within(Step, "the check anchored at the peer", t => new WindowsClientVerifier(PipePeer.Executable).VerifyAsync(pipe, t).AsTask());
        var anchoredHere = await PipeSteps.Within(Step, "the check anchored here", t => new WindowsClientVerifier(Environment.ProcessPath!).VerifyAsync(pipe, t).AsTask());

        peer.AssertStillRunning();
        Assert.Null(anchoredAtPeer);
        Assert.NotNull(anchoredHere);
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs Windows")]
    public async Task TheServiceRefusesAClientStartedAfterTheConnection()
    {
        // Security review B-01 (b): the id the pipe recorded can name a process started after the
        // connection, once the one that connected has exited. Here the real peer is asked about as if the
        // connection had been made an hour before it started; the start time alone refuses it.
        Assert.SkipUnless(PipePeer.Available, "Windows PowerShell not found");
        var name = PipeName();
        using var peer = PipePeer.Connecting(name);
        await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await PipeSteps.Within(Soon, "the PowerShell peer's connect", t => pipe.WaitForConnectionAsync(t));

        Restrict(peer, nameof(TheServiceRefusesAClientStartedAfterTheConnection));
        IClientVerifier verifier = new WindowsClientVerifier(PipePeer.Executable);

        var earlier = await PipeSteps.Within(Step, "the check an hour back", t => verifier.CheckAsync(pipe, DateTime.UtcNow.AddHours(-1), t).AsTask());
        var now = await PipeSteps.Within(Step, "the check now", t => verifier.CheckAsync(pipe, DateTime.UtcNow, t).AsTask());
        now.Peer?.Dispose();

        peer.AssertStillRunning();
        Assert.Equal(PeerStart.ClientStartedAfterConnect, earlier.Refusal);
        Assert.Null(earlier.Peer);
        Assert.Null(now.Refusal);
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs Windows")]
    public async Task TheUiRefusesAServerStartedAfterTheConnection()
    {
        Assert.SkipUnless(PipePeer.Available, "Windows PowerShell not found");
        var name = PipeName();
        using var peer = PipePeer.Serving(name);
        await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await PipeSteps.Within(Soon, "the connect to the PowerShell peer", t => pipe.ConnectAsync(t));
        Restrict(peer, nameof(TheUiRefusesAServerStartedAfterTheConnection));
        IServerVerifier verifier = new WindowsServerVerifier(PipePeer.Executable);

        var earlier = await PipeSteps.Within(Step, "the check an hour back", t => verifier.VerifyAsync(pipe, DateTime.UtcNow.AddHours(-1), t).AsTask());
        var now = await PipeSteps.Within(Step, "the check now", t => verifier.VerifyAsync(pipe, DateTime.UtcNow, t).AsTask());

        peer.AssertStillRunning();
        Assert.Equal(PeerStart.ServerStartedAfterConnect, earlier);
        Assert.Null(now);
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs Windows")]
    public async Task TheServiceRefusesAClientThroughLoopbackSmb()
    {
        // Security review B-01: the capture pipe refuses remote clients itself, and the service's verifier
        // also asks Windows where the client is. This pipe is a plain one that lets a client through the
        // SMB server in, so only the verifier's question stands in the way. The client is this process,
        // from its own directory, which the dev-build rule would otherwise pass.
        var name = PipeName();
        await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var accepting = pipe.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        await using var client = new NamedPipeClientStream("127.0.0.1", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await PipeSteps.Within(SmbConnect + Step, "the loopback client's connect", t => client.ConnectAsync(SmbConnect, t));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or TimeoutException)
        {
            Assert.Skip($"the SMB server does not let this user reach its own pipe on 127.0.0.1 here ({ex.GetType().Name})");
        }

        await PipeSteps.Within(Step, "the pipe's accept of the loopback client", _ => accepting);

        // The check first, and on its own: where the client is, which is what refuses it, is known from the
        // connection itself, so the answer does not depend on anything crossing the pipe.
        IClientVerifier verifier = new WindowsClientVerifier(Environment.ProcessPath!);
        var check = await PipeSteps.Within(Step, "the verifier's check", t => verifier.CheckAsync(pipe, DateTime.UtcNow, t).AsTask());
        check.Peer?.Dispose();

        var seen = await SeenThroughSmbAsync(pipe, client);
        Measurements.Record($"{nameof(TheServiceRefusesAClientThroughLoopbackSmb)}: {seen}");

        Assert.Equal(PipeClientOrigin.ClientNotLocal, check.Refusal);
        Assert.Null(check.Peer);
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs Windows")]
    public async Task EachVerifierRefusesThePipeEndItDoesNotHold()
    {
        // The service's verifier handed a client stream, or the UI's a server stream, is being asked the
        // wrong question, and says so rather than answering one it was not asked.
        var name = PipeName();
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var accepting = server.WaitForConnectionAsync(TestContext.Current.CancellationToken);
        await PipeSteps.Within(Step, "the client's connect", t => client.ConnectAsync(t));
        await PipeSteps.Within(Step, "the server's accept", _ => accepting);

        var asClient = await PipeSteps.Within(Step, "the service's check of a client stream", t => new WindowsClientVerifier(Environment.ProcessPath!).VerifyAsync(client, t).AsTask());
        var asServer = await PipeSteps.Within(Step, "the UI's check of a server stream", t => new WindowsServerVerifier(Environment.ProcessPath!).VerifyAsync(server, t).AsTask());
        Assert.Equal("not_a_server_pipe", asClient);
        Assert.Equal("not_a_client_pipe", asServer);
    }

    private static void Restrict(PipePeer peer, string test)
    {
        if (!peer.RestrictToQueryLimited())
        {
            Measurements.Record($"{test}: the restricted DACL did not hold on this machine, so a VerifyAsync that read MainModule would not be caught here");
        }
    }

    private static string PipeName() => IpcPipeNames.ForUser($"test-peer-{Guid.NewGuid():N}");

    /// <summary>
    /// What the server end is told about a client that came through the SMB server: the process id the
    /// pipe recorded (MC-a9), whether Windows names a client computer, and whether the identity the
    /// server is given carries the NETWORK and INTERACTIVE groups (which says whether a deny for NETWORK
    /// can reach such a client at all).
    ///
    /// The identity needs a byte across first: Windows lets the server take on the client's identity only
    /// once it has read from the pipe. The read is posted before the client writes: the pipe is made with
    /// buffer sizes of 0, and a write may then wait for a read at the far end. If the byte does not arrive
    /// within its limit, the identity is not read and the measurement says so; nothing else waits on it.
    /// </summary>
    private static async Task<string> SeenThroughSmbAsync(NamedPipeServerStream pipe, NamedPipeClientStream client)
    {
        var processId = GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) ? pid.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unknown";
        var buffer = new char[512];
        var named = GetNamedPipeClientComputerName(pipe.SafePipeHandle, buffer, (uint)(buffer.Length * sizeof(char)));
        var computer = named ? "a computer name" : $"no name, error {Marshal.GetLastPInvokeError()}";
        var where = $"client process id {processId} (this process {Environment.ProcessId}); {computer}";

        string identity;
        try
        {
            var reading = PipeSteps.TryWithin(Step, t => pipe.ReadAsync(new byte[1], t).AsTask());
            var written = await PipeSteps.TryWithin(Step, t => client.WriteAsync(new byte[] { 1 }, t).AsTask())
                && await PipeSteps.TryWithin(Step, t => client.FlushAsync(t));
            var (arrived, count) = await reading;
            identity = arrived && count == 1
                ? Identity(pipe)
                : $"the byte from the loopback client did not arrive within {Step.TotalSeconds:0} s, so the identity was not read (the client's write {(written ? "finished" : "did not finish")})";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            identity = $"the byte from the loopback client could not be sent or read ({ex.GetType().Name}), so the identity was not read";
        }

        return $"{where}; {identity}";
    }

    private static string Identity(NamedPipeServerStream pipe)
    {
        try
        {
            bool network = false, interactive = false;
            pipe.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent();
                network = identity.Groups?.Contains(new SecurityIdentifier(WellKnownSidType.NetworkSid, null)) ?? false;
                interactive = identity.Groups?.Contains(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null)) ?? false;
            });
            return $"client identity: NETWORK {(network ? "present" : "absent")}, INTERACTIVE {(interactive ? "present" : "absent")}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return $"client identity: not readable ({ex.GetType().Name})";
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", EntryPoint = "GetNamedPipeClientComputerNameW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientComputerName(SafePipeHandle pipe, [Out] char[] clientComputerName, uint clientComputerNameLength);
}
