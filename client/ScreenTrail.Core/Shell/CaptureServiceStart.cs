using System.ComponentModel;
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
