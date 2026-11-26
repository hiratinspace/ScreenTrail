namespace ScreenTrail.Core.Hud;

/// <summary>
/// One screen's rectangle, in the virtual desktop's coordinates.
///
/// Its own type rather than <c>System.Windows.Rect</c> so that the decision below can be made (and
/// tested) on a machine with no windowing system at all. The UI translates; nothing here knows what a
/// monitor is beyond four numbers.
/// </summary>
public readonly record struct ScreenArea(double Left, double Top, double Right, double Bottom);

/// <summary>
/// Whether the recording pill would actually be visible where it is about to be drawn (ST-072, INV-4).
///
/// The pill is what a customer can point at to know their screen is being recorded, and the capture
/// service treats an attached UI as proof that something on screen says so. The window restored whatever
/// position it last saved without asking whether that position still exists, so a technician who docked
/// the pill on a second monitor and later unplugged it got a pill drawn into empty space: no indicator
/// anywhere, and a service still counting itself indicated (2026-09-20 review).
///
/// Nothing malicious is needed to arrive there. Unplugging a monitor is Tuesday. That the same file is
/// writable by any process running as the technician is the smaller half of it.
/// </summary>
public static class HudPlacement
{
    /// <summary>
    /// Whether the middle of the pill lands on some screen.
    ///
    /// The middle rather than a corner, and rather than an area fraction. A corner check passes a pill
    /// hanging off the bottom edge by all but one pixel; an area fraction re-docks a pill deliberately
    /// straddling two monitors, which is a thing people do on purpose and would be a bug of its own. The
    /// centre is the part a person looks at, and asking for it to be somewhere real is the whole claim.
    /// </summary>
    public static bool IsOnScreen(double x, double y, double width, double height, IReadOnlyList<ScreenArea> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);

        var centreX = x + (width / 2);
        var centreY = y + (height / 2);

        for (var i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            if (centreX >= screen.Left && centreX < screen.Right
                && centreY >= screen.Top && centreY < screen.Bottom)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Where the pill's left edge goes when its width changes (review finding D-16). Its words change from
    /// a timer to "Paused: press Ctrl+Alt+P to resume", and buttons come and go, so it grows and shrinks. It
    /// grew rightward only, and a pill docked at the top right ran off the screen. One in the right half of
    /// its monitor now keeps its right edge and grows leftward; one in the left half keeps its left edge.
    /// A pill that was inside the monitor's work area stays inside it. One placed across two monitors, which
    /// people do on purpose (<see cref="IsOnScreen"/>), is not pulled onto one. All in one unit, the window's own.
    /// </summary>
    public static double LeftAfterResize(double left, double oldWidth, double newWidth, double areaLeft, double areaRight)
    {
        var rightAnchored = left + (oldWidth / 2) >= (areaLeft + areaRight) / 2;
        var next = rightAnchored ? left + oldWidth - newWidth : left;
        var inside = left >= areaLeft && left + oldWidth <= areaRight;
        return inside ? Math.Clamp(next, areaLeft, Math.Max(areaLeft, areaRight - newWidth)) : next;
    }
}
