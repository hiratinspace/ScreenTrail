using ScreenTrail.Core.Shell;

namespace ScreenTrail.Tests.Shell;

/// <summary>One duration format wherever a technician reads one (review finding E-08).</summary>
public sealed class DurationsTests
{
    [Theory]
    [InlineData(0, "0 s")]
    [InlineData(8_000, "8 s")]
    [InlineData(59_400, "59 s")]
    [InlineData(59_600, "1 min")]
    [InlineData(21 * 60_000, "21 min")]
    [InlineData(59 * 60_000 + 40_000, "1 h")]
    [InlineData(62 * 60_000, "1 h 2 min")]
    [InlineData(120 * 60_000, "2 h")]
    [InlineData(-5_000, "0 s")]
    public void ADurationIsSecondsMinutesOrHoursAndMinutes(long ms, string words) => Assert.Equal(words, Durations.InWords(ms));

    [Theory]
    [InlineData(15, "15 min")]
    [InlineData(90, "1 h 30 min")]
    [InlineData(60, "1 h")]
    [InlineData(0, "0 min")]
    public void ATimeEntryIsInMinutesNotAClock(int minutes, string words) => Assert.Equal(words, Durations.OfMinutes(minutes));
}
