namespace ScreenTrail.Core.Shell;

/// <summary>
/// How tall a window that sizes to its content may grow (design system §11 "Sizes", review finding D-07):
/// the work area of its own monitor less <see cref="WorkAreaMargin"/>. The shortcut sheet and the
/// diagnostics panel grew past a 1080p screen at 150%, and Close went under the taskbar.
/// </summary>
public static class WindowFit
{
    /// <summary>Kept clear of the work area, so the window's edge and shadow stay on screen.</summary>
    public const double WorkAreaMargin = 48;

    /// <summary>The tallest the window may be, in device-independent pixels.</summary>
    /// <param name="workAreaHeight">The monitor's work area, in device pixels.</param>
    /// <param name="scale">The monitor's scale: 1.5 at 150%.</param>
    public static double MaxHeight(int workAreaHeight, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        return Math.Max(0, (workAreaHeight / scale) - WorkAreaMargin);
    }

    /// <summary>
    /// Where a window of this size goes to sit in the middle of a work area, all in device pixels, kept
    /// inside it when it is the larger. For a dialog with no window to centre on: the tray's, on the
    /// monitor under the pointer rather than the primary one (review finding D-08).
    /// </summary>
    public static (int X, int Y) Centre(int areaLeft, int areaTop, int areaWidth, int areaHeight, int width, int height) =>
        (areaLeft + Math.Max(0, (areaWidth - width) / 2), areaTop + Math.Max(0, (areaHeight - height) / 2));
}
