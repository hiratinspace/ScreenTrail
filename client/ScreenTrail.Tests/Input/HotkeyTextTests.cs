using ScreenTrail.Core.History;
using ScreenTrail.Core.Hud;
using ScreenTrail.Core.Input;
using ScreenTrail.Core.Sessions;
using ScreenTrail.Core.Shell;
using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Tests.Input;

/// <summary>HotkeyText is process-wide, so the tests that change it run alone, after the rest.</summary>
[CollectionDefinition(nameof(HotkeyTextSerial), DisableParallelization = true)]
public sealed class HotkeyTextSerial;

/// <summary>
/// Every string that teaches a chord teaches the one bound now (review finding E-03). After a technician
/// rebound Pause in Settings, the pill, the tray, the empty screens and the service's refusals all kept
/// saying the shipped Ctrl+Alt+P.
/// </summary>
[Collection(nameof(HotkeyTextSerial))]
public sealed class HotkeyTextTests
{
    [Fact]
    public void UntilTheBindingsAreKnownTheShippedChordsAreSaid()
    {
        Assert.Equal("Ctrl+Alt+P", HotkeyText.For(HotkeyAction.PauseOrResume));
        Assert.Equal(string.Empty, HotkeyText.For(HotkeyAction.DiscardSession));
        Assert.Equal("Ctrl⁠+⁠Alt⁠+⁠R", HotkeyText.Unbroken(HotkeyAction.StartCapture));
    }

    [Fact]
    public void AReboundChordIsWhatEverySurfaceSays()
    {
        var bound = new Dictionary<HotkeyAction, Hotkey>(HotkeyBindings.Defaults)
        {
            [HotkeyAction.StartCapture] = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, "F9"),
            [HotkeyAction.PauseOrResume] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, "K"),
            [HotkeyAction.StopAndDraft] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, "J"),
        };
        var changed = 0;
        void Count(object? sender, EventArgs e) => changed++;
        HotkeyText.Changed += Count;
        try
        {
            HotkeyText.Use(new HotkeyBindings(bound));
            var start = HotkeyText.For(HotkeyAction.StartCapture);
            Assert.Equal(1, changed);

            var paused = HudState.For(new CaptureStateSnapshot { State = CaptureStates.Paused, Reason = CaptureReasons.User });
            Assert.Equal("Press Ctrl+Alt+K to resume.", paused.State.Tooltip);
            Assert.Equal("Ctrl+Alt+K", HudControls.Pause.Hotkey);
            Assert.Equal("Ctrl+Alt+J", HudControls.Stop.Hotkey);
            Assert.Equal(start, TrayMenu.StartHotkey);
            Assert.Contains($"or press {start}.", TrayMenu.StartRefused, StringComparison.Ordinal);
            Assert.Contains($"or press {start}.", HandStart.NoRemoteSession, StringComparison.Ordinal);
            Assert.Contains(HotkeyText.Unbroken(HotkeyAction.StartCapture), SessionHistory.Empty, StringComparison.Ordinal);
            Assert.EndsWith("stop it with Ctrl+Alt+J.", ShellStatus.NothingToReviewYet, StringComparison.Ordinal);
            Assert.DoesNotContain("Ctrl+Alt+P", paused.Spoken, StringComparison.Ordinal);

            // The same bindings again change nothing and say nothing.
            HotkeyText.Use(new HotkeyBindings(bound));
            Assert.Equal(1, changed);
        }
        finally
        {
            HotkeyText.Changed -= Count;
            HotkeyText.Use(new HotkeyBindings());
        }
    }
}
