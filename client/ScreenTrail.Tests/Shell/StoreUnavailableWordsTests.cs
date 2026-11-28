using ScreenTrail.Core.Hud;
using ScreenTrail.Core.Shell;
using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Tests.Shell;

/// <summary>
/// F-10, the UI's half: while the service's store will not open, the pill, the tray and the shell say the
/// same thing, that nothing is being recorded and why, and the tray offers the retry. Nothing that captures
/// or discards is offered: the service would refuse it.
/// </summary>
public sealed class StoreUnavailableWordsTests
{
    private static readonly CaptureStateSnapshot Capture = new() { State = CaptureStates.StoreUnavailable, Reason = StoreUnavailableReasons.KeyRefused };

    private static ShellSnapshot Snapshot => new(ServiceConnection.Connected, Capture, ShellView.Review, null);

    [Fact]
    public void ThePillTheTrayAndTheShellSayNothingIsRecordedAndWhy()
    {
        Assert.Equal(StoreUnavailableWords.Line, HudState.For(Capture).State.Text);
        Assert.Equal(StoreUnavailableWords.Line, TrayPresence.From(Snapshot).StateLine);
        Assert.Equal(StoreUnavailableWords.Line, ShellStatus.ChipWords(Snapshot));
        Assert.StartsWith("Not recording", StoreUnavailableWords.Line, StringComparison.Ordinal);
        Assert.Contains("password", TrayPresence.From(Snapshot).Counts, StringComparison.Ordinal);
        Assert.Contains("password", HudState.For(Capture).State.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingIsCapturedSoThePillMayBePutAway()
    {
        Assert.True(HudState.KnownIdle(Capture));
        Assert.Equal(HudTone.Idle, ShellStatus.Tone(Snapshot));
    }

    [Fact]
    public void TheTrayOffersTheRetryAndNothingThatCapturesOrDiscards()
    {
        var rows = TrayMenu.For(Snapshot, pillHidden: false);
        var commands = rows.Where(r => r.Kind == TrayRowKind.Item).Select(r => r.Command).ToList();

        Assert.Contains(TrayCommand.RetryStore, commands);
        Assert.True(rows.Single(r => r.Command == TrayCommand.RetryStore).Enabled);
        Assert.DoesNotContain(TrayCommand.Start, commands);
        Assert.DoesNotContain(TrayCommand.Discard, commands);
        Assert.DoesNotContain(TrayCommand.Pause, commands);
        Assert.DoesNotContain(TrayCommand.Stop, commands);
        Assert.Contains(TrayCommand.Settings, commands);
    }

    [Fact]
    public void AReasonThisBuildDoesNotKnowIsStillSaidPlainly()
    {
        Assert.False(string.IsNullOrWhiteSpace(StoreUnavailableWords.Why("something_new")));
    }
}
