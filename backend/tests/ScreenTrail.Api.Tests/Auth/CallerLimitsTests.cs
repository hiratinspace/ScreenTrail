using ScreenTrail.Api.Auth;

namespace ScreenTrail.Api.Tests.Auth;

/// <summary>
/// The per-device and per-tenant counts behind the authenticated routes (D-01, D-06, E-05): the check and
/// the take are one step, a request refused by one budget spends none of the others, and idle counts go.
/// </summary>
public sealed class CallerLimitsTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task RequestsArrivingTogetherGetExactlyTheLimit()
    {
        var limits = new CallerLimits(new Clock());
        var (tenant, device) = (Guid.NewGuid(), Guid.NewGuid());
        using var start = new ManualResetEventSlim();

        var tries = Enumerable.Range(0, 400).Select(_ => Task.Run(() =>
        {
            start.Wait(TestContext.Current.CancellationToken);
            return limits.TryTake(tenant, device, [CallerLimits.Requests]);
        })).ToList();
        start.Set();
        var answers = await Task.WhenAll(tries);

        Assert.Equal(CallerLimits.Requests.Permits, answers.Count(a => a is null));
    }

    [Fact]
    public void TheThreeHundredAndFirstRequestInAMinuteWaitsAndAnotherDeviceDoesNot()
    {
        var clock = new Clock();
        var limits = new CallerLimits(clock);
        var (tenant, device, other) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        for (var i = 0; i < 300; i++)
        {
            Assert.Null(limits.TryTake(tenant, device, [CallerLimits.Requests]));
        }

        var refused = limits.TryTake(tenant, device, [CallerLimits.Requests]);
        var otherDevice = limits.TryTake(tenant, other, [CallerLimits.Requests]);
        clock.Now += TimeSpan.FromMinutes(1);

        Assert.NotNull(refused);
        Assert.Equal(TimeSpan.FromMinutes(1), refused.Value.Wait);
        Assert.Null(otherDevice);
        Assert.Null(limits.TryTake(tenant, device, [CallerLimits.Requests]));
    }

    [Fact]
    public void ARequestRefusedByOneBudgetSpendsNoneOfTheOthers()
    {
        var limits = new CallerLimits(new Clock());
        var (tenant, device) = (Guid.NewGuid(), Guid.NewGuid());
        for (var i = 0; i < CallerLimits.Checks.Permits; i++)
        {
            Assert.Null(limits.TryTake(tenant, device, [CallerLimits.Checks]));
        }

        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(CallerLimits.Checks, limits.TryTake(tenant, device, [CallerLimits.Requests, CallerLimits.Checks])?.Budget);
        }

        // Nothing of the 50 refused was counted against the device's general budget.
        for (var i = 0; i < CallerLimits.Requests.Permits; i++)
        {
            Assert.Null(limits.TryTake(tenant, device, [CallerLimits.Requests]));
        }
    }

    [Fact]
    public void TheTenantsBudgetIsSharedByItsDevices()
    {
        var limits = new CallerLimits(new Clock());
        var tenant = Guid.NewGuid();
        var devices = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();
        var budgets = new[] { CallerLimits.ProviderCalls, CallerLimits.TenantProviderCalls };

        var taken = devices.Sum(device => Enumerable.Range(0, 120).Count(_ => limits.TryTake(tenant, device, budgets) is null));

        Assert.Equal(CallerLimits.TenantProviderCalls.Permits, taken);
        Assert.Null(limits.TryTake(Guid.NewGuid(), devices[5], budgets));
    }

    [Fact]
    public void IdleCountsAreDroppedAndAPlaceInFlightIsGivenBackOnce()
    {
        var clock = new Clock();
        var limits = new CallerLimits(clock);
        var tenant = Guid.NewGuid();
        _ = limits.TryTake(tenant, Guid.NewGuid(), [CallerLimits.Requests, CallerLimits.TenantProviderCalls]);
        Assert.Equal(2, limits.Held);

        clock.Now += TimeSpan.FromMinutes(2);
        _ = limits.TryTake(tenant, Guid.NewGuid(), [CallerLimits.Checks]);

        Assert.Equal(1, limits.Held);

        var places = Enumerable.Range(0, CallerLimits.ProviderCallsInFlight).Select(_ => limits.TryEnterProviderCall(tenant)).ToList();
        Assert.All(places, Assert.NotNull);
        Assert.Null(limits.TryEnterProviderCall(tenant));
        Assert.NotNull(limits.TryEnterProviderCall(Guid.NewGuid()));
        places[0]!.Dispose();
        places[0]!.Dispose();
        using var again = limits.TryEnterProviderCall(tenant);
        Assert.NotNull(again);
        Assert.Null(limits.TryEnterProviderCall(tenant));
    }
}
