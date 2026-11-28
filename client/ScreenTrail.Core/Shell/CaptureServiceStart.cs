using System.ComponentModel;
using System.Diagnostics;
using ScreenTrail.Core.Ipc;
using ScreenTrail.Core.Net;

namespace ScreenTrail.Core.Shell;

/// <summary>
/// Starting the capture service again, from the main window's banner ("Capture service not running",
/// Start) and the tray's first row while it is not running (review finding E-13).
///
/// The service is not a Windows service. It runs in the signed-in technician's own session, started at
/// sign-in from the machine's Run key (ADR-0003), so starting it by hand is running the same
/// executable from beside this app, as the technician: no rights they do not already have. A second
/// start while one is already running is the service's to refuse, as it is at sign-in.
/// </summary>
public static class CaptureServiceStart
{
    /// <summary>The tray's first row while the service is not running.</summary>
    public const string RowText = "S&tart the capture service";

    /// <summary>What the balloon says when nothing could be started.</summary>
    public const string Failed = "ScreenTrail could not start the capture service. Sign out and back in to start it, or reinstall ScreenTrail.";

    /// <summary>How long a start the technician asked for waits for the service before starting it (review finding C-I4).</summary>
    public static readonly TimeSpan AnswerWait = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Review finding C-I4: after a hand install nothing had started the service (the machine Run key starts
    /// it only at the next sign-in), so opening ScreenTrail from Start showed setup waiting on a service that
    /// was not coming. When the app is opened and no service has answered after <see cref="AnswerWait"/>, it
    /// is started once, by <see cref="Run"/>: the same executable beside this app, as the technician, that
    /// the tray's Start row already runs, so this adds no new way in. A service that is running answers first,
    /// and a second start is the service's to refuse.
    /// </summary>
    /// <param name="answered">Whether the service has answered.</param>
    /// <param name="wait">Waits for a time, or throws when cancelled (Task.Delay; a test passes its own).</param>
    /// <param name="start">Starts the service, as <see cref="Run"/> does.</param>
    /// <returns>Whether it was started.</returns>
    public static async Task<bool> StartIfSilentAsync(Func<bool> answered, Func<TimeSpan, CancellationToken, Task> wait, Action start, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(answered);
        ArgumentNullException.ThrowIfNull(wait);
        ArgumentNullException.ThrowIfNull(start);
        try
        {
            await wait(AnswerWait, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        if (ct.IsCancellationRequested || answered())
        {
            return false;
        }

        start();
        return true;
    }

    /// <summary>
    /// How the service at <paramref name="path"/> is started: as the technician, from its own folder. In the
    /// installer's build its environment is made clean first (<see cref="RunningHonestly.StartClean"/>), so
    /// the service this window starts runs with the runtime's diagnostics off and nothing the runtime would
    /// load before Main (B-02). A development build passes the environment on as it is, so a developer can
    /// still attach a profiler or a debugger.
    /// </summary>
    public static ProcessStartInfo StartInfo(string path, bool installerBuild)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var start = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path) };
        if (installerBuild)
        {
            RunningHonestly.StartClean(start.Environment);
        }

        return start;
    }

    /// <summary>Starts the service from <paramref name="appFolder"/>.</summary>
    /// <param name="appFolder">The folder this app runs from, where the installer puts the service beside it.</param>
    /// <param name="exists">Whether a file is there.</param>
    /// <param name="start">Runs the executable at a path.</param>
    /// <returns>Null when it was started; otherwise the words to say.</returns>
    public static string? Run(string appFolder, Func<string, bool> exists, Action<string> start)
    {
        ArgumentNullException.ThrowIfNull(appFolder);
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(start);
        var path = Path.Combine(appFolder, DeploymentAddresses.ServiceExecutableName);
        if (!exists(path))
        {
            return Failed;
        }

        try
        {
            start(path);
            return null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return Failed;
        }
    }
}
