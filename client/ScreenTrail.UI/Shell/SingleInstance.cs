using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using ScreenTrail.Core.Lifecycle;

namespace ScreenTrail.UI.Shell;

/// <summary>
/// The one running copy of the UI for this user and session (review finding D-02). The first start holds
/// the mutex and waits for the show signal; a later start sets the signal when it was asked for a window,
/// lets the first take the foreground, and exits. What the names are and why: <see cref="UiStart"/>.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const int AsfwAny = -1;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private RegisteredWaitHandle? _wait;

    private SingleInstance(Mutex mutex, EventWaitHandle show)
    {
        _mutex = mutex;
        _show = show;
    }

    /// <summary>
    /// The running copy's hold, or null when another copy already runs. A second start that opens a window
    /// has asked that copy to show its own by the time this returns.
    /// </summary>
    public static SingleInstance? Take(bool opensWindow)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? "unknown";

        // The event first, by both: a second start that finds the mutex held then always has an event to
        // set, and an auto-reset event set before the first is waiting stays set until it is.
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, UiStart.ShowSignalName(sid));
        var mutex = new Mutex(initiallyOwned: true, UiStart.InstanceName(sid), out var first);
        if (first)
        {
            return new SingleInstance(mutex, show);
        }

        if (opensWindow)
        {
            // The first copy is in the background; without this Windows refuses its window the foreground
            // and flashes its taskbar button instead.
            _ = AllowSetForegroundWindow(AsfwAny);
            _ = show.Set();
        }

        mutex.Dispose();
        show.Dispose();
        return null;
    }

    /// <summary>Calls <paramref name="show"/>, on a pool thread, each time a later start asks for the window.</summary>
    public void OnShowAsked(Action show)
    {
        ArgumentNullException.ThrowIfNull(show);
        _wait = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) => show(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _ = _wait?.Unregister(null);
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released from another thread than the one that took it, at exit: Windows releases it with the process.
        }

        _mutex.Dispose();
        _show.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
