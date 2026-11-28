using ScreenTrail.Core.Notifications;
using ScreenTrail.Core.Shell;
using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Tests.Notifications;

/// <summary>
/// F-09, owner decision DEC-02: the "Draft ready" toast keeps its words, and Windows is not left holding
/// them longer than they are of use. The UI takes the toast away once its session is no longer the draft
/// waiting, and clears ScreenTrail's toasts on "Delete everything" and at uninstall. The decisions are
/// here; the Windows calls are in the UI's WindowsToasts and the service's uninstall step.
/// </summary>
public sealed class DraftToastsTests
{
    [Fact]
    public void WhileItsSessionIsTheDraftReadyItsToastStays()
    {
        var ready = new CaptureStateSnapshot { State = CaptureStates.DraftReady, SessionId = "s1", DraftTitle = "Printer offline" };

        Assert.Empty(DraftToasts.ToRemove([TagOf(ready)], ready));
    }

    [Theory]
    [InlineData(CaptureStates.Idle, null)]
    [InlineData(CaptureStates.Recording, "s2")]
    [InlineData(CaptureStates.DraftFailed, "s1")]
    [InlineData(CaptureStates.DraftReady, "s2")]
    [InlineData(CaptureStates.StoreUnavailable, null)]
    public void OnceItsSessionIsNoLongerTheDraftReadyItsToastGoes(string state, string? session)
    {
        var ready = new CaptureStateSnapshot { State = CaptureStates.DraftReady, SessionId = "s1", DraftTitle = "Printer offline" };
        var now = new CaptureStateSnapshot { State = state, SessionId = session };

        Assert.Equal([TagOf(ready)], DraftToasts.ToRemove([TagOf(ready)], now));
    }

    [Fact]
    public void ToastsThatNameNoDraftAreLeftToExpire()
    {
        var idle = CaptureStateSnapshot.Idle;

        Assert.Empty(DraftToasts.ToRemove(["draft-failed:s1", "outbox-offline", "update-ready:1.2.3"], idle));
    }

    [Fact]
    public void NothingIsTakenAwayWhileTheServiceIsNotAnswering()
    {
        Assert.Empty(DraftToasts.ToRemove(["draft-ready:s1"], known: null));
    }

    [Fact]
    public void TheTagLookedForIsTheOneTheToastWasShownWith()
    {
        var ready = new CaptureStateSnapshot { State = CaptureStates.DraftReady, SessionId = Guid.NewGuid().ToString("N"), DraftTitle = "t", DraftCompany = "c" };

        Assert.StartsWith(DraftToasts.KeyPrefix, TagOf(ready), StringComparison.Ordinal);
        Assert.Equal([TagOf(ready)], DraftToasts.ToRemove([TagOf(ready)], CaptureStateSnapshot.Idle));
    }

    [Fact]
    public void TheToastsWordsAndArgumentsAreAsTheyWere()
    {
        // DEC-02: the text stays as Spec section 6 has it, and the toast carries only action= and one value.
        var notice = Notices.For(new CaptureStateSnapshot { State = CaptureStates.DraftReady, SessionId = "s1", DraftTitle = "Printer offline", DraftCompany = "Acme Dental" })!;
        var xml = ToastContent.Xml(notice);

        Assert.Equal("Printer offline, Acme Dental. Select to open it.", notice.Text);
        Assert.Contains("launch=\"action=Review\"", xml, StringComparison.Ordinal);
    }

    [Fact(Timeout = 30_000)]
    public async Task ADeleteEverythingTheServiceAcceptedClearsTheToasts()
    {
        var ct = TestContext.Current.CancellationToken;
        var cleared = 0;
        var erase = Erase(ok: true, () => cleared++);

        var result = await erase.RunAsync(_ => Task.FromResult(true), ct);

        Assert.True(result.Done);
        Assert.Equal(1, cleared);
    }

    [Fact(Timeout = 30_000)]
    public async Task ADeleteEverythingRefusedOrDeclinedLeavesThem()
    {
        var ct = TestContext.Current.CancellationToken;
        var cleared = 0;

        _ = await Erase(ok: false, () => cleared++).RunAsync(_ => Task.FromResult(true), ct);
        _ = await Erase(ok: true, () => cleared++).RunAsync(_ => Task.FromResult(false), ct);

        Assert.Equal(0, cleared);
    }

    [Fact(Timeout = 30_000)]
    public async Task TheUninstallsClearIsGivenUpWhenWindowsDoesNotAnswer()
    {
        var ct = TestContext.Current.CancellationToken;
        using var never = new ManualResetEventSlim(false);
        try
        {
            var cleared = await Task.Run(() => DraftToasts.TryWithin(never.Wait, TimeSpan.FromMilliseconds(50)), ct);

            Assert.False(cleared);
        }
        finally
        {
            never.Set();
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task TheUninstallsClearSaysWhetherItWorked()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.True(await Task.Run(() => DraftToasts.TryWithin(() => { }, TimeSpan.FromSeconds(20)), ct));
        Assert.False(await Task.Run(() => DraftToasts.TryWithin(() => throw new InvalidOperationException("no platform"), TimeSpan.FromSeconds(20)), ct));
    }

    private static string TagOf(CaptureStateSnapshot ready) => ToastContent.Tag(Notices.For(ready)!);

    private static DeleteEverything Erase(bool ok, Action erased)
    {
        var state = new ShellState();
        state.Connected(CaptureStateSnapshot.Idle);
        return new DeleteEverything(
            state,
            (_, _) => Task.FromResult<ConfirmationIssued?>(new ConfirmationIssued { Token = "t", Phrase = "DELETE EVERYTHING" }),
            (_, _) => Task.FromResult(new CommandResult { RequestId = 1, Ok = ok, Error = ok ? null : "The token has expired." }),
            erased);
    }
}
