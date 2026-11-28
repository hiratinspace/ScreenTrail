using ScreenTrail.Api.Summarize;

namespace ScreenTrail.Api.Tests.Summarize;

/// <summary>The per-device share itself (E-15): settled to what a draft cost, given back when nothing was spent, and new each UTC day.</summary>
public sealed class DeviceDraftSharesTests
{
    [Fact]
    public void AClaimIsSettledToWhatTheDraftCostAndANewDayStartsAgain()
    {
        var clock = new HeldClock();
        var shares = new DeviceDraftShares(clock);
        var (tenant, device) = (Guid.NewGuid(), Guid.NewGuid());

        // A half-dollar share, ten-cent claims that cost one cent each: fifty drafts, not five.
        var drafted = 0;
        while (shares.TryClaim(tenant, device, 0.50m, 0.10m) is { } claim && drafted < 100)
        {
            claim.Settle(0.01m);
            claim.Settle(5m);
            drafted++;
        }

        var given = shares.TryClaim(tenant, Guid.NewGuid(), 0.50m, 0.10m);
        given!.Settle(0m);
        clock.Advance(TimeSpan.FromDays(1));

        Assert.Equal(50, drafted);
        Assert.NotNull(shares.TryClaim(tenant, device, 0.50m, 0.10m));
    }

    [Theory]
    [InlineData(0, 0.90, 0.90)]
    [InlineData(1, 0.90, 0.90)]
    [InlineData(2, 0.90, 0.45)]
    [InlineData(3, 0.90, 0.30)]
    [InlineData(40, 0.90, 0.30)]
    public void TheShareIsTheWholeCapForOneDeviceAndAThirdAtLeast(int devices, double cap, double share) =>
        Assert.Equal((decimal)share, DeviceDraftShares.ShareOf((decimal)cap, devices));
}
