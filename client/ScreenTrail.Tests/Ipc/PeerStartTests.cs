using ScreenTrail.Core.Ipc;

namespace ScreenTrail.Tests.Ipc;

/// <summary>
/// A process started after the pipe connection was made cannot be the process that made it (security
/// review B-01 (b)). The pipe records a process id when it connects, and an id is free to name a new
/// process once its own has exited; both verifiers ask this before they believe what the id names.
/// </summary>
public sealed class PeerStartTests
{
    private static readonly DateTime Connected = new(2026, 11, 28, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AProcessStartedAfterTheConnectionIsNotThePeer() =>
        Assert.False(PeerStart.CanBeThePeer(Connected.AddMilliseconds(1), Connected));

    [Fact]
    public void AProcessStartedLongAfterTheConnectionIsNotThePeer() =>
        Assert.False(PeerStart.CanBeThePeer(Connected.AddMinutes(5), Connected));

    [Fact]
    public void AProcessWhoseStartCannotBeReadIsNotThePeer() =>
        Assert.False(PeerStart.CanBeThePeer(null, Connected));

    [Fact]
    public void AProcessStartedBeforeTheConnectionCanBeThePeer()
    {
        Assert.True(PeerStart.CanBeThePeer(Connected.AddSeconds(-3), Connected));
        Assert.True(PeerStart.CanBeThePeer(Connected.AddDays(-2), Connected));
    }

    [Fact]
    public void TheSameInstantCountsAsBefore() =>
        Assert.True(PeerStart.CanBeThePeer(Connected, Connected));

    [Fact]
    public void LocalAndUniversalTimesAreComparedAsTheSameClock()
    {
        var startedLocal = Connected.AddMinutes(1).ToLocalTime();

        Assert.False(PeerStart.CanBeThePeer(startedLocal, Connected));
    }
}
