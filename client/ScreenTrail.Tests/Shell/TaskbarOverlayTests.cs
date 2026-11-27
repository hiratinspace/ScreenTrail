using ScreenTrail.Core.Shell;
using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Tests.Shell;

/// <summary>Review finding D-15: the taskbar button marks recording and paused, and nothing else.</summary>
public sealed class TaskbarOverlayTests
{
    [Theory]
    [InlineData(CaptureStates.Recording, TaskbarMark.Recording)]
    [InlineData(CaptureStates.Paused, TaskbarMark.Paused)]
    [InlineData(CaptureStates.Suppressed, TaskbarMark.Paused)]
    [InlineData(CaptureStates.Idle, TaskbarMark.None)]
    [InlineData(CaptureStates.Finalizing, TaskbarMark.None)]
    [InlineData(CaptureStates.DraftReady, TaskbarMark.None)]
    public void TheMarkFollowsCapture(string state, TaskbarMark mark)
    {
        var snapshot = new ShellSnapshot(ServiceConnection.Connected, new CaptureStateSnapshot { State = state }, ShellView.Review, null);
        Assert.Equal(mark, TaskbarOverlay.For(snapshot));
    }

    [Fact]
    public void NoServiceNoMark()
    {
        var snapshot = new ShellSnapshot(ServiceConnection.Unavailable, new CaptureStateSnapshot { State = CaptureStates.Recording }, ShellView.Review, null);
        Assert.Equal(TaskbarMark.None, TaskbarOverlay.For(snapshot));
        Assert.Equal(TaskbarMark.None, TaskbarOverlay.For(snapshot with { Connection = ServiceConnection.Connected, Capture = null }));
    }
}
