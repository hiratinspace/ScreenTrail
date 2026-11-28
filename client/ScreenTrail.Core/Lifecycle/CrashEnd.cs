namespace ScreenTrail.Core.Lifecycle;

/// <summary>
/// What the service and the window do with an exception nothing caught (security review A-08): write the
/// opt-in report, which holds the stack and never the message, and then end the process themselves, on a
/// short path that cannot wait on a lock the crashed code was holding.
///
/// Left to itself, the .NET runtime goes on from the handler to write the exception's message and stack to
/// the Application event log, which other users of the machine can read, and hands the crash to Windows
/// Error Reporting. Ending the process first is what keeps the message out of both. It does nothing for a
/// stack overflow or a fault in native code, which the runtime ends before any handler runs.
/// </summary>
public static class CrashEnd
{
    /// <summary>The exit code a crash ends with: a software error (EX_SOFTWARE), never 0.</summary>
    public const int ExitCode = 70;

    /// <summary>How long the report is given; a report that is not written by then is not waited for.</summary>
    public static readonly TimeSpan LongestReport = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Runs <paramref name="report"/> on a thread of its own for at most <paramref name="bound"/>, then calls
    /// <paramref name="end"/> with <see cref="ExitCode"/>. A report that throws or hangs still ends the process.
    /// </summary>
    public static void ReportThenEnd(Exception crash, Action<Exception> report, Action<int> end, TimeSpan bound)
    {
        ArgumentNullException.ThrowIfNull(crash);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(end);
        var thread = new Thread(() =>
        {
            try
            {
                report(crash);
            }
            catch (Exception)
            {
                // Nothing more can be said about a crash whose report failed; the process ends all the same.
            }
        })
        {
            IsBackground = true,
            Name = "ScreenTrail crash report",
        };
        thread.Start();
        _ = thread.Join(bound);
        end(ExitCode);
    }

    /// <summary>The exception a handler was given, or a stand-in when the runtime gave something else.</summary>
    public static Exception Of(object? thrown) =>
        thrown as Exception ?? new InvalidOperationException("Something other than an exception was thrown.");
}
