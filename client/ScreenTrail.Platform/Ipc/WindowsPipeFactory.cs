using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using ScreenTrail.Core.Ipc;

namespace ScreenTrail.Platform.Ipc;

/// <summary>
/// ADR-0003 rule 1: the pipe's DACL admits only the user the service runs as, and not over the network
/// (security review B-01: the pipe refuses every client that comes through the SMB server, loopback
/// included, and its DACL denies NETWORK), and the pipe's name is the service's own.
///
/// The service runs as the signed-in technician (started from the Run key in their session, never as
/// SYSTEM), and so does the UI, so both ends are always the same account.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsPipeFactory
{
    /// <summary>
    /// How many refusals in a row before a held name is an error rather than a warning. A service that
    /// crashed can leave its pipe behind for as long as a UI holds its end, which clears in a few seconds
    /// once the UI notices; a name still held after that is worth an error.
    /// </summary>
    public const int HeldIsAnErrorAfter = 5;

    private static readonly SecurityIdentifier Network = new(WellKnownSidType.NetworkSid, null);

    /// <summary>
    /// The service's pipe instances, one per call: the server listens on one and keeps one per client.
    ///
    /// <b>The first instance claims the name</b> (<see cref="PipeOptions.FirstPipeInstance"/>). Pipe names
    /// are machine-wide, and without the flag a pipe of the same name made earlier by another account
    /// would not stop the service: its instances would join that pipe, under that account's DACL, and a
    /// UI could be answered by either. With it, a name already held refuses the service's first instance.
    /// Every later instance joins the pipe the first one made, and is checked to have done so: a name lost
    /// in a gap with no instance open belongs to whoever took it, and is refused the same way.
    ///
    /// A refusal is <see cref="PipeNameHeldException"/>, an <see cref="IOException"/>, so the server goes
    /// on retrying and takes the name once it is free. <paramref name="nameHeld"/> is told each refusal,
    /// with how many there have been in a row, for the service's log; the count starts again once the
    /// name is the service's. The UI meanwhile reaches nothing it will trust and says the capture service
    /// is not running.
    /// </summary>
    public static Func<NamedPipeServerStream> ForCurrentUser(string pipeName, Action<int>? nameHeld = null)
    {
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Cannot determine the current Windows user.");
        var security = new PipeSecurity();
        security.SetAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));

        // And nobody over the network, this user included (security review B-01 (a)). Through the SMB
        // server the client's process id is not one this machine recorded, and the UI is always on this
        // machine. The deny refuses a network logon. A client through the SMB server on this machine is
        // authenticated with a copy of its own logon, which is not a network logon, so the deny does not
        // reach it: the pipe's mode refuses it (CapturePipeMode, PIPE_REJECT_REMOTE_CLIENTS), whoever it is.
        security.AddAccessRule(new PipeAccessRule(Network, PipeAccessRights.FullControl, AccessControlType.Deny));

        var gate = new object();
        string? descriptor = null;
        var refusals = 0;

        return () =>
        {
            lock (gate)
            {
                if (descriptor is null)
                {
                    NamedPipeServerStream first;
                    try
                    {
                        first = Create(pipeName, firstInstance: true, security);
                    }
                    catch (UnauthorizedAccessException refused)
                    {
                        // What CreateNamedPipe answers when FILE_FLAG_FIRST_PIPE_INSTANCE meets a name
                        // that exists, and when a pipe of that name will not let this account in.
                        throw Held(refused);
                    }

                    // The descriptor is read back and checked. The mode cannot be: GetNamedPipeInfo reports
                    // which end and the pipe's type, GetNamedPipeHandleState the read and wait modes, and
                    // neither says whether remote clients are refused. This instance was made with it by
                    // the call above, and the Windows tests connect through loopback to see it hold.
                    descriptor = DescriptorOf(first);
                    if (descriptor is null || !DeniesNetwork(descriptor))
                    {
                        first.Dispose();
                        throw Held(null);
                    }

                    refusals = 0;
                    return first;
                }

                // All instances of one pipe share its security descriptor, so an instance of the pipe the
                // first one made has the first one's owner and DACL, and an instance of anybody else's has
                // theirs, even one of this account's that lets it in.
                NamedPipeServerStream next;
                try
                {
                    next = Create(pipeName, firstInstance: false, security);
                }
                catch (UnauthorizedAccessException refused)
                {
                    // This account always may add an instance to its own pipe, so a pipe that refuses it
                    // is somebody else's.
                    descriptor = null;
                    throw Held(refused);
                }

                if (!string.Equals(DescriptorOf(next), descriptor, StringComparison.Ordinal))
                {
                    next.Dispose();
                    descriptor = null;
                    throw Held(null);
                }

                return next;
            }
        };

        PipeNameHeldException Held(Exception? inner)
        {
            nameHeld?.Invoke(++refusals);
            return new PipeNameHeldException(PipeNameHeldException.Explanation, inner);
        }
    }

    /// <summary>
    /// Whether a descriptor, as SDDL, denies the NETWORK SID full control (B-01 (a)). The first instance's
    /// descriptor is checked with it, and every later instance is compared with that one.
    /// </summary>
    public static bool DeniesNetwork(string sddl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sddl);
        var dacl = new RawSecurityDescriptor(sddl).DiscretionaryAcl;
        return dacl is not null && dacl.OfType<CommonAce>().Any(ace =>
            ace.AceQualifier == AceQualifier.AccessDenied
            && ace.SecurityIdentifier == Network
            && ((PipeAccessRights)ace.AccessMask & PipeAccessRights.FullControl) == PipeAccessRights.FullControl);
    }

    /// <summary>The per-user pipe name from the user's SID.</summary>
    public static string PipeNameForCurrentUser()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        return Core.Ipc.IpcPipeNames.ForUser(sid);
    }

    /// <summary>
    /// One instance, made as <c>NamedPipeServerStreamAcl.Create</c> made it (the same name, open mode,
    /// unlimited instances, default buffers and timeout, and descriptor), with the one thing it cannot ask
    /// for: the pipe refuses remote clients (<see cref="CapturePipeMode"/>, B-01). Fails as it failed:
    /// <see cref="UnauthorizedAccessException"/> for ERROR_ACCESS_DENIED, an <see cref="IOException"/>
    /// otherwise.
    /// </summary>
    private static NamedPipeServerStream Create(string pipeName, bool firstInstance, PipeSecurity security)
    {
        var descriptor = security.GetSecurityDescriptorBinaryForm();
        var pinned = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = pinned.AddrOfPinnedObject(),
                InheritHandle = 0,
            };
            var handle = CreateNamedPipe(
                Path.GetFullPath(@"\\.\pipe\" + pipeName),
                CapturePipeMode.OpenMode(firstInstance),
                CapturePipeMode.PipeMode,
                UnlimitedInstances,
                outBufferSize: 0,
                inBufferSize: 0,
                defaultTimeout: 0,
                ref attributes);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                var message = new Win32Exception(error).Message;
                throw error == ErrorAccessDenied
                    ? new UnauthorizedAccessException(message)
                    : new IOException(message, unchecked((int)0x80070000) | error);
            }

            try
            {
                return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }
        finally
        {
            pinned.Free();
        }
    }

    /// <summary>
    /// The pipe's owner and DACL, as text, or null when Windows will not say, which is treated as not ours.
    /// </summary>
    private static string? DescriptorOf(NamedPipeServerStream pipe)
    {
        try
        {
            return pipe.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>PIPE_UNLIMITED_INSTANCES, what <see cref="NamedPipeServerStream.MaxAllowedServerInstances"/> becomes.</summary>
    private const uint UnlimitedInstances = 255;

    private const int ErrorAccessDenied = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public int InheritHandle;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafePipeHandle CreateNamedPipe(
        string name,
        uint openMode,
        uint pipeMode,
        uint maxInstances,
        uint outBufferSize,
        uint inBufferSize,
        uint defaultTimeout,
        ref SecurityAttributes securityAttributes);
}
