namespace ScreenTrail.Api.Summarize;

/// <summary>Who is drafting, for <see cref="DeviceDraftShares"/>: the device and how many active devices its tenant has.</summary>
public sealed record DeviceShare(Guid DeviceId, int ActiveDevices);

/// <summary>
/// What each device has spent on drafting today, so that one device cannot spend its tenant's whole daily
/// budget (E-15). A device may claim at most its share of the cap: the whole of it in a tenant of one
/// device, half with two, a third with three or more. The tenant's own cap still applies on top, through
/// <see cref="CostLedger"/>.
///
/// A claim is the session's most (<see cref="SummarizationOptions.MaxSessionCostUsd"/>) while the draft
/// is in flight, and is settled to what the draft cost, as the ledger's is; a device may start a draft
/// while what it has claimed is under its share, the ledger's rule too.
///
/// In memory, per API instance, and the day is UTC like the ledger's. A restart forgets each device's
/// spending, but not the tenant's, which the ledger sums from rows: a crash loop gives one device a
/// fresh share, never a fresh budget. Keeping the share across a restart needs the device on the ledger's
/// row, a migration (DEC-23).
/// </summary>
public sealed class DeviceDraftShares(TimeProvider time)
{
    /// <summary>Into how many shares, at most, the day's budget is split.</summary>
    public const int MostShares = 3;

    private readonly Dictionary<(Guid Tenant, Guid Device), decimal> _spent = [];
    private readonly Lock _lock = new();
    private DateOnly _day = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    /// <summary>One device's share of <paramref name="cap"/> in a tenant with <paramref name="activeDevices"/> active devices.</summary>
    public static decimal ShareOf(decimal cap, int activeDevices) => cap / Math.Clamp(activeDevices, 1, MostShares);

    /// <summary>
    /// Claims <paramref name="estimate"/> for the device, or null when what it has claimed today already
    /// reaches <paramref name="share"/>. The claim is settled, or given back, through what is returned.
    /// </summary>
    public Claim? TryClaim(Guid tenantId, Guid deviceId, decimal share, decimal estimate)
    {
        lock (_lock)
        {
            var day = Today();
            var spent = _spent.GetValueOrDefault((tenantId, deviceId));
            if (spent >= share)
            {
                return null;
            }

            _spent[(tenantId, deviceId)] = spent + estimate;
            return new Claim(this, tenantId, deviceId, estimate, day);
        }
    }

    private DateOnly Today()
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        if (today != _day)
        {
            // A new day: yesterday's spending counts against nothing, so it is all dropped at once.
            _spent.Clear();
            _day = today;
        }

        return today;
    }

    private void Settle(Claim claim, decimal cost)
    {
        lock (_lock)
        {
            if (Today() != claim.Day)
            {
                return;
            }

            var key = (claim.TenantId, claim.DeviceId);
            _spent[key] = Math.Max(0m, _spent.GetValueOrDefault(key) - claim.Estimate + cost);
        }
    }

    /// <summary>One draft's claim on its device's share. Settled once; a second settle changes nothing.</summary>
    public sealed class Claim
    {
        private readonly DeviceDraftShares _shares;
        private int _settled;

        internal Claim(DeviceDraftShares shares, Guid tenantId, Guid deviceId, decimal estimate, DateOnly day)
        {
            _shares = shares;
            TenantId = tenantId;
            DeviceId = deviceId;
            Estimate = estimate;
            Day = day;
        }

        internal Guid TenantId { get; }

        internal Guid DeviceId { get; }

        internal decimal Estimate { get; }

        internal DateOnly Day { get; }

        /// <summary>What the draft cost; zero gives the claim back.</summary>
        public void Settle(decimal cost)
        {
            if (Interlocked.Exchange(ref _settled, 1) == 0)
            {
                _shares.Settle(this, cost);
            }
        }
    }
}
