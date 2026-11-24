namespace ScreenTrail.Core.Speech;

/// <summary>
/// Whether a stretch the gate let through has a voice in it, before the model is paid to read it (ST-027).
///
/// The gate (<see cref="VoiceActivity"/>, <see cref="SpeechGate"/>) goes by loudness against the room. A
/// keyboard and a mouse beside the microphone are loud against the room, and a run of keys bridged by the
/// gate's hangover is a "segment" as long as a sentence. On the laptop on 2026-11-22 a session in which
/// nobody spoke kept 12 transcript segments and another 19: each cost whisper about five seconds of a
/// processor, and the words it wrote for them were not said.
///
/// A voice is voiced: the vocal folds make it periodic at the pitch of the voice, 70 to 400 Hz, for much of
/// every word. A key or a click is a burst with no pitch. So each 32 ms frame with something in it is asked
/// for its strongest normalised autocorrelation at a lag inside that pitch range, and a stretch counts as
/// voiced when enough of its frames are clearly periodic. Whispering has no pitch either and would be
/// refused; a technician narrating to a ticket does not whisper.
/// </summary>
public static class Voicing
{
    /// <summary>A frame this periodic at a pitch lag is voiced. Speech reads 0.6 to 0.95; keys and clicks well under.</summary>
    public const double PeriodicAt = 0.6;

    /// <summary>The share of a stretch's audible frames that must be voiced for it to go to the model.</summary>
    public const double VoicedShareAtLeast = 0.15;

    /// <summary>And at least this much voiced audio in all, so one stray periodic frame in a burst of keys is not a word.</summary>
    public static readonly TimeSpan VoicedAtLeast = TimeSpan.FromMilliseconds(150);

    private const double LowestPitchHz = 70;
    private const double HighestPitchHz = 400;
    private static readonly TimeSpan FrameLength = TimeSpan.FromMilliseconds(32);

    /// <summary>A first-order high-pass at about 100 Hz at 16 kHz (RC / (RC + 1/16000)).</summary>
    private const double HighPass = 0.962;

    /// <summary>Whether this audio sounds like someone speaking.</summary>
    public static bool SoundsVoiced(ReadOnlySpan<short> audio, int sampleRate)
    {
        var (voiced, audible, frame) = Count(audio, sampleRate);
        return audible > 0
            && voiced * frame >= VoicedAtLeast.TotalSeconds * sampleRate
            && voiced >= VoicedShareAtLeast * audible;
    }

    /// <summary>Voiced frames, audible frames, and the frame length in samples. For tests and measurement.</summary>
    public static (int Voiced, int Audible, int FrameSamples) Count(ReadOnlySpan<short> audio, int sampleRate)
    {
        var frame = Math.Max(1, (int)(FrameLength.TotalSeconds * sampleRate));
        var shortest = Math.Max(1, (int)(sampleRate / HighestPitchHz));
        var longest = Math.Min((frame / 2) - 1, (int)(sampleRate / LowestPitchHz));
        if (audio.Length < frame || longest <= shortest)
        {
            return (0, 0, frame);
        }

        // Loud enough to judge: within 30 dB of the stretch's loudest frame. Quieter frames are the pauses
        // between words, and judging them would dilute the share for a slow speaker.
        var frames = audio.Length / frame;
        var energies = new double[frames];
        var loudest = 0.0;
        for (var f = 0; f < frames; f++)
        {
            energies[f] = Energy(audio.Slice(f * frame, frame));
            loudest = Math.Max(loudest, energies[f]);
        }

        if (loudest <= 0)
        {
            return (0, 0, frame);
        }

        var audibleFrom = loudest * 1e-3;
        var buffer = new double[frame];
        int voiced = 0, audible = 0;
        for (var f = 0; f < frames; f++)
        {
            if (energies[f] < audibleFrom)
            {
                continue;
            }

            audible++;

            // High-passed first. Rumble and hum under the voice's lowest pitch are smooth, and a smooth
            // signal correlates with itself at every short lag: unfiltered, a room's low end read as
            // voiced in most frames of a recording nobody spoke in.
            var samples = audio.Slice(f * frame, frame);
            double previousIn = samples[0], previousOut = 0;
            for (var i = 0; i < frame; i++)
            {
                previousOut = HighPass * (previousOut + samples[i] - previousIn);
                previousIn = samples[i];
                buffer[i] = previousOut;
            }

            if (Periodicity(buffer, shortest, longest) >= PeriodicAt)
            {
                voiced++;
            }
        }

        return (voiced, audible, frame);
    }

    private static double Energy(ReadOnlySpan<short> samples)
    {
        var sum = 0.0;
        foreach (var s in samples)
        {
            sum += (double)s * s;
        }

        return sum / samples.Length;
    }

    /// <summary>The strongest normalised autocorrelation of the frame at a lag in the pitch range, 0 to 1.</summary>
    private static double Periodicity(double[] x, int shortest, int longest)
    {
        // A peak: higher than the lags either side. What is left of a smooth signal falls away from the
        // shortest lag with no peak, and is not a pitch.
        var correlation = new double[longest + 2];
        for (var lag = shortest - 1; lag <= longest + 1; lag++)
        {
            double cross = 0, head = 0, tail = 0;
            for (var i = 0; i + lag < x.Length; i++)
            {
                cross += x[i] * x[i + lag];
                head += x[i] * x[i];
                tail += x[i + lag] * x[i + lag];
            }

            correlation[lag] = head > 0 && tail > 0 ? cross / Math.Sqrt(head * tail) : 0;
        }

        var best = 0.0;
        for (var lag = shortest; lag <= longest; lag++)
        {
            if (correlation[lag] > correlation[lag - 1] && correlation[lag] >= correlation[lag + 1])
            {
                best = Math.Max(best, correlation[lag]);
            }
        }

        return best;
    }
}
