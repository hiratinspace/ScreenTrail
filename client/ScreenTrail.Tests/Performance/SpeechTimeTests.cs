using ScreenTrail.Core.Perf;

namespace ScreenTrail.Tests.Performance;

public sealed class SpeechTimeTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ASegmentUnderWayCountsAsItGoes()
    {
        var time = new ManualTime(Start);
        var speech = new SpeechTime(time);

        speech.Begin();
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(2), speech.Busy);

        time.Advance(TimeSpan.FromSeconds(3));
        speech.End();
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(5), speech.Busy);
    }

    [Fact]
    public void SegmentsAddUpAndTheGapsBetweenThemDoNot()
    {
        var time = new ManualTime(Start);
        var speech = new SpeechTime(time);

        speech.Begin();
        time.Advance(TimeSpan.FromSeconds(4));
        speech.End();
        time.Advance(TimeSpan.FromSeconds(7));
        speech.Begin();
        time.Advance(TimeSpan.FromSeconds(1));
        speech.End();

        Assert.Equal(TimeSpan.FromSeconds(5), speech.Busy);
    }

    [Fact]
    public void AnEndWithoutABeginAndASecondBeginChangeNothing()
    {
        var time = new ManualTime(Start);
        var speech = new SpeechTime(time);

        speech.End();
        speech.Begin();
        time.Advance(TimeSpan.FromSeconds(2));
        speech.Begin();
        time.Advance(TimeSpan.FromSeconds(1));
        speech.End();
        speech.End();

        Assert.Equal(TimeSpan.FromSeconds(3), speech.Busy);
    }
}

public sealed class CaptureWorkTimeTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SpeechIsTakenOutOfTheServicesTime()
    {
        var time = new ManualTime(Start);
        var total = new FakeProcessorTime(processors: 8);
        var speech = new SpeechTime(time);
        var capture = new CaptureWorkTime(total, speech);

        speech.Begin();
        time.Advance(TimeSpan.FromSeconds(5));
        total.Use(TimeSpan.FromSeconds(5.4));

        Assert.Equal(TimeSpan.FromSeconds(0.4), capture.Used);
        Assert.Equal(8, capture.Processors);
    }

    [Fact]
    public void ItNeverGoesBackwardsWhenSpeechWaitedForTheProcessor()
    {
        var time = new ManualTime(Start);
        var total = new FakeProcessorTime(processors: 8);
        var speech = new SpeechTime(time);
        var capture = new CaptureWorkTime(total, speech);

        total.Use(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(1), capture.Used);

        // A busy machine: the speech thread spent two seconds of the clock but got one of processor.
        speech.Begin();
        time.Advance(TimeSpan.FromSeconds(2));
        total.Use(TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.FromSeconds(1), capture.Used);
    }

    [Fact]
    public void ANarratedSessionNoLongerWalksCaptureDown()
    {
        // The laptop on 2026-11-22: eight processors, whisper's one thread busy the whole time (12.5% of the
        // machine, over the 7.5% budget), and capture's own work about 0.3 of a core.
        var time = new ManualTime(Start);
        var total = new FakeProcessorTime(processors: 8);
        var speech = new SpeechTime(time);
        var throttle = new CaptureThrottle(new CaptureWorkTime(total, speech), time);
        var whole = new CaptureThrottle(total, time);
        throttle.Observe();
        whole.Observe();

        speech.Begin();
        for (var second = 0; second < 120; second++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            total.Use(TimeSpan.FromSeconds(1.3));
            throttle.Observe();
            whole.Observe();
        }

        Assert.Equal(CaptureLevel.Full, throttle.Level);
        Assert.Equal(CaptureLevel.OcrClicksOnly, whole.Level);
    }

    [Fact]
    public void CaptureThatIsItselfOverBudgetIsStillThrottled()
    {
        var time = new ManualTime(Start);
        var total = new FakeProcessorTime(processors: 8);
        var speech = new SpeechTime(time);
        var throttle = new CaptureThrottle(new CaptureWorkTime(total, speech), time);
        throttle.Observe();

        speech.Begin();
        for (var second = 0; second < 15; second++)
        {
            // Speech's one core plus a whole core of capture work: 100% of one core is over 60%.
            time.Advance(TimeSpan.FromSeconds(1));
            total.Use(TimeSpan.FromSeconds(2));
            throttle.Observe();
        }

        Assert.Equal(CaptureLevel.FewerSamples, throttle.Level);
    }
}
