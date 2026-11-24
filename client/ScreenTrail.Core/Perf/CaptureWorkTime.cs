namespace ScreenTrail.Core.Perf;

/// <summary>
/// The service's processor time with speech recognition's taken out: what the capture throttle can do
/// something about (ST-031).
///
/// The throttle's steps all make capture cheaper: the scene sampler looks less often, OCR reads one frame
/// at a time, then only click and marked-moment frames. None of them makes speech cheaper, and speech is
/// never throttled. Measured against the whole service, one narrated session kept whisper's single thread
/// busy (12.5% of the laptop's eight processors, against a budget of 7.5%), so the throttle walked capture
/// down to its deepest level within a minute, and stayed there whether the technician talked or typed
/// (laptop, 2026-11-22: "Capture throttle ... -> OcrClicksOnly at 12.6% of the machine"). Screenshots were
/// given up for a cost they were not causing. Speech has its own ceiling: one thread, and the laptop's
/// narrated recording measurement holds the whole service under 120% of one core.
///
/// Never goes backwards: speech time is counted by the clock, which can run ahead of the processor time
/// the process was actually given when the machine is busy, and a reading must not come out negative.
/// </summary>
public sealed class CaptureWorkTime(IProcessorTime total, SpeechTime speech) : IProcessorTime
{
    private readonly IProcessorTime _total = total ?? throw new ArgumentNullException(nameof(total));
    private readonly SpeechTime _speech = speech ?? throw new ArgumentNullException(nameof(speech));
    private readonly Lock _gate = new();
    private TimeSpan _last;

    public TimeSpan Used
    {
        get
        {
            var used = _total.Used - _speech.Busy;
            lock (_gate)
            {
                if (used > _last)
                {
                    _last = used;
                }

                return _last;
            }
        }
    }

    public int Processors => _total.Processors;
}
