using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ScreenTrail.Core.Hud;
using ScreenTrail.Core.Shell;
using Forms = System.Windows.Forms;

namespace ScreenTrail.UI.Shell;

/// <summary>
/// Opens the shell window where it was closed on the same monitors, or in the middle of the monitor under
/// the pointer (review finding D-04; what and why: <see cref="WindowPlacements"/>). Windows' own placement
/// calls, in device pixels, so a monitor of another scale comes back the same size.
/// </summary>
internal static class ShellPlacement
{
    private const int SwHide = 0;
    private const int SwShowMinimized = 2;
    private const int SwShowMaximized = 3;
    private const int WpfRestoreToMaximized = 0x0002;

    public static void Attach(Window window, ShellPreferencesStore preferences)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(preferences);
        window.SourceInitialized += (_, _) => Restore(window, preferences);
        window.Closing += (_, e) => Save(window, preferences, e);
    }

    private static void Restore(Window window, ShellPreferencesStore preferences)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var screens = Forms.Screen.AllScreens;
        var saved = WindowPlacements.ToRestore(
            preferences.Load().Placements,
            [.. screens.Select(s => Area(s.Bounds))],
            [.. screens.Select(s => Area(s.WorkingArea))]);
        if (handle == IntPtr.Zero || saved is null)
        {
            Theme.Placement.CentreUnderThePointer(window);
            return;
        }

        // Hidden: the window is not shown yet, and WPF shows it next, at this normal place, maximised there
        // when it was.
        var placement = new NativePlacement
        {
            Length = Marshal.SizeOf<NativePlacement>(),
            ShowCommand = SwHide,
            Normal = new NativeRect { Left = saved.Left, Top = saved.Top, Right = saved.Right, Bottom = saved.Bottom },
        };
        if (SetWindowPlacement(handle, ref placement) && saved.Maximised)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    private static void Save(Window window, ShellPreferencesStore preferences, CancelEventArgs e)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var placement = new NativePlacement { Length = Marshal.SizeOf<NativePlacement>() };
        if (e.Cancel || handle == IntPtr.Zero || !GetWindowPlacement(handle, ref placement))
        {
            return;
        }

        var maximised = placement.ShowCommand == SwShowMaximized
            || (placement.ShowCommand == SwShowMinimized && (placement.Flags & WpfRestoreToMaximized) != 0);
        var now = new WindowPlacement(
            placement.Normal.Left,
            placement.Normal.Top,
            placement.Normal.Right,
            placement.Normal.Bottom,
            maximised,
            WindowPlacements.LayoutOf([.. Forms.Screen.AllScreens.Select(s => Area(s.Bounds))]));
        var current = preferences.Load();
        _ = preferences.Save(current with { Placements = WindowPlacements.With(current.Placements, now) });
    }

    private static ScreenArea Area(System.Drawing.Rectangle r) => new(r.Left, r.Top, r.Right, r.Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePlacement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public NativePoint MinPosition;
        public NativePoint MaxPosition;
        public NativeRect Normal;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr window, ref NativePlacement placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(IntPtr window, ref NativePlacement placement);
}
