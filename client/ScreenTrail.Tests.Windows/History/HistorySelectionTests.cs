using System.Runtime.Versioning;
using ScreenTrail.Core.History;
using ScreenTrail.Core.Shell;
using ScreenTrail.Shared.Ipc;
using ScreenTrail.Tests.Windows.Review;
using ScreenTrail.UI.History;

namespace ScreenTrail.Tests.Windows.History;

/// <summary>
/// Review finding D-22: reading the list again keeps what the technician selected and the rows that did
/// not change. It cleared the table and added every row back, which dropped the selection and scrolled to
/// the top whenever another session started.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HistorySelectionTests
{
    [Fact]
    public void ReadingTheListAgainKeepsTheSelectionAndTheRowsThatDidNotChange()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        UiThread.Run(async () =>
        {
            var rows = new List<SessionRow> { Row("s1", "draft"), Row("s2", "draft"), Row("s3", "published"), Row("s4", "draft") };
            var model = new HistoryViewModel(new ShellState(), _ => Task.FromResult<IReadOnlyList<SessionRow>?>([.. rows]));
            await model.RefreshAsync();
            model.Select([model.Rows[1], model.Rows[2]]);
            var unchanged = model.Rows[1];
            var restored = 0;
            model.SelectionRestored += (_, _) => restored++;

            // Another session starts, and one of the selected sessions changes.
            rows.Insert(0, Row("s0", "recording"));
            rows[3] = rows[3] with { Status = "discarded" };
            await model.RefreshAsync();

            Assert.Equal(["s0", "s1", "s2", "s3", "s4"], model.Rows.Select(r => r.Id));
            Assert.Equal(["s2", "s3"], model.Selected.Select(r => r.Id));
            Assert.Same(unchanged, model.Rows[2]);
            Assert.Equal("Discarded", model.Selected[1].Status);
            Assert.Equal(1, restored);
        });
    }

    [Fact]
    public void ARowTheFiltersHideLeavesTheSelection()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        UiThread.Run(async () =>
        {
            var model = new HistoryViewModel(new ShellState(), _ => Task.FromResult<IReadOnlyList<SessionRow>?>([Row("s1", "draft"), Row("s2", "published")]));
            await model.RefreshAsync();
            model.Select(model.Rows);

            model.StatusChoice = HistoryFilters.StatusChoices.First(c => c.Value == "draft");

            Assert.Equal(["s1"], model.Selected.Select(r => r.Id));
            Assert.Equal("1 session selected.", model.SelectionNote);
        });
    }

    /// <summary>Review finding A-13: Open is off with nothing selected, where it was a filled button that did nothing.</summary>
    [Fact]
    public void OpenIsOffUntilARowIsSelected()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        UiThread.Run(async () =>
        {
            var model = new HistoryViewModel(new ShellState(), _ => Task.FromResult<IReadOnlyList<SessionRow>?>([Row("s1", "draft")]));
            await model.RefreshAsync();

            Assert.False(model.OpenCommand.CanExecute(null));
            Assert.True(model.OpenCommand.CanExecute(model.Rows[0]));
        });
    }

    private static SessionRow Row(string id, string status) => new()
    {
        Id = id,
        StartedAt = DateTimeOffset.UnixEpoch.AddMinutes(-int.Parse(id[1..], System.Globalization.CultureInfo.InvariantCulture)),
        DurationMs = 60_000,
        Status = status,
        Frames = 1,
        FramesPurged = 0,
    };
}
