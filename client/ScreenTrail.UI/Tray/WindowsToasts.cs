using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;
using ScreenTrail.Core.Notifications;
using Windows.UI.Notifications;
using Notification = ScreenTrail.Core.Notifications.Notification;

namespace ScreenTrail.UI.Tray;

/// <summary>
/// Shows a notice as a real Windows toast (review findings C-T6 and D-10), through Windows' own API: no
/// toolkit package and no extra binary to ship. <see cref="ToastContent"/> writes it.
///
/// A toast is ScreenTrail's when the process says its identity (<see cref="Identify"/>) and a Start menu
/// shortcut carries the same one, which the installer's does. A development build has no such shortcut,
/// Windows refuses its toast, and <see cref="TryShow"/> says so, so the tray shows its balloon instead, as
/// it does when the technician has turned ScreenTrail's notifications off.
///
/// A press is handled while the UI runs, which between sessions it does (the tray icon is the indicator,
/// INV-4). Starting the UI from a toast pressed after it quit needs a COM activator, which is not built.
/// </summary>
internal static class WindowsToasts
{
    /// <summary>Says the app's identity, before any window: Windows groups the taskbar and the toasts by it.</summary>
    public static void Identify() => _ = SetCurrentProcessExplicitAppUserModelID(ToastContent.AppId);

    /// <summary>Shows the toast; false when Windows would not, and nothing was shown.</summary>
    /// <param name="pressed">Run on <paramref name="dispatcher"/> with the action a press names.</param>
    public static bool TryShow(Notification notice, Dispatcher dispatcher, Action<NotificationAction> pressed)
    {
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(pressed);
        if (!Installed.Value)
        {
            return false;
        }

        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier(ToastContent.AppId);
            if (notifier.Setting != NotificationSetting.Enabled)
            {
                return false;
            }

            var content = new Windows.Data.Xml.Dom.XmlDocument();
            content.LoadXml(ToastContent.Xml(notice));
            var toast = new ToastNotification(content)
            {
                Tag = ToastContent.Tag(notice),
                Group = ToastContent.Group,
                ExpirationTime = DateTimeOffset.Now + ToastContent.Lifetime,
            };
            toast.Activated += (sender, args) =>
            {
                var action = ToastContent.ActionOf((args as ToastActivatedEventArgs)?.Arguments);
                if (action != NotificationAction.None)
                {
                    _ = dispatcher.BeginInvoke(() => pressed(action));
                }
            };
            notifier.Show(toast);
            return true;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        {
            // No identity Windows knows (a development build), or notifications unavailable: the balloon.
            return false;
        }
    }

    /// <summary>
    /// Takes away every "Draft ready" toast but the one for the draft ready now (F-09, DEC-02): its text
    /// names the note and the customer, and Windows keeps it in a store of its own. <see cref="DraftToasts"/>
    /// decides which; this only asks Windows. Nothing is said when Windows will not: a toast left is a toast
    /// that expires a day after it was shown.
    /// </summary>
    public static void RemoveStale(ScreenTrail.Shared.Ipc.CaptureStateSnapshot? known)
    {
        if (!Installed.Value)
        {
            return;
        }

        try
        {
            var history = ToastNotificationManager.History;
            var shown = history.GetHistory(ToastContent.AppId)
                .Where(toast => toast.Group == ToastContent.Group)
                .Select(toast => toast.Tag)
                .ToList();
            foreach (var tag in DraftToasts.ToRemove(shown, known))
            {
                history.Remove(tag, ToastContent.Group, ToastContent.AppId);
            }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Windows' notification platform is not answering; the next change of state asks again.
        }
    }

    /// <summary>Clears ScreenTrail's toasts from the notification centre: "Delete everything" (F-09, DEC-02).</summary>
    public static void ClearHistory()
    {
        try
        {
            ToastNotificationManager.History.Clear(ToastContent.AppId);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        {
            // No identity Windows knows (a development build, which never showed a toast), or no platform.
        }
    }

    /// <summary>
    /// Whether the installer's Start menu shortcut, which carries the identity, is on this machine (the
    /// value Package.wxs writes beside it). Without it Windows does not know the identity, and a development
    /// build or a test run never tries a toast.
    /// </summary>
    private static readonly Lazy<bool> Installed = new(() =>
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"Software\ScreenTrail");
            return key?.GetValue("StartMenuShortcut") is 1;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    });

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
