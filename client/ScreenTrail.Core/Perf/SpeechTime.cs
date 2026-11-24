namespace ScreenTrail.Core.Perf;

/// <summary>
/// How long speech recognition has been at work, counted while it works (ST-031).
///
/// whisper.cpp runs on one thread (<c>WhisperRecogniser.Threads</c>) and keeps it busy for the whole of a
/// segment, so the time between <see cref="Begin"/> and <see cref="End"/> is the processor time speech
/// took, to within what the thread spent waiting for a turn. The segment still being transcribed counts as
/// it goes, so a reading taken halfway through a five-second segment sees the half already spent rather
/// than nothing until it ends.
///
/// What it is for: the capture throttle can only make capture cheaper, never speech, so it subtracts this
/// before comparing the service with its budget (<see cref="CaptureWorkTime"/>).
/// </summary>
public sealed class SpeechTime(TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private TimeSpan _finished;
    private long? _startedAt;

    /// <summary>Speech time so far, the segment under way included.</summary>
    public TimeSpan Busy
    {
        get
        {
            lock (_gate)
            {
                return _startedAt is { } started ? _finished + _time.GetElapsedTime(started) : _finished;
            }
        }
    }

    /// <summary>A segment goes to the model. A second Begin without an End keeps the first start.</summary>
    public void Begin()
    {
        lock (_gate)
        {
            _startedAt ??= _time.GetTimestamp();
        }
    }

    /// <summary>The model is done with the segment, however it ended. An End with no Begin does nothing.</summary>
    public void End()
    {
        lock (_gate)
        {
            if (_startedAt is { } started)
            {
                _finished += _time.GetElapsedTime(started);
                _startedAt = null;
            }
        }
    }
}
