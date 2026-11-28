namespace ScreenTrail.Core.Detection;

/// <summary>
/// The name and elevation of the process whose window is in front, asked once per process (ST-022).
///
/// Opening a process and reading its image name and token is the expensive part of a look at the window
/// in front, and the watcher asks about the same window many times a second, so the answer is kept. It
/// used to be kept by process id alone, and it outlived the process: when that process exited and Windows
/// gave the same id to the next program to come to the front, the new one was judged by the old one's name
/// and elevation, so an exclusion by name and the elevated refusal could miss it (I-02, INV-5, INV-6).
///
/// Now the answer is kept only while a handle to the process is held open, and Windows does not give the
/// id of a process to another while a handle to it is open. The handle is let go when another process is
/// asked about and when nothing is in front (<see cref="Forget"/>), so at most one is held. A process that
/// could not be opened has no handle to hold, and its answer is not kept: it is asked about every time.
/// Kept apart from the Windows calls so the rule is testable without them.
/// </summary>
/// <param name="open">Opens the process for reading, or gives 0 when it cannot be opened.</param>
/// <param name="read">The name and elevation, read through the handle, or without one when it is 0.</param>
/// <param name="close">Closes a handle <paramref name="open"/> gave.</param>
public sealed class ForegroundProcessCache(
    Func<uint, nint> open,
    Func<uint, nint, (string? Name, bool Elevated)> read,
    Action<nint> close)
{
    private uint _processId;
    private nint _handle;
    private (string? Name, bool Elevated) _known;

    /// <summary>The process's name and elevation: the kept answer while its handle is held, otherwise asked now.</summary>
    public (string? Name, bool Elevated) Lookup(uint processId)
    {
        if (_handle != 0 && _processId == processId)
        {
            return _known;
        }

        Forget();
        var handle = open(processId);
        var known = read(processId, handle);
        if (handle != 0)
        {
            _processId = processId;
            _handle = handle;
            _known = known;
        }

        return known;
    }

    /// <summary>Lets go of the kept answer and its handle. Nothing in front, or the watcher stopping.</summary>
    public void Forget()
    {
        if (_handle != 0)
        {
            close(_handle);
        }

        _processId = 0;
        _handle = 0;
        _known = default;
    }
}
