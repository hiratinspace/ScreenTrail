using System.Windows;

namespace ScreenTrail.UI.Shell;

/// <summary>
/// Brings a window back the way the technician left it (review finding D-05). Show and Activate leave a
/// minimised window minimised, so Open in the tray, Open last draft and the pill's double-click did nothing
/// visible. A window minimised from maximised comes back maximised, as the taskbar button brings it.
/// </summary>
internal sealed class WindowRestore
{
    private bool _maximised;

    public WindowRestore(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _maximised = window.WindowState == WindowState.Maximized;
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState != WindowState.Minimized)
            {
                _maximised = window.WindowState == WindowState.Maximized;
            }
        };
    }

    /// <summary>Shows, restores if minimised, and activates.</summary>
    public void Bring(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = _maximised ? WindowState.Maximized : WindowState.Normal;
        }

        _ = window.Activate();
    }
}
