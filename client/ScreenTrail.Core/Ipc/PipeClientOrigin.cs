namespace ScreenTrail.Core.Ipc;

/// <summary>
/// Whether a pipe client is on this machine, from what <c>GetNamedPipeClientComputerName</c> answered
/// (security review B-01). Kept here, out of the Windows adapter, so the rule is tested on every machine.
///
/// The service's pipe already refuses clients that come through the SMB server, loopback included; the
/// service's verifier asks again of each connection before it looks at the client's process, because the
/// process id of such a client is not one this machine recorded. Windows names the client's computer for
/// one of those, and fails with <c>ERROR_PIPE_LOCAL</c> for a client on this machine. That failure is the
/// only answer that lets the check go on: a name, or any other error, is refused.
/// </summary>
public static class PipeClientOrigin
{
    /// <summary>The verifier's refusal of a client not shown to be on this machine. A fixed label.</summary>
    public const string ClientNotLocal = "client_not_local";

    /// <summary><c>ERROR_PIPE_LOCAL</c>: the pipe's client is a local process.</summary>
    private const int ErrorPipeLocal = 229;

    /// <param name="nameReturned">Whether <c>GetNamedPipeClientComputerName</c> returned a name.</param>
    /// <param name="error">The last error it left; read only when it returned none.</param>
    /// <returns>null for a local client; otherwise <see cref="ClientNotLocal"/>.</returns>
    public static string? Refusal(bool nameReturned, int error) =>
        !nameReturned && error == ErrorPipeLocal ? null : ClientNotLocal;
}
