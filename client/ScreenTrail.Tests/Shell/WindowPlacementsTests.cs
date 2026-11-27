using ScreenTrail.Core.Hud;
using ScreenTrail.Core.Shell;

namespace ScreenTrail.Tests.Shell;

/// <summary>The shell window opens where it was closed on the same monitors (review finding D-04).</summary>
public sealed class WindowPlacementsTests
{
    private static readonly ScreenArea Laptop = new(0, 0, 1920, 1080);
    private static readonly ScreenArea LaptopWork = new(0, 0, 1920, 1032);
    private static readonly ScreenArea Second = new(1920, 0, 4480, 1440);

    [Fact]
    public void ThePlaceOnTheSameMonitorsIsUsed()
    {
        var layout = WindowPlacements.LayoutOf([Laptop, Second]);
        var saved = new WindowPlacement(2100, 100, 3600, 1100, Maximised: true, layout);

        Assert.Equal(saved, WindowPlacements.ToRestore([saved], [Second, Laptop], [LaptopWork, Second]));
    }

    [Fact]
    public void OnOtherMonitorsItIsNot()
    {
        var docked = new WindowPlacement(2100, 100, 3600, 1100, false, WindowPlacements.LayoutOf([Laptop, Second]));

        Assert.Null(WindowPlacements.ToRestore([docked], [Laptop], [LaptopWork]));
        Assert.Null(WindowPlacements.ToRestore(null, [Laptop], [LaptopWork]));
        Assert.Null(WindowPlacements.ToRestore([], [Laptop], [LaptopWork]));
    }

    [Fact]
    public void APlaceLessThanHalfOnAWorkAreaIsNotUsed()
    {
        var layout = WindowPlacements.LayoutOf([Laptop]);

        // 1280 wide, 1000 of it past the right edge.
        Assert.Null(WindowPlacements.ToRestore([new WindowPlacement(1640, 100, 2920, 900, false, layout)], [Laptop], [LaptopWork]));

        // Half on: used.
        var half = new WindowPlacement(1280, 100, 2560, 900, false, layout);
        Assert.Equal(half, WindowPlacements.ToRestore([half], [Laptop], [LaptopWork]));

        // Empty, from a hand-edited file.
        Assert.Null(WindowPlacements.ToRestore([new WindowPlacement(100, 100, 100, 900, false, layout)], [Laptop], [LaptopWork]));
    }

    [Fact]
    public void EachArrangementKeepsItsOwnPlaceAndTheOldestGoesFirst()
    {
        var alone = WindowPlacements.LayoutOf([Laptop]);
        var docked = WindowPlacements.LayoutOf([Laptop, Second]);
        WindowPlacementList saved = [new(0, 0, 1280, 800, false, alone)];

        saved = WindowPlacements.With(saved, new(2000, 0, 3280, 800, true, docked));
        saved = WindowPlacements.With(saved, new(10, 10, 1290, 810, false, alone));

        Assert.Equal(2, saved.Count);
        Assert.Equal(10, saved[0].Left);
        Assert.Equal(docked, saved[1].Layout);

        for (var i = 0; i < 10; i++)
        {
            saved = WindowPlacements.With(saved, new(0, 0, 1280, 800, false, "layout " + i));
        }

        Assert.Equal(WindowPlacements.Kept, saved.Count);
        Assert.Equal("layout 9", saved[0].Layout);
    }

    [Fact]
    public void ThePlacesRoundTripThroughShellJson()
    {
        var path = Path.Combine(Path.GetTempPath(), "screentrail-placements-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new ShellPreferencesStore(path);
            var placement = new WindowPlacement(1, 2, 1281, 802, true, WindowPlacements.LayoutOf([Laptop]));
            Assert.True(store.Save(new ShellPreferences { Placements = [placement] }));

            Assert.Equal(placement, Assert.Single(store.Load().Placements));
            Assert.Empty(new ShellPreferencesStore(path + ".missing").Load().Placements);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
