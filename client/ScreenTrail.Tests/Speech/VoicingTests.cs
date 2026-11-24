using ScreenTrail.Core.Speech;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Tests.Speech;

/// <summary>
/// A stretch loud enough for the gate goes to the model only if it has a voice in it (ST-027). On the laptop a
/// session nobody spoke in kept 12 and 19 transcript segments of typing and clicks, at about five seconds
/// of a processor each.
/// </summary>
public sealed class VoicingTests
{
    private const int Rate = NarrationRecorderTests.Rate;

    [Fact]
    public void AVoicedSoundIsSpeech()
    {
        // A vowel: a pitch of 140 Hz and its harmonics, rising and falling as a syllable does.
        var audio = Vowel(seconds: 1.0, pitch: 140);

        Assert.True(Voicing.SoundsVoiced(audio, Rate));
    }

    [Fact]
    public void TypingIsNotSpeech()
    {
        // Key strikes: a few milliseconds of broadband burst each, ten a second, nothing periodic.
        var random = new Random(3);
        var audio = new short[Rate * 2];
        for (var key = 0; key < 20; key++)
        {
            var at = (key * Rate / 10) + random.Next(0, 400);
            for (var i = 0; i < 160 && at + i < audio.Length; i++)
            {
                audio[at + i] = (short)(random.Next(-12000, 12001) * Math.Exp(-i / 40.0));
            }
        }

        Assert.False(Voicing.SoundsVoiced(audio, Rate));
    }

    [Fact]
    public void LoudNoiseIsNotSpeech()
    {
        var random = new Random(5);
        var audio = Enumerable.Range(0, Rate).Select(_ => (short)random.Next(-9000, 9001)).ToArray();

        Assert.False(Voicing.SoundsVoiced(audio, Rate));
    }

    [Fact]
    public void RumbleUnderTheLowestVoiceIsNotSpeech()
    {
        // A smooth low hum correlates with itself at every short lag; the high-pass and the peak rule keep
        // it from reading as a pitch.
        var audio = Enumerable.Range(0, Rate).Select(n => (short)(9000 * Math.Sin(2 * Math.PI * 30 * n / Rate))).ToArray();

        Assert.False(Voicing.SoundsVoiced(audio, Rate));
    }

    [Fact]
    public void SilenceAndTooLittleAudioAreNotSpeech()
    {
        Assert.False(Voicing.SoundsVoiced(new short[Rate], Rate));
        Assert.False(Voicing.SoundsVoiced(Vowel(seconds: 0.01, pitch: 140), Rate));
    }

    [Fact]
    public void OneVoicedBlipInABurstOfKeysIsNotAWord()
    {
        // Under the 150 ms of voice a stretch needs, however small the stretch.
        var audio = Vowel(seconds: 0.1, pitch: 140);

        Assert.False(Voicing.SoundsVoiced(audio, Rate));
    }

    [Fact]
    public async Task TypingThatPassesTheGateNeverReachesTheModel()
    {
        var kept = new List<TranscriptSegment>();
        var recogniser = new NarrationRecorderTests.FakeRecogniser("thank you for watching");
        var microphone = new NarrationRecorderTests.FakeMicrophone(
            NarrationRecorderTests.Silence(20), NarrationRecorderTests.Room(9000, 40), NarrationRecorderTests.Silence(40));
        var recorder = Recorder(microphone, recogniser, kept);

        await recorder.RunAsync(TestContext.Current.CancellationToken);

        Assert.True(recorder.Heard > 0, "the gate let the noise through, as it does on the laptop");
        Assert.Equal(0, recogniser.Asked);
        Assert.Empty(kept);
        Assert.Equal(recorder.Heard, recorder.NotVoiced);
    }

    [Fact]
    public async Task SpeechStillReachesTheModel()
    {
        var kept = new List<TranscriptSegment>();
        var recogniser = new NarrationRecorderTests.FakeRecogniser("restarted the print spooler");
        var microphone = new NarrationRecorderTests.FakeMicrophone(
            NarrationRecorderTests.Silence(20), NarrationRecorderTests.Speech(40), NarrationRecorderTests.Silence(40));
        var recorder = Recorder(microphone, recogniser, kept);

        await recorder.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(kept);
        Assert.Equal(0, recorder.NotVoiced);
    }

    private static NarrationRecorder Recorder(IMicrophone microphone, ISpeechRecogniser recogniser, List<TranscriptSegment> kept) =>
        new(
            microphone,
            recogniser,
            (_, segment, _) =>
            {
                kept.Add(segment);
                return Task.FromResult(true);
            },
            () => new NarrationSession("session-a", 0),
            hearing: () => true);

    /// <summary>A pitch and four harmonics under a syllable-shaped envelope, four syllables a second.</summary>
    private static short[] Vowel(double seconds, double pitch)
    {
        var audio = new short[(int)(seconds * Rate)];
        for (var n = 0; n < audio.Length; n++)
        {
            var t = n / (double)Rate;
            var envelope = 0.4 + (0.6 * Math.Abs(Math.Sin(Math.PI * 4 * t)));
            var sample = 0.0;
            for (var h = 1; h <= 5; h++)
            {
                sample += Math.Sin(2 * Math.PI * pitch * h * t) / h;
            }

            audio[n] = (short)(6000 * envelope * sample);
        }

        return audio;
    }
}
