using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ScreenTrail.Core.Lifecycle;

namespace ScreenTrail.Platform.Lifecycle;

/// <summary>
/// Writes a start refusal to the Windows Application log, where somebody can find it (ST-112): an installed
/// build is a Windows program, and its standard error goes nowhere. Also the one notice of a pipe name that
/// stays held (security review B-06).
///
/// Through <c>RegisterEventSource</c> and <c>ReportEvent</c>, which need no administrator rights and no
/// registered source. Event Viewer shows the entry under the source "ScreenTrail" with the fixed sentence as
/// its text, and notes that the source has no message file. .NET's <c>EventLog.WriteEntry</c> is not used:
/// it tries to create the source, which needs administrator rights. The text is the refusal's fixed sentence
/// and nothing else: no user, no SID, no path (INV-10).
/// </summary>
[SupportedOSPlatform("windows")]
public static class RefusalEventLog
{
    /// <summary>The source name the entries carry.</summary>
    public const string Source = "ScreenTrail";

    /// <summary>The event id of <see cref="PipeNameHeld"/>, beside the service's start refusals (1100 and up).</summary>
    public const int PipeNameHeldEventId = 1110;

    /// <summary>
    /// What the service writes when its pipe's name stays held (security review B-06). Fixed: no account, no
    /// SID, no pipe name.
    /// </summary>
    public const string PipeNameHeld = "Another pipe holds the name of ScreenTrail's capture pipe, possibly one of another account on this PC, so ScreenTrail's window cannot reach the capture service and nothing is captured. The service keeps trying. Signing that account out, or restarting the PC, frees the name.";

    /// <summary>Writes the entry. Returns whether Windows took it; a refusal goes on to exit either way.</summary>
    public static bool Write(StartRefusal refusal, bool ui)
    {
        var text = ui ? StartGuard.ExplainForUi(refusal) : StartGuard.Explain(refusal);
        return text.Length != 0 && Write(text, StartGuard.EventId(refusal, ui));
    }

    /// <summary>Writes <see cref="PipeNameHeld"/>. Returns whether Windows took it.</summary>
    public static bool WritePipeNameHeld() => Write(PipeNameHeld, PipeNameHeldEventId);

    private static bool Write(string text, int eventId)
    {
        var source = RegisterEventSource(null, Source);
        if (source == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return ReportEvent(source, EVENTLOG_WARNING_TYPE, 0, (uint)eventId, IntPtr.Zero, 1, 0, [text], IntPtr.Zero);
        }
        finally
        {
            _ = DeregisterEventSource(source);
        }
    }

    private const ushort EVENTLOG_WARNING_TYPE = 0x0002;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterEventSource(string? server, string source);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeregisterEventSource(IntPtr source);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReportEvent(IntPtr source, ushort type, ushort category, uint eventId, IntPtr userSid, ushort strings, uint dataSize, string[] texts, IntPtr data);
}
