namespace ScreenTrail.Core.Ipc;

/// <summary>
/// The modes the service's pipe is created with, as <c>CreateNamedPipe</c> takes them (security review
/// B-01). Kept here, out of the Windows adapter, so the values are tested on every machine.
///
/// The pipe refuses every client that reaches it through the SMB server, loopback included
/// (<c>PIPE_REJECT_REMOTE_CLIENTS</c>), which the framework's pipe API has no way to ask for. Everything
/// else is what the framework passed before: a duplex, overlapped byte stream that waits, whose first
/// instance claims the name.
/// </summary>
public static class CapturePipeMode
{
    /// <summary><c>PIPE_ACCESS_DUPLEX</c>, the value of <c>PipeDirection.InOut</c>.</summary>
    private const uint AccessDuplex = 0x00000003;

    /// <summary><c>FILE_FLAG_OVERLAPPED</c>, the value of <c>PipeOptions.Asynchronous</c>.</summary>
    private const uint Overlapped = 0x40000000;

    /// <summary><c>FILE_FLAG_FIRST_PIPE_INSTANCE</c>, the value of <c>PipeOptions.FirstPipeInstance</c>.</summary>
    private const uint FirstPipeInstance = 0x00080000;

    /// <summary><c>PIPE_REJECT_REMOTE_CLIENTS</c>.</summary>
    private const uint RejectRemoteClients = 0x00000008;

    /// <summary>
    /// <c>dwPipeMode</c>: <c>PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT</c> (all zero, as the framework
    /// asked for a byte pipe) and <c>PIPE_REJECT_REMOTE_CLIENTS</c>.
    /// </summary>
    public static uint PipeMode => RejectRemoteClients;

    /// <summary><c>dwOpenMode</c> for one instance: the first claims the name, every later one joins it.</summary>
    public static uint OpenMode(bool firstInstance) => AccessDuplex | Overlapped | (firstInstance ? FirstPipeInstance : 0);
}
