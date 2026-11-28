using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ScreenTrail.Core.Lifecycle;

namespace ScreenTrail.Platform.Lifecycle;

/// <summary>
/// The Windows half of <see cref="CrashEnd"/> (security review A-08): Windows Error Reporting told to take no
/// heap and show nothing, and a crash that ends the process with <c>TerminateProcess</c> once its report is
/// written, before the runtime writes its own event-log entry. The installer also lists both programs under
/// Windows Error Reporting's excluded applications.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ProcessEnd
{
    private const uint WerFaultReportingFlagNoHeap = 0x1;
    private const uint WerFaultReportingNoUi = 0x20;

    /// <summary>At start, before anything can crash: the reporting flags, and the handler for a crash nothing caught.</summary>
    /// <param name="report">Writes the opt-in report (stack only).</param>
    public static void Install(Action<Exception> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _ = WerSetFlags(WerFaultReportingFlagNoHeap | WerFaultReportingNoUi);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => End(CrashEnd.Of(args.ExceptionObject), report);
    }

    /// <summary>Writes the report, bounded, and ends this process. Does not return.</summary>
    public static void End(Exception crash, Action<Exception> report) =>
        CrashEnd.ReportThenEnd(crash, report, Now, CrashEnd.LongestReport);

    private static void Now(int exitCode) => _ = TerminateProcess(GetCurrentProcess(), (uint)exitCode);

    [DllImport("kernel32.dll")]
    private static extern int WerSetFlags(uint flags);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);
}
