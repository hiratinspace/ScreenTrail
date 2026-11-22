using ScreenTrail.Core.Perf;

namespace ScreenTrail.Tests.Performance;

/// <summary>
/// ST-031's live processor figure in "What's being captured right now?": the share the throttle acts on
/// while a session is under way, the share since the panel last asked between sessions, and "measuring"
/// rather than a number nobody measured. A processor clock and a wall clock moved by hand.
/// </summary>
public sealed class ProcessorShareGaugeTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheFirstAskSaysMeasuringAndTheNextGivesTheShareSinceIt()
    {
        var (gauge, cpu, time) = Rig(processors: 4);

        Assert.Null(gauge.ShareOfMachine());

        // One core of four busy for the two seconds between the panel's refreshes.
        cpu.Use(TimeSpan.FromSeconds(2));
        time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(0.25, gauge.ShareOfMachine()!.Value, 6);
    }

    [Fact]
    public void WhileASessionRecordsThePanelShowsTheShareTheThrottleMeasured()
    {
        var (gauge, cpu, time) = Rig(processors: 8);
        var throttle = new CaptureThrottle(gauge, time);
        throttle.Observe();
        for (var second = 0; second < 5; second++)
        {
            // Two cores of eight: 25% of the machine, well over the 7.5% budget.
            cpu.Use(TimeSpan.FromSeconds(2));
            time.Advance(TimeSpan.FromSeconds(1));
            throttle.Observe();
        }

        var reads = cpu.Reads;
        time.Advance(TimeSpan.FromMilliseconds(300));

        // Under a second since the throttle's reading: the panel is told that reading, and takes no new one.
        Assert.Equal(0.25, gauge.ShareOfMachine()!.Value, 6);
        Assert.Equal(reads, cpu.Reads);
    }

    [Fact]
    public void ReadingTheGaugeDoesNotChangeWhatTheThrottleSees()
    {
        // The panel's readings land between the throttle's; the throttle measures from its own last one.
        var (gauge, cpu, time) = Rig(processors: 4);
        var throttle = new CaptureThrottle(gauge, time);
        throttle.Observe();
        for (var second = 0; second < 10; second++)
        {
            cpu.Use(TimeSpan.FromSeconds(2));
            time.Advance(TimeSpan.FromMilliseconds(500));
            _ = gauge.ShareOfMachine();
            time.Advance(TimeSpan.FromMilliseconds(500));
            throttle.Observe();
        }

        Assert.Equal(CaptureLevel.FewerSamples, throttle.Level);
    }

    [Fact]
    public void TwoReadingsTooFarApartAreNotAFigureForNow()
    {
        var (gauge, cpu, time) = Rig(processors: 4);
        Assert.Null(gauge.ShareOfMachine());

        // The panel was closed for a minute: an average over it is not "right now".
        cpu.Use(TimeSpan.FromSeconds(60));
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(gauge.ShareOfMachine());

        cpu.Use(TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(0.125, gauge.ShareOfMachine()!.Value, 6);
    }

    [Fact]
    public void AReadingCloseBehindTheLastIsNotPairedWithIt()
    {
        // A throttle reading 50 ms after the panel's would make a pair too short to measure: left out.
        var (gauge, cpu, time) = Rig(processors: 4);
        Assert.Null(gauge.ShareOfMachine());
        cpu.Use(TimeSpan.FromSeconds(2));
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(0.25, gauge.ShareOfMachine()!.Value, 6);

        cpu.Use(TimeSpan.FromMilliseconds(200));
        time.Advance(TimeSpan.FromMilliseconds(50));
        _ = gauge.Used;

        Assert.Equal(0.25, gauge.ShareOfMachine()!.Value, 6);
    }

    [Fact]
    public void TheShareIsNeverMoreThanTheWholeMachine()
    {
        var (gauge, cpu, time) = Rig(processors: 2);
        _ = gauge.ShareOfMachine();
        cpu.Use(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(1, gauge.ShareOfMachine()!.Value, 6);
        Assert.Equal(2, gauge.Processors);
    }

    [Fact]
    public void EveryLevelHasAWireName()
    {
        var names = Enum.GetValues<CaptureLevel>().Select(CaptureLevelNames.WireName).ToList();

        Assert.Equal(["full", "fewer_samples", "fewest_samples", "ocr_one_at_a_time", "ocr_clicks_only"], names);
    }

    private static (ProcessorShareGauge Gauge, FakeProcessorTime Cpu, ManualTime Time) Rig(int processors)
    {
        var cpu = new FakeProcessorTime(processors);
        var time = new ManualTime(At);
        return (new ProcessorShareGauge(cpu, time), cpu, time);
    }
}
