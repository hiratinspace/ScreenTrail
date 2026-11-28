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
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(30);

    public static bool OnWindows => OperatingSystem.IsWindows();

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs Windows")]
    public async Task TheUiAsksAboutTheServerAndNotAboutItself()
    {
        Assert.SkipUnless(PipePeer.Available, "Windows PowerShell not found");
        var name = PipeName();
        using var peer = PipePeer.Serving(name);
        await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(Soon, TestContext.Current.CancellationToken);
        Restrict(peer, nameof(TheUiAsksAboutTheServerAndNotAboutItself));

        var anchoredAtPeer = await new WindowsServerVerifier(PipePeer.Executable).VerifyAsync(pipe, TestContext.Current.CancellationToken);
        var anchoredHere = await new WindowsServerVerifier(Environment.ProcessPath!).VerifyAsync(pipe, TestContext.Current.CancellationToken);

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
        using (var waiting = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
        {
            waiting.CancelAfter(Soon);
            await pipe.WaitForConnectionAsync(waiting.Token);
        }

        Restrict(peer, nameof(TheServiceAsksAboutTheClientAndNotAboutItself));

        var anchoredAtPeer = await new WindowsClientVerifier(PipePeer.Executable).VerifyAsync(pipe, TestContext.Current.CancellationToken);
        var anchoredHere = await new WindowsClientVerifier(Environment.ProcessPath!).VerifyAsync(pipe, TestContext.Current.CancellationToken);

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
        using (var waiting = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
        {
            waiting.CancelAfter(Soon);
            await pipe.WaitForConnectionAsync(waiting.Token);
        }

        Restrict(peer, nameof(TheServiceRefusesAClientStartedAfterTheConnection));
        IClientVerifier verifier = new WindowsClientVerifier(PipePeer.Executable);

        var earlier = await verifier.CheckAsync(pipe, DateTime.UtcNow.AddHours(-1), TestContext.Current.CancellationToken);
        var now = await verifier.CheckAsync(pipe, DateTime.UtcNow, TestContext.Current.CancellationToken);
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
        await pipe.ConnectAsync(Soon, TestContext.Current.CancellationToken);
        Restrict(peer, nameof(TheUiRefusesAServerStartedAfterTheConnection));
        IServerVerifier verifier = new WindowsServerVerifier(PipePeer.Executable);

        var earlier = await verifier.VerifyAsync(pipe, DateTime.UtcNow.AddHours(-1), TestContext.Current.CancellationToken);
        var now = await verifier.VerifyAsync(pipe, DateTime.UtcNow, TestContext.Current.CancellationToken);

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
        var ct = TestContext.Current.CancellationToken;
        var name = PipeName();
        await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var accepting = pipe.WaitForConnectionAsync(ct);
        await using var client = new NamedPipeClientStream("127.0.0.1", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await client.ConnectAsync(TimeSpan.FromSeconds(10), ct);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or TimeoutException)
        {
            Assert.Skip($"the SMB server does not let this user reach its own pipe on 127.0.0.1 here ({ex.GetType().Name})");
        }

        await accepting.WaitAsync(Soon, ct);

        // A byte across first: Windows lets the server take on the client's identity only once it has read
        // from the pipe, and that identity is half of what is recorded below.
        await client.WriteAsync(new byte[] { 1 }, ct);
        await client.FlushAsync(ct);
        await pipe.ReadExactlyAsync(new byte[1], ct);
        Measurements.Record($"{nameof(TheServiceRefusesAClientThroughLoopbackSmb)}: {SeenThroughSmb(pipe)}");

        IClientVerifier verifier = new WindowsClientVerifier(Environment.ProcessPath!);
        var check = await verifier.CheckAsync(pipe, DateTime.UtcNow, ct);
        check.Peer?.Dispose();

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
        await client.ConnectAsync(Soon, TestContext.Current.CancellationToken);
        await accepting;

        Assert.Equal("not_a_server_pipe", await new WindowsClientVerifier(Environment.ProcessPath!).VerifyAsync(client, TestContext.Current.CancellationToken));
        Assert.Equal("not_a_client_pipe", await new WindowsServerVerifier(Environment.ProcessPath!).VerifyAsync(server, TestContext.Current.CancellationToken));
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
    /// </summary>
    private static string SeenThroughSmb(NamedPipeServerStream pipe)
    {
        var processId = GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) ? pid.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unknown";
        var buffer = new char[512];
        var named = GetNamedPipeClientComputerName(pipe.SafePipeHandle, buffer, (uint)(buffer.Length * sizeof(char)));
        var computer = named ? "a computer name" : $"no name, error {Marshal.GetLastPInvokeError()}";

        string groups;
        try
        {
            bool network = false, interactive = false;
            pipe.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent();
                network = identity.Groups?.Contains(new SecurityIdentifier(WellKnownSidType.NetworkSid, null)) ?? false;
                interactive = identity.Groups?.Contains(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null)) ?? false;
            });
            groups = $"NETWORK {(network ? "present" : "absent")}, INTERACTIVE {(interactive ? "present" : "absent")}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            groups = $"identity not readable ({ex.GetType().Name})";
        }

        return $"client process id {processId} (this process {Environment.ProcessId}); {computer}; client identity: {groups}";
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", EntryPoint = "GetNamedPipeClientComputerNameW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientComputerName(SafePipeHandle pipe, [Out] char[] clientComputerName, uint clientComputerNameLength);
}
