using ScreenTrail.Core.Detection;

namespace ScreenTrail.Core.Capture;

/// <summary>
/// The part of the screen a frame of the window in front is copied from (ST-025, INV-5).
///
/// Not the window's rectangle. On Windows 10 and 11 that reaches past what the window draws by an
/// invisible resize border, about 7 px at 100% on the left, right and bottom, and a maximized window's
/// reaches past its monitor on every side. The capturer copies the screen, so a copy of the rectangle
/// carried a strip of whatever lay beside or behind the window, or on the next monitor: an excluded
/// password manager snapped next to the remote tool, say, which nobody judged (A-06). What the window
/// draws is the compositor's frame (<c>DWMWA_EXTENDED_FRAME_BOUNDS</c>), the same rectangle the cover
/// check measures, and a maximized window is held to its monitor. Kept apart from the Windows plumbing so
/// the rule is testable without a desktop.
/// </summary>
public static class CaptureBounds
{
    /// <param name="window">The window's rectangle, as <c>GetWindowRect</c> gives it.</param>
    /// <param name="visibleFrame">
    /// What the window draws, as the compositor gives it, or null when it will not say. Then the rectangle
    /// is used, as before.
    /// </param>
    /// <param name="maximizedOn">The monitor a maximized window is on, or null for a window that is not maximized.</param>
    /// <returns>The rectangle to copy, never larger than the window's own; empty when nothing of it is left.</returns>
    public static ScreenRect Of(ScreenRect window, ScreenRect? visibleFrame, ScreenRect? maximizedOn)
    {
        var copied = visibleFrame is { } frame ? Intersect(frame, window) : window;
        return maximizedOn is { } monitor ? Intersect(copied, monitor) : copied;
    }

    private static ScreenRect Intersect(ScreenRect a, ScreenRect b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);

        // An empty result is kept empty rather than inverted, so a width or height computed from it is
        // never negative.
        return right <= left || bottom <= top ? new ScreenRect(left, top, left, top) : new ScreenRect(left, top, right, bottom);
    }
}
