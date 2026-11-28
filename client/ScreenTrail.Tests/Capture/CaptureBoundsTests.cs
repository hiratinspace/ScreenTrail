using ScreenTrail.Core.Capture;
using ScreenTrail.Core.Detection;

namespace ScreenTrail.Tests.Capture;

/// <summary>
/// Which part of the screen a frame of the window in front is copied from (A-06, INV-5). The window's
/// rectangle on Windows 10 and 11 reaches past what is drawn by an invisible resize border, about 7 px at
/// 100% on the left, right and bottom, and a maximized window's reaches past its monitor on every side.
/// Copied whole, that border is a strip of whatever lies beside or behind the window, or on the next
/// monitor, and nobody judged that window.
/// </summary>
public sealed class CaptureBoundsTests
{
    // A window snapped to the right half of a 1920x1080 monitor at 100%, as Windows reports it.
    private static readonly ScreenRect SnappedRectangle = new(953, 0, 1927, 1047);
    private static readonly ScreenRect SnappedFrame = new(960, 0, 1920, 1040);

    // The same monitor's window maximized: the rectangle and the drawn frame both reach past the monitor.
    private static readonly ScreenRect Monitor = new(0, 0, 1920, 1080);
    private static readonly ScreenRect MaximizedRectangle = new(-8, -8, 1928, 1088);
    private static readonly ScreenRect MaximizedFrame = new(-1, -1, 1921, 1081);

    [Fact]
    public void ARestoredWindowIsCopiedFromWhatItDrawsAndNotItsInvisibleBorder()
    {
        var copied = CaptureBounds.Of(SnappedRectangle, SnappedFrame, maximizedOn: null);

        Assert.Equal(SnappedFrame, copied);
    }

    [Fact]
    public void AMaximizedWindowIsCopiedNoFurtherThanItsMonitor()
    {
        var copied = CaptureBounds.Of(MaximizedRectangle, MaximizedFrame, maximizedOn: Monitor);

        Assert.Equal(Monitor, copied);
    }

    [Fact]
    public void AMaximizedWindowWhoseFrameCannotBeReadStillStopsAtItsMonitor()
    {
        var copied = CaptureBounds.Of(MaximizedRectangle, visibleFrame: null, maximizedOn: Monitor);

        Assert.Equal(Monitor, copied);
    }

    [Fact]
    public void WhenTheCompositorWillNotSayTheRectangleIsCopiedAsBefore()
    {
        // DWM refusing is the only case the rectangle is still used for a restored window: the frame is
        // otherwise always known on Windows 8 and later, where composition cannot be turned off.
        var copied = CaptureBounds.Of(SnappedRectangle, visibleFrame: null, maximizedOn: null);

        Assert.Equal(SnappedRectangle, copied);
    }

    [Fact]
    public void AFrameReportedPastTheRectangleIsCutToIt()
    {
        var copied = CaptureBounds.Of(SnappedRectangle, new ScreenRect(940, -5, 1940, 1060), maximizedOn: null);

        Assert.Equal(SnappedRectangle, copied);
    }

    [Fact]
    public void ARestoredWindowAcrossTwoMonitorsIsCopiedWhole()
    {
        // Only a maximized window is held to one monitor. A restored one dragged across two is drawn on
        // both, and cutting it to either would lose what the technician sees.
        var rectangle = new ScreenRect(1593, 100, 2567, 700);
        var frame = new ScreenRect(1600, 100, 2560, 693);

        Assert.Equal(frame, CaptureBounds.Of(rectangle, frame, maximizedOn: null));
    }

    [Fact]
    public void AMaximizedWindowNotOnItsMonitorAtAllGivesNothingToCopy()
    {
        var copied = CaptureBounds.Of(MaximizedRectangle, MaximizedFrame, maximizedOn: new ScreenRect(2000, 0, 3920, 1080));

        Assert.True(copied.IsEmpty);
    }
}
