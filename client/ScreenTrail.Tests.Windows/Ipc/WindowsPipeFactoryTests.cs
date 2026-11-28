using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using ScreenTrail.Core.Audit;
using ScreenTrail.Core.Capabilities;
using ScreenTrail.Core.Ipc;
using ScreenTrail.Platform.Ipc;
using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Tests.Windows.Ipc;

/// <summary>
/// ADR-0003 rule 1 against the pipe the service really creates.
///
/// Every other IPC test builds its pipe with <see cref="IpcServer.DefaultPipeFactory"/>, which is the
/// platform's default permissions and nothing else. So the one factory that ships, the one that writes
/// the DACL, had never been run: a rule widened to Everyone or Authenticated Users, or a factory that
/// threw on first use, would have passed every test there is (2026-10-12 review, T-79).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPipeFactoryTests
{
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(10);

    /// <summary>For one step on a pipe that is already connected.</summary>
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(5);

    /// <summary>For the probe's connect through the SMB server to give up by itself.</summary>
    private static readonly TimeSpan Probe = TimeSpan.FromSeconds(5);

    public static bool OnWindows => OperatingSystem.IsWindows();

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs Windows")]
    public async Task TheCapturePipeAdmitsOnlyItsOwnUser()
    {
        // The explicit rule set, read back off the pipe. Not AreAccessRulesProtected: a kernel object's
        // DACL is what it was created with, and the factory never sets that flag, so asserting it would
        // test nothing the factory does.
        var me = WindowsIdentity.GetCurrent().User;
        Assert.NotNull(me);
        var create = WindowsPipeFactory.ForCurrentUser(PipeName("acl"));

        // Two instances, because the service listens on several at once and every one of them comes from
        // the same factory call.
        await using var first = create();
        await using var second = create();

        foreach (var pipe in new[] { first, second })
        {
            var rules = pipe.GetAccessControl()
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                .Cast<PipeAccessRule>()
                .ToList();

            // Named one by one so a failure says which door was opened. Deny rules open no door: the one
            // for NETWORK (B-01) is pinned by TheCapturePipeDeniesTheNetwork.
            var allows = rules.Where(rule => rule.AccessControlType == AccessControlType.Allow).ToList();
            foreach (var outsider in Outsiders)
            {
                Assert.DoesNotContain(allows, rule => rule.IdentityReference.Equals(new SecurityIdentifier(outsider, null)));
            }

            var only = Assert.Single(allows);
            Assert.Equal(me, only.IdentityReference);
            Assert.Equal(AccessControlType.Allow, only.AccessControlType);
            Assert.False(only.IsInherited);
        }
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs Windows")]
    public async Task TheCapturePipeDeniesTheNetwork()
    {
        // Security review B-01 (a): over loopback SMB the "client process id" is a number the client
        // writes itself, so a pipe that admits NETWORK lets anything name the real UI. The deny is read
        // back off every instance, and the descriptor the factory compares later instances with holds it.
        var network = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
        var create = WindowsPipeFactory.ForCurrentUser(PipeName("network"));
        await using var first = create();
        await using var second = create();

        foreach (var pipe in new[] { first, second })
        {
            var rules = pipe.GetAccessControl()
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                .Cast<PipeAccessRule>()
                .ToList();

            var deny = Assert.Single(rules, rule => rule.AccessControlType == AccessControlType.Deny);
            Assert.Equal(network, deny.IdentityReference);
            Assert.Equal(PipeAccessRights.FullControl, deny.PipeAccessRights & PipeAccessRights.FullControl);
            Assert.True(WindowsPipeFactory.DeniesNetwork(pipe.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access)));
        }
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs Windows")]
    public async Task TheCapturePipeRefusesAClientThroughLoopbackSmb()
    {
        // B-01 (a), end to end: the pipe opened as \\127.0.0.1\pipe\<name> comes in through the SMB
        // server and is refused. Needs the Server service (LanmanServer) running.
        var name = PipeName("smb");
        var create = WindowsPipeFactory.ForCurrentUser(name);
        await using var pipe = create();
        var accepting = pipe.WaitForConnectionAsync(TestContext.Current.CancellationToken);

        await using var client = new NamedPipeClientStream("127.0.0.1", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        Exception? refused = null;
        try
        {
            await PipeSteps.Within(Soon + Step, "the loopback client's connect", t => client.ConnectAsync(Soon, t));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or TimeoutException)
        {
            refused = ex;
        }

        // A client that got in: the server's end is given a moment to see the connection too, so that the
        // assertion below fails with its own message.
        if (refused is null)
        {
            _ = await PipeSteps.TryWithin(Step, _ => accepting);
        }

        var unanswered = refused is IOException or TimeoutException && !accepting.IsCompleted && !await LoopbackSmbServesAsync();
        Assert.SkipWhen(unanswered, "the SMB server does not answer on 127.0.0.1 here");
        Assert.False(accepting.IsCompleted, "a client through loopback SMB was connected to the capture pipe");

        // The connect itself failed. Its shape depends on what refused it: the DACL's deny for NETWORK is
        // access denied, and a pipe that rejects remote clients may also be reported to the SMB client as
        // an I/O error or a pipe it cannot reach until the timeout. Any of the three, with no connection on
        // the server's side, is a refusal; a client that connected would have no exception at all.
        Assert.True(refused is not null, "a client through loopback SMB was connected to the capture pipe");
        Measurements.Record($"{nameof(TheCapturePipeRefusesAClientThroughLoopbackSmb)}: the loopback client was refused with {refused.GetType().Name} (0x{refused.HResult:X8})");
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs Windows")]
    public async Task TheCapturePipeCarriesAFullHandshake()
    {
        // The DACL has to admit the real client as well as keep others out. This is the whole handshake,
        // with the production pieces on both ends: the ACL'd pipe, the client verifier the service runs,
        // and the server verifier the UI runs before it writes the token. Both ends are this test process,
        // which is the dev-build rule's own case: an unsigned executable talking to itself, in its own
        // directory.
        var name = PipeName("handshake");
        var token = IpcToken.Generate();
        var self = Environment.ProcessPath!;
        var handler = new IdleHandler();
        await using var server = new IpcServer(
            WindowsPipeFactory.ForCurrentUser(name),
            token,
            new WindowsClientVerifier(self),
            handler,
            new NoAudit(),
            "0.0.1-test");
        server.Start();

        await using var client = await PipeSteps.Within(
            Soon + Step,
            "the UI's connect and handshake",
            t => IpcClient.ConnectAsync(name, token, "test-ui", new WindowsServerVerifier(self), "0.0.1", Soon, t));
        var answered = await PipeSteps.Within(Step, "the get_state command", t => client.SendAsync(id => new GetStateCommand { RequestId = id }, t));

        Assert.Equal(CaptureStates.Idle, client.State.State);
        Assert.True(answered.Ok);
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs Windows")]
    public async Task ANameAlreadyHeldRefusesTheServiceLoudlyUntilItIsFree()
    {
        // 2026-11-15 review: without the first-instance flag the service's instances joined a pipe
        // somebody else had made under the same name, and a UI could be answered by either. Here the
        // earlier pipe is this test's own, which is the hardest case: same account, default DACL, so
        // only the flag tells it apart.
        var name = PipeName("held");
        var told = new List<int>();
        var create = WindowsPipeFactory.ForCurrentUser(name, told.Add);
        await using var squatter = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        Assert.Throws<PipeNameHeldException>(() => create());
        Assert.Throws<PipeNameHeldException>(() => create());
        Assert.Throws<PipeNameHeldException>(() => create());

        // Each refusal counted in a row, which is what turns the service's warning into an error.
        Assert.Equal([1, 2, 3], told);

        // Once it is released the service takes the name, and every later instance joins its pipe.
        await squatter.DisposeAsync();
        await using var first = create();
        await using var second = create();
        Assert.Equal([1, 2, 3], told);
        await first.DisposeAsync();
        await second.DisposeAsync();

        // A later episode counts from one again.
        await using var again = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Assert.Throws<PipeNameHeldException>(() => create());
        Assert.Equal([1, 2, 3, 1], told);
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs Windows")]
    public async Task APipeOfTheSameOwnerThatLetsUsInIsStillNotOurs()
    {
        // The gap check compares owner and DACL, not the owner alone. This pipe has this account as its
        // owner and lets it add instances, so only its DACL (which also admits Everyone) tells it apart.
        var name = PipeName("same-owner");
        var told = new List<int>();
        var create = WindowsPipeFactory.ForCurrentUser(name, told.Add);
        create().Dispose();

        var me = WindowsIdentity.GetCurrent().User!;
        var open = new PipeSecurity();
        open.SetAccessRule(new PipeAccessRule(me, PipeAccessRights.FullControl, AccessControlType.Allow));
        open.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        await using var squatter = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, open);

        Assert.Throws<PipeNameHeldException>(() => create());
        Assert.Equal([1], told);

        await squatter.DisposeAsync();
        await using var reclaimed = create();
        await using var joined = create();
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs Windows")]
    public async Task TheServerWaitsOutAHeldNameAndThenServesTheUi()
    {
        // The refusal is an IOException, which the accept loop backs off from and tries again, so the
        // service is reachable the moment the name is free rather than never.
        var name = PipeName("wait");
        var token = IpcToken.Generate();
        var self = Environment.ProcessPath!;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var squatter = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var server = new IpcServer(
            WindowsPipeFactory.ForCurrentUser(name, _ => held.TrySetResult()),
            token,
            new WindowsClientVerifier(self),
            new IdleHandler(),
            new NoAudit(),
            "0.0.1-test");
        server.Start();

        await held.Task.WaitAsync(Soon, TestContext.Current.CancellationToken);
        await squatter.DisposeAsync();

        await using var client = await PipeSteps.Within(
            Soon + Step,
            "the UI's connect and handshake",
            t => IpcClient.ConnectAsync(name, token, "test-ui", new WindowsServerVerifier(self), "0.0.1", Soon, t));
        Assert.Equal(CaptureStates.Idle, client.State.State);
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs Windows")]
    public async Task ANameTakenWhileNoInstanceWasOpenIsRefusedToo()
    {
        // The accept loop can close its only instance before making the next. A pipe made in that gap
        // is not the service's, though the service has made the first instance before.
        var name = PipeName("gap");
        var told = new List<int>();
        var create = WindowsPipeFactory.ForCurrentUser(name, told.Add);
        create().Dispose();

        // One that will not let anyone add an instance, as another account's pipe would not.
        var me = WindowsIdentity.GetCurrent().User!;
        var closed = new PipeSecurity();
        closed.SetAccessRule(new PipeAccessRule(me, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        await using var squatter = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, closed);

        Assert.Throws<PipeNameHeldException>(() => create());
        Assert.Equal([1], told);

        await squatter.DisposeAsync();
        await using var reclaimed = create();
        await using var joined = create();
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs Windows")]
    public async Task TheServiceStillListensOnSeveralInstancesAtOnce()
    {
        // The flag is for the first instance only. On every one it would refuse the service's own
        // second listener, and the HUD's connection would wait behind the shell's.
        var name = PipeName("several");
        var create = WindowsPipeFactory.ForCurrentUser(name);
        var pipes = new List<NamedPipeServerStream>();
        try
        {
            for (var i = 0; i < 4; i++)
            {
                pipes.Add(create());
            }

            await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await PipeSteps.Within(Soon, "the client's connect", t => client.ConnectAsync(t));
        }
        finally
        {
            foreach (var pipe in pipes)
            {
                await pipe.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Everyone, Anonymous, Authenticated Users, BUILTIN\Users, Interactive and Network: each of these
    /// would let some other account on the machine, or a process that is not the technician, open the
    /// pipe and start the handshake.
    /// </summary>
    private static readonly WellKnownSidType[] Outsiders =
    [
        WellKnownSidType.WorldSid,
        WellKnownSidType.AnonymousSid,
        WellKnownSidType.AuthenticatedUserSid,
        WellKnownSidType.BuiltinUsersSid,
        WellKnownSidType.InteractiveSid,
        WellKnownSidType.NetworkSid,
    ];

    /// <summary>Whether the SMB server answers on loopback at all: an ordinary pipe of this user's, opened through it.</summary>
    private static async Task<bool> LoopbackSmbServesAsync()
    {
        var name = PipeName("smb-probe");
        await using var open = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var probe = new NamedPipeClientStream("127.0.0.1", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await PipeSteps.Within(Probe + Step, "the loopback SMB probe's connect", t => probe.ConnectAsync(Probe, t));
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return ex is UnauthorizedAccessException;
        }
    }

    /// <summary>Never the service's own name: the laptop that runs these may have the real service running.</summary>
    private static string PipeName(string purpose) => IpcPipeNames.ForUser($"test-{purpose}-{Guid.NewGuid():N}");

    private sealed class IdleHandler : IIpcCommandHandler
    {
        public CaptureStateSnapshot CurrentState => CaptureStateSnapshot.Idle;

        public CapabilitiesReported CurrentCapabilities =>
            new CapabilityReport(DateTimeOffset.UnixEpoch, Array.Empty<CapabilityCheck>()).ToWire();

        public Task<CommandResult> HandleAsync(IpcCommand command, Guid caller, CancellationToken ct = default) =>
            Task.FromResult(new CommandResult { RequestId = command.RequestId, Ok = true });

        public Task<IpcEvent?> ReplyToAsync(IpcCommand command, Guid caller, CancellationToken ct = default) =>
            Task.FromResult<IpcEvent?>(null);
    }

    private sealed class NoAudit : IAuditLog
    {
        public Task RecordAsync(string type, string? sessionId = null, long? count = null, string? detail = null, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
