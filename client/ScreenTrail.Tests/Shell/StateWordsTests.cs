using ScreenTrail.Core.Hud;
using ScreenTrail.Core.Review;
using ScreenTrail.Core.Shell;
using ScreenTrail.Shared.Ipc;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Tests.Shell;

/// <summary>
/// The pill, the tray and the shell's status chip say the same words for the same state (review findings
/// E-01, E-02; design system 10.10). "Is it recording?" is the one question the product must answer at a
/// glance, and the three used to call no session "Not recording", "Not capturing" and "Idle".
/// </summary>
public sealed class StateWordsTests
{
    public static TheoryData<string, string?, string> States => new()
    {
        { CaptureStates.Idle, null, "Not recording" },
        { CaptureStates.DraftPending, null, "Not recording: drafting the note" },
        { CaptureStates.Finalizing, null, "Finishing up" },

        // A privacy pause names its reason everywhere, from SuppressionWords (E-02).
        { CaptureStates.Suppressed, CaptureReasons.PasswordField, "Paused: password field" },
        { CaptureStates.Suppressed, CaptureReasons.SensitiveContext, "Paused: sign-in screen" },
        { CaptureStates.Suppressed, CaptureReasons.ExcludedApp, "Paused: excluded app" },
        { CaptureStates.Suppressed, CaptureReasons.ElevatedWindow, "Paused: admin window" },
        { CaptureStates.Suppressed, null, "Paused" },
    };

    [Theory]
    [MemberData(nameof(States))]
    public void ThePillTheTrayAndTheShellAgree(string state, string? reason, string words)
    {
        var capture = new CaptureStateSnapshot { State = state, Reason = reason };
        var snapshot = new ShellSnapshot(ServiceConnection.Connected, capture, ShellView.Review, null);

        Assert.Equal(words, HudState.For(capture).State.Text);
        Assert.Equal(words, TrayPresence.From(snapshot).StateLine);
        Assert.Equal(words, ShellStatus.ChipWords(snapshot));
    }

    [Theory]
    [InlineData(CaptureStateReason.PasswordField, "Paused: a password field had focus")]
    [InlineData(CaptureStateReason.SensitiveContext, "Paused: a sign-in screen was in front")]
    [InlineData(CaptureStateReason.ExcludedApp, "Paused: an excluded app was in front")]
    [InlineData(CaptureStateReason.ElevatedWindow, "Paused: an admin window was in front")]
    [InlineData(CaptureStateReason.User, "Paused by you")]
    public void TheStripAndTheTimelineSayTheSameReasonInThePast(CaptureStateReason reason, string words)
    {
        Assert.Equal(words, SuppressionWords.Past(reason));
        Assert.Equal(words, new GapCell(0, 1000, reason).Description);
    }

    [Fact]
    public void NotCapturingIsOnlyEverSaidWithTheAppItSkips()
    {
        var idle = new CaptureStateSnapshot { State = CaptureStates.Idle };
        var snapshot = new ShellSnapshot(ServiceConnection.Connected, idle, ShellView.Review, null);
        Assert.DoesNotContain("capturing", TrayPresence.From(snapshot).StateLine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("capturing", ShellStatus.Describe(snapshot), StringComparison.OrdinalIgnoreCase);

        var skipping = new CaptureStateSnapshot { State = CaptureStates.Suppressed, Reason = CaptureReasons.OutOfScope, ScopeProcess = "OUTLOOK" };
        Assert.Equal("Not capturing: Outlook", HudState.For(skipping).State.Text);
    }
}
