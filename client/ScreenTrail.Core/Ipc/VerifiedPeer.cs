using ScreenTrail.Core.Detection;

namespace ScreenTrail.Core.Ipc;

/// <summary>
/// The process a connection's check passed, held for as long as the connection lasts (security review
/// B-01, B-03).
///
/// The check used to let go of the process the moment it had answered. Holding it keeps that process the
/// one later questions are asked of: what is on screen for the pill a connection reports is asked of the
/// process the connection was verified as, and its id cannot name anything else while it is held. The
/// server disposes it when the connection ends.
/// </summary>
public interface IVerifiedPeer : IDisposable
{
    /// <summary>
    /// The process's own top-level windows, as Windows describes them now. Empty when it has none, and when
    /// it cannot be asked, which is read the same way: nothing of it is on screen.
    /// </summary>
    IReadOnlyList<PeerWindow> Windows();
}

/// <summary>One top-level window of a verified peer. Window properties only, never its content.</summary>
/// <param name="Bounds">Where it is, in the same physical virtual-screen pixels the UI's report uses.</param>
/// <param name="Visible">Shown, and not minimised.</param>
/// <param name="Cloaked">Hidden by the compositor (another virtual desktop, an app the shell has put away), so not on screen.</param>
/// <param name="OnAMonitor">Some part of it is on a monitor that is connected now.</param>
public readonly record struct PeerWindow(ScreenRect Bounds, bool Visible, bool Cloaked, bool OnAMonitor);

/// <summary>What a client check decided: a refusal, or the peer it passed, held for the connection.</summary>
/// <param name="Refusal">A short reason with no content in it, or null when the client is acceptable.</param>
/// <param name="Peer">The process that passed, when the verifier can hold one; always null with a refusal.</param>
public sealed record ClientCheck(string? Refusal, IVerifiedPeer? Peer);
