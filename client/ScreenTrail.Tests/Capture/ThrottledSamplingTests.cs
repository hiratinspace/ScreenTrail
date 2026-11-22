using ScreenTrail.Core.Capture;
using ScreenTrail.Core.Detection;
using ScreenTrail.Core.Perf;
using ScreenTrail.Shared.Schema;
using ScreenTrail.Tests.Performance;
using ScreenTrail.Tests.Sessions;

namespace ScreenTrail.Tests.Capture;

/// <summary>
/// ST-031 AC2 on the sampling side: over the processor budget the screen is looked at less often, and that
/// is the first thing to give. A look skipped is a picture never taken, so a frame that is taken is staged
/// waiting for redaction exactly as at full capture (INV-1).
/// </summary>
public sealed class ThrottledSamplingTests
{
    private static readonly RemoteTool ScreenConnect = new() { Kind = RemoteToolKind.Screenconnect, ClientVersion = "24.1" };

    private static readonly DateTimeOffset At = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(CaptureLevel.Full, 60)]
    [InlineData(CaptureLevel.FewerSamples, 30)]
    [InlineData(CaptureLevel.FewestSamples, 15)]
    [InlineData(CaptureLevel.OcrOneAtATime, 15)]
    [InlineData(CaptureLevel.OcrClicksOnly, 2)]
    public async Task OverBudgetTheScreenIsLookedAtLessOftenButNeverNotAtAll(CaptureLevel level, int looks)
    {
        // #232: the deepest level used to stop looking, and a throttled session went without scene frames
        // for long stretches. It now looks on one tick in thirty: two looks a minute.
        await using var harness = await MachineHarness.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await harness.Machine.StartAsync(ScreenConnect, ct: ct);
        var capturer = new FlippingCapturer();
        var scenes = new SceneRecorder(harness.Machine, capturer, InScope, EagerSampler(), Throttles.At(level));

        for (var tick = 0; tick < 60; tick++)
        {
            await scenes.SampleAsync(ct);
        }

        Assert.Equal(looks, capturer.Grids);
    }

    [Fact]
    public async Task AtTheFloorAScreenThatKeepsChangingGetsAFrameAtMostEveryThirtySeconds()
    {
        // The floor's bound: ten minutes of a screen that differs from the last picture on every look, at the
        // deepest level, are at most one frame every thirty seconds however much it changes. Each tick is a
        // second on the sampler's clock, as the sampling loop ticks.
        await using var harness = await MachineHarness.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await harness.Machine.StartAsync(ScreenConnect, ct: ct);
        var clock = new ManualTime(At);
        var capturer = new ScriptedCapturer((grids, captures) => grids == 0 || captures % 2 == 1 ? Rising : Falling);
        var scenes = new SceneRecorder(harness.Machine, capturer, InScope, new SceneSampler(clock), Throttles.At(CaptureLevel.OcrClicksOnly));
        var capturedAt = new List<int>();

        for (var second = 1; second <= 600; second++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            var before = capturer.Captures;
            await scenes.SampleAsync(ct);
            if (capturer.Captures > before)
            {
                capturedAt.Add(second);
            }
        }

        // The baseline at 30 s, then a frame on every later look: 60 s, 90 s, ... 600 s.
        Assert.Equal(20, capturer.Grids);
        Assert.Equal(Enumerable.Range(2, 19).Select(look => look * 30), capturedAt);
        Assert.Equal(19, scenes.Counts.Kept);
        while (harness.Pending.TryTake(out var queued))
        {
            Assert.Equal(FrameTrigger.SceneChange, queued.Frame.Trigger);
        }

        Assert.Equal(0, await harness.Store.CountAllPendingFramesAsync(ct));
    }

    [Fact]
    public async Task AtTheFloorAStillScreenCostsNoFrames()
    {
        // The floor only keeps a screen that changed meaningfully, under the sampler's usual threshold: a
        // technician reading a page while narrating gets looks, not pictures of the same page.
        await using var harness = await MachineHarness.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await harness.Machine.StartAsync(ScreenConnect, ct: ct);
        var clock = new ManualTime(At);
        var capturer = new ScriptedCapturer((_, _) => Rising);
        var scenes = new SceneRecorder(harness.Machine, capturer, InScope, new SceneSampler(clock), Throttles.At(CaptureLevel.OcrClicksOnly));

        for (var second = 1; second <= 600; second++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await scenes.SampleAsync(ct);
        }

        Assert.Equal(20, capturer.Grids);
        Assert.Equal(0, capturer.Captures);
        Assert.False(harness.Pending.TryTake(out _));
    }

    [Fact]
    public async Task AtTheFloorADialogThatAppearsWhileTheTechnicianOnlyWatchesIsPhotographedAndRedactedFirst()
    {
        // The defect M1 showed: a technician narrating, not clicking, and an error dialog appearing. At the
        // floor it is photographed on the next look, within thirty seconds, and staged waiting for
        // redaction like every other frame (INV-1, ADR-0006): in memory, never in the store unread.
        await using var harness = await MachineHarness.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await harness.Machine.StartAsync(ScreenConnect, ct: ct);
        var clock = new ManualTime(At);
        const int dialogAt = 100;
        var second = 0;
        var capturer = new ScriptedCapturer((_, _) => second >= dialogAt ? Falling : Rising);
        var scenes = new SceneRecorder(harness.Machine, capturer, InScope, new SceneSampler(clock), Throttles.At(CaptureLevel.OcrClicksOnly));
        int? capturedAt = null;

        for (second = 1; second <= 180; second++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await scenes.SampleAsync(ct);
            capturedAt ??= capturer.Captures > 0 ? second : null;
        }

        Assert.Equal(1, capturer.Captures);
        Assert.InRange(capturedAt!.Value, dialogAt, dialogAt + 30);
        Assert.True(harness.Pending.TryTake(out var queued));
        Assert.Equal(FrameTrigger.SceneChange, queued.Frame.Trigger);
        Assert.Equal(0, await harness.Store.CountAllPendingFramesAsync(ct));
        Assert.Empty((await harness.Store.LoadSessionAsync(harness.Machine.SessionId!, ct))!.Frames);
    }

    [Fact]
    public async Task AFrameTakenOverBudgetIsStagedWaitingForRedactionAsAtFullCapture()
    {
        await using var harness = await MachineHarness.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await harness.Machine.StartAsync(ScreenConnect, ct: ct);
        var capturer = new FlippingCapturer();
        var scenes = new SceneRecorder(harness.Machine, capturer, InScope, EagerSampler(), Throttles.At(CaptureLevel.OcrOneAtATime));

        for (var tick = 0; tick < 8; tick++)
        {
            await scenes.SampleAsync(ct);
        }

        // Two looks: the baseline, and one change that was kept.
        Assert.Equal(1, capturer.Captures);
        Assert.True(harness.Pending.TryTake(out var queued));
        Assert.Equal(FrameTrigger.SceneChange, queued.Frame.Trigger);
        Assert.Equal(0, await harness.Store.CountAllPendingFramesAsync(ct));
        Assert.Empty((await harness.Store.LoadSessionAsync(harness.Machine.SessionId!, ct))!.Frames);
    }

    [Fact]
    public async Task AThrottledSamplerStillSeesTheSessionEnd()
    {
        // Skipping looks must not skip the reset: a session that stops and starts again is a new scene,
        // so its first look is a baseline and never a frame.
        await using var harness = await MachineHarness.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await harness.Machine.StartAsync(ScreenConnect, ct: ct);
        var capturer = new FlippingCapturer();
        var scenes = new SceneRecorder(harness.Machine, capturer, InScope, EagerSampler(), Throttles.At(CaptureLevel.FewerSamples));
        await scenes.SampleAsync(ct);
        await scenes.SampleAsync(ct);   // the baseline

        Assert.True(await harness.Machine.PauseAsync(ct));
        await scenes.SampleAsync(ct);
        Assert.True(await harness.Machine.ResumeAsync(ct));
        await scenes.SampleAsync(ct);
        await scenes.SampleAsync(ct);   // a baseline again, not a change

        Assert.Equal(2, capturer.Grids);
        Assert.Equal(0, capturer.Captures);
    }

    private static ScopeDecision? InScope() =>
        new(CaptureScope.RemoteTool, "screenconnect", RemoteToolKind.Screenconnect, "Capturing ScreenConnect.", 99);

    /// <summary>No quiet period, so every changed look is kept and the tests need no clock.</summary>
    private static SceneSampler EagerSampler() => new(options: new SceneSamplerOptions { MinimumGap = TimeSpan.Zero });

    private static readonly byte[] Rising = Ramp(rising: true);
    private static readonly byte[] Falling = Ramp(rising: false);

    private static byte[] Ramp(bool rising)
    {
        var grid = new byte[PerceptualHash.GridLength];
        for (var i = 0; i < grid.Length; i++)
        {
            var value = ((i % PerceptualHash.Columns) * 10) + ((i / PerceptualHash.Columns) * 5);
            grid[i] = (byte)(rising ? value : 255 - value);
        }

        return grid;
    }

    /// <summary>A scene grid that flips between two opposite ramps on every look, so every look after the first is a change.</summary>
    private sealed class FlippingCapturer() : ScriptedCapturer((grids, _) => grids % 2 == 0 ? Rising : Falling);

    /// <summary>A scene grid chosen per look from how many looks and captures came before it.</summary>
    private class ScriptedCapturer(Func<int, int, byte[]> screen) : IScreenshotCapturer
    {
        public int Captures { get; private set; }

        public int Grids { get; private set; }

        public CapturedFrame? CaptureForegroundWindow(nint expected = 0)
        {
            Captures++;
            return new CapturedFrame([1, 2, 3, 4], 100, 80, 200, 160, default);
        }

        public byte[]? CaptureSceneGrid(nint expected = 0)
        {
            var grid = screen(Grids, Captures);
            Grids++;
            return grid;
        }
    }
}
