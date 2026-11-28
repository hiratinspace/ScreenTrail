using System.IO.Pipes;
using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Core.Ipc;

/// <summary>
/// Decides whether the process on the other end of a freshly connected pipe may drive capture
/// (ADR-0003: signed by our publisher, or a sibling of the service executable in unsigned dev builds).
/// The Windows implementation resolves the client process from the pipe; tests use fakes.
/// </summary>
public interface IClientVerifier
{
    /// <returns>null when the client is acceptable; otherwise a short reason with no content in it.</returns>
    ValueTask<string?> VerifyAsync(PipeStream connection, CancellationToken ct = default);

    /// <summary>
    /// The same check, keeping hold of the process it passed for the life of the connection (security
    /// review B-01, B-03). The server asks this one. A verifier that cannot hold a process answers with
    /// no peer, which is what every test double does.
    /// </summary>
    /// <param name="connectedAt">When the server accepted the connection, in UTC.</param>
    async ValueTask<ClientCheck> CheckAsync(PipeStream connection, DateTime connectedAt, CancellationToken ct = default) =>
        new(await VerifyAsync(connection, ct).ConfigureAwait(false), null);
}

/// <summary>What the service does with commands once a client is authenticated. ST-020 supplies the real one.</summary>
public interface IIpcCommandHandler
{
    CaptureStateSnapshot CurrentState { get; }

    /// <summary>What Windows allows right now (ST-021). Re-checked on request: permissions change mid-day.</summary>
    CapabilitiesReported CurrentCapabilities { get; }

    /// <param name="caller">
    /// Which connection sent this. Destructive commands carry a confirmation token, and a token that is
    /// not tied to the conversation it came from is a bearer token any authenticated client can spend,
    /// which is the whole of the second round trip, gone (2026-09-20 review).
    /// </param>
    Task<CommandResult> HandleAsync(IpcCommand command, Guid caller, CancellationToken ct = default);

    /// <summary>
    /// The reply an asking command wants, or null when the command is an instruction rather than a
    /// question (ST-085).
    ///
    /// Separate from <see cref="HandleAsync"/> because the two answer differently: an instruction gets a
    /// result saying whether it was accepted, while a question gets an event of its own and a result
    /// saying the question was understood. Keeping them apart is what stops <see cref="IpcServer"/>
    /// growing a case per reply type.
    /// </summary>
    Task<IpcEvent?> ReplyToAsync(IpcCommand command, Guid caller, CancellationToken ct = default) => Task.FromResult<IpcEvent?>(null);

    /// <summary>
    /// A connection passed the handshake, before its first command is read. <paramref name="peer"/> is the
    /// process its check passed, or null when the verifier holds none; the server disposes it when the
    /// connection ends, so it must not be used after that.
    /// </summary>
    void Opened(Guid caller, IVerifiedPeer? peer)
    {
    }

    /// <summary>A connection that was opened has ended, for whatever reason. Its peer is disposed straight after.</summary>
    void Closed(Guid caller)
    {
    }
}

/// <summary>
/// The other half of ADR-0003 rule 2, and the half that was missing: the UI deciding whether the process
/// answering on the pipe is really the capture service, <em>before</em> it hands over the session token.
///
/// Without it the handshake is one-sided. Anything that can create the pipe name first (a per-user pipe,
/// so no privilege is needed) receives the token, and can then feed the UI whatever capture state it
/// likes. That breaks INV-4 in the worst direction: a technician is shown "not recording" while recording
/// continues, which is the one lie this product must never tell.
/// </summary>
public interface IServerVerifier
{
    /// <returns>null when the server is really the service; otherwise a short reason with no content in it.</returns>
    ValueTask<string?> VerifyAsync(PipeStream connection, CancellationToken ct = default);

    /// <summary>
    /// The same check, also refusing a server process that started after the connection was made (security
    /// review B-01 (b)). The client asks this one; a verifier that cannot tell answers as the plain check.
    /// </summary>
    /// <param name="connectedAt">When the connect returned, in UTC.</param>
    ValueTask<string?> VerifyAsync(PipeStream connection, DateTime connectedAt, CancellationToken ct = default) =>
        VerifyAsync(connection, ct);
}
