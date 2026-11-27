using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ScreenTrail.Core.Shell;
using Forms = System.Windows.Forms;

namespace ScreenTrail.UI.Theme;

/// <summary>
/// Where a window with nothing to centre on opens: the middle of the monitor under the pointer, which is
/// where the tray menu that opened it was, rather than the primary monitor (review findings D-08, D-09).
/// Done once, at the first size, before the window is drawn.
/// </summary>
internal static class Placement
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    public static void CentreUnderThePointer(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.SizeChanged += CentreOnce;
    }

    private static void CentreOnce(object sender, SizeChangedEventArgs e)
    {
        var window = (Window)sender;
        window.SizeChanged -= CentreOnce;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
        {
            return;
        }

        // Device pixels throughout, so a monitor of another scale than the one the window was made on comes
        // out right: Windows tells WPF of the new scale once the window has moved.
        var area = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
        var (x, y) = WindowFit.Centre(area.Left, area.Top, area.Width, area.Height, rect.Right - rect.Left, rect.Bottom - rect.Top);
        _ = SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
