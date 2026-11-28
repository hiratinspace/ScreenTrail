using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace ScreenTrail.Platform.Ipc;

/// <summary>
/// Which file a running process was started from, asked of the kernel (ST-012).
///
/// <b>Not <c>Process.MainModule.FileName</c>.</b> That reads the target process's own loader data,
/// which lives in that process's memory and which a process running as the same user can rewrite. The
/// peer check is the one thing standing between the capture service and a same-user impostor, and it was
/// asking the impostor to describe itself: overwrite the path in your own PEB, name the real signed
/// executable, and the verifier goes off and checks that genuine file on disk (2026-09-19 review).
///
/// <c>QueryFullProcessImageName</c> answers from the kernel's own record of what was mapped, which the
/// process cannot reach.
///
/// <b>The handle is the other half, and not all of it</b> (corrected 2026-11-28, security review B-01).
/// Holding an open handle pins the process object, so from the moment it is opened the id cannot be handed
/// to anything new and every later answer (the path, the start time, the windows) is about the same
/// process. It does not reach back to before it was opened: the id a pipe records is free to name a new
/// process once the one that connected has exited, so the process opened may not be the one that
/// connected. <see cref="StartedUtc"/> is what tells, and the verifiers refuse a process that started after
/// the connection (<c>PeerStart</c>).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessImage : IDisposable
{
    /// <summary>
    /// Enough to ask a process what it is, and nothing else.
    ///
    /// PROCESS_QUERY_LIMITED_INFORMATION rather than PROCESS_QUERY_INFORMATION: it is granted across
    /// integrity levels and asks for no right to read memory, write memory or change anything. A
    /// verifier that needed more access than it uses would be a verifier worth attacking.
    /// </summary>
    private const int QueryLimitedInformation = 0x1000;

    private readonly SafeProcessHandle _handle;

    private ProcessImage(SafeProcessHandle handle, uint processId, string path)
    {
        _handle = handle;
        ProcessId = processId;
        Path = path;
    }

    /// <summary>The full path of the file the process was started from.</summary>
    public string Path { get; }

    /// <summary>The process's id, which names this process and nothing else while the handle is held.</summary>
    public uint ProcessId { get; }

    /// <summary>The open handle, for a further question asked of the same process. Owned by this object.</summary>
    internal SafeProcessHandle Handle => _handle;

    /// <summary>When the process started, in UTC, asked of the handle; null when Windows will not say.</summary>
    public DateTime? StartedUtc =>
        GetProcessTimes(_handle, out var created, out _, out _, out _) && created > 0 ? DateTime.FromFileTimeUtc(created) : null;

    /// <summary>
    /// Opens the process and reads its image path, keeping it open until this is disposed.
    ///
    /// Null when the process is gone, or when this account may not ask, both of which are refusals
    /// rather than passes. "Cannot tell" is not "yes".
    /// </summary>
    public static ProcessImage? Of(uint processId)
    {
        var handle = OpenProcess(QueryLimitedInformation, inheritHandle: false, processId);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        // The documented maximum path, doubled: long paths are enabled per-machine, and a truncated
        // answer would be a different file that happens to start the same way.
        var buffer = new char[32_768];
        var length = buffer.Length;
        if (!QueryFullProcessImageName(handle, 0, buffer, ref length))
        {
            handle.Dispose();
            return null;
        }

        return new ProcessImage(handle, processId, new string(buffer, 0, length));
    }

    /// <summary>
    /// The file this process was started from, as the kernel records it: the same source, and so the
    /// same spelling, as a peer's path. Null when it cannot be read.
    /// </summary>
    public static string? OwnPath()
    {
        using var image = Of((uint)Environment.ProcessId);
        return image?.Path;
    }

    /// <summary>Lets go of the process. Nothing may be trusted about <see cref="Path"/> after this.</summary>
    public void Dispose() => _handle.Dispose();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        int desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        SafeProcessHandle process,
        int flags,
        [Out] char[] exeName,
        ref int size);
}
