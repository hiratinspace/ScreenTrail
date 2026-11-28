using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ScreenTrail.Core.Detection;
using ScreenTrail.Core.Ipc;

namespace ScreenTrail.Platform.Ipc;

/// <summary>
/// The process a pipe client was verified as, held open for the life of its connection (security review
/// B-01 (c)), and asked what it has on screen (B-03).
///
/// The id is the one the handle was opened with, and a held handle keeps the process object, and so its id,
/// from naming anything else. Window properties only: nothing of a window's content is read.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsVerifiedPeer(ProcessImage image) : IVerifiedPeer
{
    private const int DWMWA_CLOAKED = 14;
    private const uint MONITOR_DEFAULTTONULL = 0;

    public IReadOnlyList<PeerWindow> Windows()
    {
        var owner = image.ProcessId;
        var found = new List<IntPtr>();
        _ = EnumWindows(
            (window, _) =>
            {
                if (GetWindowThreadProcessId(window, out var process) != 0 && process == owner)
                {
                    found.Add(window);
                }

                return true;
            },
            IntPtr.Zero);

        var windows = new List<PeerWindow>(found.Count);
        foreach (var window in found)
        {
            if (!GetWindowRect(window, out var rect))
            {
                continue;
            }

            var bounds = new ScreenRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
            windows.Add(new PeerWindow(
                bounds,
                Visible: IsWindowVisible(window) && !IsIconic(window),
                Cloaked: DwmGetWindowAttribute(window, DWMWA_CLOAKED, out int cloaked, sizeof(int)) != 0 || cloaked != 0,
                OnAMonitor: MonitorFromRect(ref rect, MONITOR_DEFAULTTONULL) != IntPtr.Zero));
        }

        return windows;
    }

    public void Dispose() => image.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
