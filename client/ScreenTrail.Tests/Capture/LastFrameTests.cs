using ScreenTrail.Core.Capture;
using ScreenTrail.Core.Detection;
using ScreenTrail.Core.Input;
using ScreenTrail.Shared.Schema;
using ScreenTrail.Tests.Sessions;

namespace ScreenTrail.Tests.Capture;

/// <summary>
/// One picture per change, not one per click (owner, 2026-11-23: "it should only capture the relevant things
/// with steps"). Clicks and scenes share what the last frame kept looked like (LastFrame).
/// </summary>
public sealed class LastFrameTests
{
    private static readonly RemoteTool Rdp = new() { Kind = RemoteToolKind.Rdp, ClientVersion = "10" };

    private static readonly ScopeDecision InScope =
        new(CaptureScope.RemoteTool, "rdp", RemoteToolKind.Rdp, "Recording: Remote Desktop", 11);

    [Fact]
    public async Task ClicksOnAScreenThatDoesNotChangeShareOnePicture()
    {
        await using var harness = await MachineHarness.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await harness.Machine.StartAsync(Rdp, ct: ct);
        var screen = new Screen();
        var recorder = Recorder(harness, screen, new LastFrame());

        for (var i = 0; i < 5; i++)
        {
            await recorder.RecordAsync([Click(harness.Machine.NowMs + i)], ct);
        }

        var stored = (await harness.Store.LoadSessionAsync(harness.Machine.SessionId!, ct))!;
        Assert.Equal(5, stored.Events.OfType<ClickEvent>().Count());
        Assert.Equal(1, screen.Captures);
    }

    [Fact]
    public async Task EveryClickThatFindsTheScreenChangedGetsItsPicture()
    {
        await using var harness = await MachineHarness.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await harness.Machine.StartAsync(Rdp, ct: ct);
        var screen = new Screen();
        var last = new LastFrame();
        var recorder = Recorder(harness, screen, last);

        for (var i = 0; i < 4; i++)
        {
            screen.Show(i);
            await recorder.RecordAsync([Click(harness.Machine.NowMs + i)], ct);
        }

        Assert.Equal(4, screen.Captures);
        Assert.Equal(0, last.Repeated);
    }

    [Fact]
    public async Task ASmallChangeAClickMadeStillCounts()
    {
        // A ticked box or a highlighted row: under the sampler's threshold of 10, over LastFrame's 5.
        await using var harness = await MachineHarness.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await harness.Machine.StartAsync(Rdp, ct: ct);
        var screen = new Screen();
        var recorder = Recorder(harness, screen, new LastFrame());

        await recorder.RecordAsync([Click(harness.Machine.NowMs)], ct);
        var before = PerceptualHash.OfGrid(screen.CaptureSceneGrid()!);
        var nudged = Enumerable.Range(1, 40)
            .Select(cells => (cells, distance: before.DistanceTo(PerceptualHash.OfGrid(Screen.Nudged(screen.CaptureSceneGrid()!, cells)))))
            .First(n => n.distance > LastFrame.DefaultRepeatWithin && n.distance < 10);
        screen.Nudge(nudged.cells);

        await recorder.RecordAsync([Click(harness.Machine.NowMs + 1)], ct);

        Assert.Equal(2, screen.Captures);
    }

    [Fact]
    public async Task ASceneTheClickAlreadyPhotographedIsNotPhotographedAgain()
    {
        await using var harness = await MachineHarness.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await harness.Machine.StartAsync(Rdp, ct: ct);
        var screen = new Screen();
        var last = new LastFrame();
        var clicks = Recorder(harness, screen, last);
        var scenes = new SceneRecorder(harness.Machine, screen, () => InScope, Eager(), lastFrame: last);

        // The scene sampler has a baseline of the first screen; then a click opens a dialog.
        await scenes.SampleAsync(ct);
        screen.Show(1);
        await clicks.RecordAsync([Click(harness.Machine.NowMs)], ct);
        Assert.Equal(1, screen.Captures);

        // The dialog stays up: the sampler compares against the click's frame, so no second picture.
        await scenes.SampleAsync(ct);
        await scenes.SampleAsync(ct);
        Assert.Equal(1, screen.Captures);

        // And a real change after it is still a scene frame.
        screen.Show(2);
        await scenes.SampleAsync(ct);
        Assert.Equal(2, screen.Captures);
    }

    [Fact]
    public async Task WithoutLastFrameEveryClickIsPhotographedAsBefore()
    {
        await using var harness = await MachineHarness.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await harness.Machine.StartAsync(Rdp, ct: ct);
        var screen = new Screen();
        var recorder = Recorder(harness, screen, lastFrame: null);

        for (var i = 0; i < 3; i++)
        {
            await recorder.RecordAsync([Click(harness.Machine.NowMs + i)], ct);
        }

        Assert.Equal(3, screen.Captures);
    }

    [Fact]
    public async Task ANewSessionsFirstClickIsAlwaysPhotographed()
    {
        await using var harness = await MachineHarness.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await harness.Machine.StartAsync(Rdp, ct: ct);
        var screen = new Screen();
        var recorder = Recorder(harness, screen, new LastFrame());

        await recorder.RecordAsync([Click(harness.Machine.NowMs)], ct);
        recorder.Reset();
        await recorder.RecordAsync([Click(harness.Machine.NowMs + 1)], ct);

        Assert.Equal(2, screen.Captures);
    }

    private static SessionRecorder Recorder(MachineHarness harness, Screen screen, LastFrame? lastFrame) =>
        new(harness.Machine, screen, () => InScope, new ClickDebouncer(window: TimeSpan.Zero), lastFrame: lastFrame);

    private static SceneSampler Eager() => new(options: new SceneSamplerOptions { MinimumGap = TimeSpan.Zero });

    private static ClickEvent Click(long tsMs) => new() { TsMs = tsMs, X = 4, Y = 5, Button = MouseButton.Left };

    /// <summary>A window whose picture the test sets: screen N is a pattern no other screen resembles.</summary>
    private sealed class Screen : IScreenshotCapturer
    {
        private byte[] _grid = Pattern(0);

        public int Captures { get; private set; }

        public void Show(int screen) => _grid = Pattern(screen);

        /// <summary>Changes a few cells, as a ticked box or a highlighted row would.</summary>
        public void Nudge(int cells) => _grid = Nudged(_grid, cells);

        public static byte[] Nudged(byte[] from, int cells)
        {
            var grid = (byte[])from.Clone();
            for (var i = 0; i < cells; i++)
            {
                var at = (i * 7) % grid.Length;
                grid[at] = (byte)(grid[at] + 24);
            }

            return grid;
        }

        public CapturedFrame? CaptureForegroundWindow(nint expected = 0)
        {
            Captures++;
            return new CapturedFrame([1, 2, 3, 4], 100, 80, 200, 160, default);
        }

        public byte[]? CaptureSceneGrid(nint expected = 0) => (byte[])_grid.Clone();

        private static byte[] Pattern(int screen)
        {
            var random = new Random(1000 + screen);
            var grid = new byte[PerceptualHash.GridLength];
            random.NextBytes(grid);
            return grid;
        }
    }
}
