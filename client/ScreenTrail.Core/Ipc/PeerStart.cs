namespace ScreenTrail.Core.Ipc;

/// <summary>
/// Whether a process can be the one at the other end of a pipe connection, by when it started (security
/// review B-01 (b)).
///
/// The pipe records the peer's process id when it connects. An id is free to name a new process as soon as
/// the one it named has exited, so the id read at the check may describe a process that had nothing to do
/// with the connection. A process started after the connection was made cannot have made it, and both
/// verifiers refuse one, with the reasons here.
/// </summary>
public static class PeerStart
{
    /// <summary>The service's refusal: the process the client id names started after the connection.</summary>
    public const string ClientStartedAfterConnect = "client_started_after_connect";

    /// <summary>The UI's refusal: the process the server id names started after the connection.</summary>
    public const string ServerStartedAfterConnect = "server_started_after_connect";

    /// <param name="started">When the process started, as Windows records it; null when it would not say.</param>
    /// <param name="connectedAt">When this end saw the connection made.</param>
    /// <returns>False for a process started after the connection, and for one whose start is unknown.</returns>
    public static bool CanBeThePeer(DateTime? started, DateTime connectedAt) =>
        started is { } at && at.ToUniversalTime() <= connectedAt.ToUniversalTime();
}
