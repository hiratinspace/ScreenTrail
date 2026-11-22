namespace ScreenTrail.Core.Perf;

/// <summary>
/// The service's processor share as "What's being captured right now?" shows it (ST-031): the same
/// reading the throttle acts on, without a timer of its own.
///
/// It stands between the process's own figure and whoever reads it. While a session is under way the
/// throttle reads once a second (<see cref="CaptureThrottle"/>), and each of its readings is kept here, so
/// the panel shows the share the throttle last measured rather than a second, different figure. Between
/// sessions nothing reads it, and the idle service must not start waking to keep a number fresh nobody is
/// looking at, so the panel's own request is the reading: the first one after a while says the share is
/// still being measured, and each one after it, a couple of seconds apart while the panel is open, gives
/// the share since the one before.
///
/// <b>Numbers only</b> (INV-10): processor time and a clock, nothing about any window.
/// </summary>
public sealed class ProcessorShareGauge : IProcessorTime
{
    /// <summary>A reading closer than this to the last one is not taken: too few scheduler ticks to say much.</summary>
    public static readonly TimeSpan Shortest = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Two readings further apart than this are not a figure for "right now": an average over the last
    /// minute says little about this second. The panel says it is measuring instead.
    /// </summary>
    public static readonly TimeSpan Longest = TimeSpan.FromSeconds(30);

    private readonly IProcessorTime _inner;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private (long At, TimeSpan Used)? _before;
    private (long At, TimeSpan Used)? _last;

    public ProcessorShareGauge(IProcessorTime inner, TimeProvider? time = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The process's processor time, as the inner figure gives it. Every read is kept as a reading.</summary>
    public TimeSpan Used
    {
        get
        {
            var used = _inner.Used;
            var at = _time.GetTimestamp();
            lock (_gate)
            {
                Keep(at, used);
            }

            return used;
        }
    }

    public int Processors => _inner.Processors;

    /// <summary>
    /// The share of the whole machine (0 to 1) this process used between its last two readings, taking a
    /// new one first unless the last is under <see cref="Shortest"/> old. Null when there is no pair close
    /// enough together to be a figure for now: the first request after a quiet spell, which the panel
    /// shows as still measuring and fills in on its next refresh.
    /// </summary>
    public double? ShareOfMachine()
    {
        var now = _time.GetTimestamp();
        lock (_gate)
        {
            if (_last is not { } last || _time.GetElapsedTime(last.At, now) >= Shortest)
            {
                Keep(now, _inner.Used);
            }

            if (_before is not { } before || _last is not { } latest)
            {
                return null;
            }

            var wall = _time.GetElapsedTime(before.At, latest.At);
            if (wall <= TimeSpan.Zero || wall > Longest)
            {
                return null;
            }

            var processors = Math.Max(1, _inner.Processors);
            return Math.Clamp((latest.Used - before.Used) / (wall * processors), 0, 1);
        }
    }

    /// <summary>
    /// Keeps a reading, under the gate. One within half of <see cref="Shortest"/> of the last is left out,
    /// so the panel's reading and the throttle's landing close together never make a pair too short to
    /// measure anything.
    /// </summary>
    private void Keep(long at, TimeSpan used)
    {
        if (_last is { } last && _time.GetElapsedTime(last.At, at) < Shortest / 2)
        {
            return;
        }

        _before = _last;
        _last = (at, used);
    }
}
