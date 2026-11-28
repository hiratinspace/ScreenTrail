namespace ScreenTrail.Core.Ipc;

/// <summary>
/// The capture pipe's name is held by another pipe, possibly this service's previous run still held open
/// by a UI (2026-11-15 review). An <see cref="IOException"/>, so the server's accept loop backs off and
/// tries again.
/// </summary>
public sealed class PipeNameHeldException : IOException
{
    /// <summary>What it says when nothing more specific was given. No path and no account, just the fact.</summary>
    public const string Explanation = "The capture pipe's name is held by another pipe (possibly this service's previous run), so the UI cannot reach this service until it is released.";

    public PipeNameHeldException()
        : base(Explanation)
    {
    }

    public PipeNameHeldException(string message)
        : base(message)
    {
    }

    public PipeNameHeldException(string message, Exception? inner)
        : base(message, inner)
    {
    }
}
