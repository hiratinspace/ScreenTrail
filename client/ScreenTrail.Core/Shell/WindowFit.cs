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
}
