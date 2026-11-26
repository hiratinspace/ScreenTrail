using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenTrail.Core.Shell;
using Forms = System.Windows.Forms;

namespace ScreenTrail.UI.Theme;

/// <summary>
/// Caps a window that sizes to its content at its monitor's work area less 48 (<see cref="WindowFit"/>,
/// review finding D-07), once it has a handle and again when it moves to a monitor of another scale.
/// The window's content scrolls in the room that is left; its footer stays outside the scroll.
/// </summary>
internal static class FitToWorkArea
{
    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SourceInitialized += (_, _) => Cap(window);
        window.DpiChanged += (_, _) => Cap(window);
    }

    private static void Cap(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var area = Forms.Screen.FromHandle(handle).WorkingArea;
        window.MaxHeight = WindowFit.MaxHeight(area.Height, VisualTreeHelper.GetDpi(window).DpiScaleY);
    }
}
