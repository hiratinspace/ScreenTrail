using ScreenTrail.Core.Install;

namespace ScreenTrail.Tests.Settings;

/// <summary>
/// The product was ScreenTail until 2026-11-17. Its old data folder is no longer brought across at start
/// (security review F-11, owner decision DEC-12): only machines that ran a build from before the rename have
/// one, no customer does, and a folder moved in part left a store and key outside what "Delete everything"
/// erases. Nothing at start reads, moves or merges it. An uninstall still removes it, with the same checks
/// as today's folder.
/// </summary>
public sealed class LegacyDataFolderTests
{
    [Fact]
    public void NoMoveOfTheOldFolderIsBuiltIn()
    {
        Assert.Null(Type.GetType("ScreenTrail.Shared.Settings.LegacyDataFolder, ScreenTrail.Shared"));
        Assert.Null(Type.GetType("ScreenTrail.Shared.Settings.LegacyDataMigration, ScreenTrail.Shared"));
    }

    [Theory]
    [InlineData("ScreenTrail.Service", "Program.cs")]
    [InlineData("ScreenTrail.UI", "App.xaml.cs")]
    public void NeitherTheServiceNorTheWindowTouchesTheOldFolderAtStart(string project, string file)
    {
        var source = File.ReadAllText(Path.Combine(Repository(), "client", project, file));

        Assert.DoesNotContain("LegacyDataFolder", source, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\ScreenTail", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ScreenTail\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNamesAreTheOnesTheUninstallRemoves()
    {
        Assert.Equal(["ScreenTrail", "ScreenTail"], InstalledFootprint.DataFolderNames);
        Assert.Equal(["AppData", "Local", "ScreenTail"], InstalledFootprint.DataSegmentsFor("ScreenTail"));
        Assert.Equal(InstalledFootprint.DataSegments, InstalledFootprint.DataSegmentsFor(null));
        Assert.Throws<ArgumentException>(() => InstalledFootprint.DataSegmentsFor("Elsewhere"));
    }

    [Fact]
    public void TheUninstallPlansTheOldFolderWithTheSameChecks()
    {
        var profile = @"C:\Users\ana";
        var states = new Dictionary<string, PathState>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\Users\ana\AppData"] = PathState.Folder,
            [@"C:\Users\ana\AppData\Local"] = PathState.Folder,
            [@"C:\Users\ana\AppData\Local\ScreenTail"] = PathState.Folder,
        };

        var decision = Assert.Single(InstalledFootprint.PlanDataRemoval(
            [new("S-1-5-21-1", profile)],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "S-1-5-21-1" },
            path => states.TryGetValue(path, out var state) ? state : PathState.Missing,
            "ScreenTail"));

        Assert.Equal(DataFolderAction.DeleteAsOwner, decision.Action);
        Assert.Equal(@"C:\Users\ana\AppData\Local\ScreenTail", decision.Folder);

        states[@"C:\Users\ana\AppData\Local\ScreenTail"] = PathState.Link;
        Assert.Equal(DataFolderAction.LeaveLinked, InstalledFootprint.PlanDataRemoval(
            [new("S-1-5-21-1", profile)],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "S-1-5-21-1" },
            path => states.TryGetValue(path, out var state) ? state : PathState.Missing,
            "ScreenTail")[0].Action);
    }

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "client", "ScreenTrail.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Not inside the repository.");
    }
}
