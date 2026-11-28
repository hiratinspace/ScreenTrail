using System.Globalization;
using ScreenTrail.Api.Endpoints;
using ScreenTrail.Api.Summarize;

namespace ScreenTrail.Api.Auth;

/// <summary>Whom a budget counts: the device (or, on an owner's route, the owner), or the whole tenant.</summary>
public enum LimitScope
{
    Caller,
    Tenant,
}

/// <summary>
/// One count: at most <paramref name="Permits"/> requests in any <paramref name="Window"/>, per caller or
/// per tenant.
/// </summary>
/// <param name="Name">The budget's own name, which keeps its counts apart from every other budget's.</param>
/// <param name="Refusal">The sentence a caller past it is shown, given how long to wait.</param>
public sealed record RequestBudget(string Name, LimitScope Scope, int Permits, TimeSpan Window, Func<TimeSpan, string> Refusal);

/// <summary>
/// How often one device, one owner and one tenant may call the authenticated routes, in the style of
/// <see cref="Vault.MappingChangeLimit"/> (D-01, D-06, E-02, E-05). Counted after authorization, so the
/// caller is known and no one can spend another's budget, and before the endpoint, so a refused request
/// has its body read into nothing and reaches no provider. A request over a budget is answered 429 with a
/// Retry-After and a sentence the client shows; nothing else changes, so it can be sent again later.
///
/// Every number is set well above what the Windows client sends, and why is beside each one. Checking
/// and taking every budget a request spends is one step under one lock, so requests arriving together
/// cannot all pass a count none of them has added to yet, and a request refused by one budget spends
/// none of the others. Counts idle past their window are dropped by a sweep at most once a minute.
///
/// The routes that call the tenant's PSA or documentation platform are also counted in flight per tenant
/// (<see cref="ProviderCallsInFlight"/>): a site that answers slowly holds each of those calls, and their
/// buffers, for up to the request's deadline.
///
/// In process, by design, as the mapping limit is: the deployment runs one API instance (ST-007). A
/// second instance would give each caller this much per instance, and a restart starts the windows again.
/// </summary>
public sealed class CallerLimits(TimeProvider time)
{
    /// <summary>
    /// Every request a device makes under <c>/v1</c>: 300 a minute. The client's busiest minute is the
    /// ticket picker, which asks once a keystroke (<see cref="ProviderCalls"/> bounds that at 120); its
    /// other calls are an hourly policy fetch, a metric and a draft per session, and a few publishes.
    /// </summary>
    public static readonly RequestBudget Requests = new("requests", LimitScope.Caller, 300, TimeSpan.FromMinutes(1), wait =>
        $"This PC has sent ScreenTrail more requests in the last minute than it may. Wait {Seconds(wait)}, then try again.");

    /// <summary>
    /// Requests from one device that make the backend call the tenant's PSA or documentation platform:
    /// 120 a minute. The ticket picker searches once a keystroke from the third character, so this is
    /// four 30-character searches typed in one minute, besides the company list and a Test connection.
    /// </summary>
    public static readonly RequestBudget ProviderCalls = new("provider", LimitScope.Caller, 120, TimeSpan.FromMinutes(1), wait =>
        $"This PC has asked your PSA more often in the last minute than ScreenTrail allows. Wait {Seconds(wait)}, then try again.");

    /// <summary>The same for the whole tenant, the owner's Test connection included: 600 a minute, five devices at their full rate.</summary>
    public static readonly RequestBudget TenantProviderCalls = new("provider-tenant", LimitScope.Tenant, 600, TimeSpan.FromMinutes(1), wait =>
        $"Your team has asked your PSA more often in the last minute than ScreenTrail allows. Wait {Seconds(wait)}, then try again.");

    /// <summary>Test connection, from a device or the owner: 6 a minute each, a press every ten seconds.</summary>
    public static readonly RequestBudget Checks = new("check", LimitScope.Caller, 6, TimeSpan.FromMinutes(1), wait =>
        $"The connection was tested 6 times in the last minute. Wait {Seconds(wait)}, then test it again.");

    /// <summary>
    /// Publishes from one device: 30 in ten minutes, as many as the mapping limit allows changes. A
    /// technician publishes a session once, and Retry sends only the destinations that failed; thirty is
    /// a publish every twenty seconds for ten minutes.
    /// </summary>
    public static readonly RequestBudget Publishes = new("publish", LimitScope.Caller, 30, TimeSpan.FromMinutes(10), wait =>
        $"This PC has published 30 times in the last ten minutes, which is as often as ScreenTrail allows. Wait {Seconds(wait)}, then publish again.");

    /// <summary>The same for the whole tenant: 300 in ten minutes, ten technicians at their full rate.</summary>
    public static readonly RequestBudget TenantPublishes = new("publish-tenant", LimitScope.Tenant, 300, TimeSpan.FromMinutes(10), wait =>
        $"Your team has published 300 times in the last ten minutes, which is as often as ScreenTrail allows. Wait {Seconds(wait)}, then publish again.");

    /// <summary>
    /// The requests allowed in flight at once for one tenant on the routes that call its PSA or
    /// documentation platform. Sixteen: the picker does not cancel a search when the next keystroke sends
    /// another, so two technicians typing at once against a PSA that takes a second hold about ten.
    /// </summary>
    public const int ProviderCallsInFlight = 16;

    /// <summary>The seconds named in Retry-After when a tenant has <see cref="ProviderCallsInFlight"/> calls in flight. A search usually answers within one.</summary>
    public const int InFlightRetryAfterSeconds = 2;

    /// <summary>How often counts idle past their window are dropped.</summary>
    public static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(1);

    private readonly Dictionary<(string Budget, Guid Key), Count> _counts = [];
    private readonly Dictionary<Guid, int> _inFlight = [];
    private readonly Lock _lock = new();
    private DateTimeOffset _lastSweep = time.GetUtcNow();

    /// <summary>How many counts are held now: for the tests that check idle ones are dropped.</summary>
    public int Held
    {
        get
        {
            lock (_lock)
            {
                return _counts.Count;
            }
        }
    }

    /// <summary>
    /// Takes one request from each budget, or none of them and says which budget refused and how long
    /// until it has room.
    /// </summary>
    public (RequestBudget Budget, TimeSpan Wait)? TryTake(Guid tenantId, Guid callerId, IEnumerable<RequestBudget> budgets)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        var now = time.GetUtcNow();
        lock (_lock)
        {
            SweepIfDue(now);
            var counts = budgets.Distinct().Select(budget =>
            {
                var key = (budget.Name, budget.Scope == LimitScope.Tenant ? tenantId : callerId);
                if (!_counts.TryGetValue(key, out var count))
                {
                    count = new Count(budget.Window);
                    _counts[key] = count;
                }

                while (count.Taken.Count > 0 && count.Taken.Peek() <= now - budget.Window)
                {
                    _ = count.Taken.Dequeue();
                }

                count.LastSeen = now;
                return (Budget: budget, Count: count);
            }).ToList();

            foreach (var (budget, count) in counts)
            {
                if (count.Taken.Count >= budget.Permits)
                {
                    var wait = count.Taken.Peek() + budget.Window - now;
                    return (budget, wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait);
                }
            }

            foreach (var (_, count) in counts)
            {
                count.Taken.Enqueue(now);
            }

            return null;
        }
    }

    /// <summary>A place for one provider call of the tenant's, or null when it has <see cref="ProviderCallsInFlight"/> already.</summary>
    public IDisposable? TryEnterProviderCall(Guid tenantId)
    {
        lock (_lock)
        {
            if (_inFlight.GetValueOrDefault(tenantId) >= ProviderCallsInFlight)
            {
                return null;
            }

            _inFlight[tenantId] = _inFlight.GetValueOrDefault(tenantId) + 1;
        }

        return new Lease(this, tenantId);
    }

    /// <summary>Marks a route whose requests make the backend call the tenant's PSA or documentation platform, for <see cref="ProviderCallsInFlight"/>.</summary>
    public static readonly object CallsProvider = new ProviderRoute();

    /// <summary>What a device route that calls the tenant's PSA or documentation platform spends: its own count and the tenant's.</summary>
    public static readonly Spends DeviceProviderRoute = new(ProviderCalls, TenantProviderCalls);

    /// <summary>Endpoint metadata naming the budgets a route's requests spend. A group's and a route's add up.</summary>
    public sealed class Spends(params RequestBudget[] budgets)
    {
        public IReadOnlyList<RequestBudget> Budgets { get; } = budgets;
    }

    /// <summary>
    /// Applies the budgets of every endpoint that names some. After authorization, so the caller is known,
    /// and before the endpoint and the concurrency gates, so the body has not been read.
    /// </summary>
    public static IApplicationBuilder Use(IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var limits = app.ApplicationServices.GetRequiredService<CallerLimits>();
        return app.Use(async (context, next) =>
        {
            var metadata = context.GetEndpoint()?.Metadata;
            var budgets = metadata?.GetOrderedMetadata<Spends>().SelectMany(s => s.Budgets).ToList() ?? [];
            var callsProvider = metadata?.GetMetadata<ProviderRoute>() is not null;
            if (budgets.Count == 0 && !callsProvider)
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            // Already proved by CallerPolicy or OwnerPolicy, so this reads what they stashed. A route that
            // reached here without a caller is refused the way the policy would refuse it.
            (Guid Tenant, Guid Caller)? who = CallerPolicy.Checked(context) is { } device
                ? (device.TenantId, device.DeviceId)
                : OwnerPolicy.Checked(context) is { } owner ? (owner.TenantId, owner.OwnerId) : null;
            if (who is not { } caller)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            using var lease = callsProvider ? limits.TryEnterProviderCall(caller.Tenant) : null;
            if (callsProvider && lease is null)
            {
                await RefuseAsync(context, InFlightRetryAfterSeconds, "busy", "ScreenTrail is already waiting on your PSA for several of your team's requests. Try again in a moment.").ConfigureAwait(false);
                return;
            }

            if (limits.TryTake(caller.Tenant, caller.Caller, budgets) is { } refused)
            {
                await RefuseAsync(context, (long)Math.Ceiling(refused.Wait.TotalSeconds), "too_many_requests", refused.Budget.Refusal(refused.Wait)).ConfigureAwait(false);
                return;
            }

            await next(context).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// The 429: Retry-After and the sentence, in the shape the route's client reads (drafting's own for
    /// the drafting route, which its outbox queues; <c>{error, message}</c> for every other). The body is
    /// read into nothing first, so a slow upload gets this answer rather than a dropped connection.
    /// </summary>
    private static async Task RefuseAsync(HttpContext context, long retryAfterSeconds, string error, string message)
    {
        if (!await UnreadBody.DiscardAsync(context).ConfigureAwait(false))
        {
            return;
        }

        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        var drafting = context.GetEndpoint()?.Metadata.Contains(DraftingConcurrency.Gated) == true;
        var answer = drafting
            ? Results.Json(new SummarizeResponse("busy", message), statusCode: StatusCodes.Status429TooManyRequests)
            : Results.Json(new { error, message }, statusCode: StatusCodes.Status429TooManyRequests);
        await answer.ExecuteAsync(context).ConfigureAwait(false);
    }

    /// <summary>"12 seconds", "1 second", or whole minutes past two.</summary>
    internal static string Seconds(TimeSpan wait)
    {
        if (wait >= TimeSpan.FromMinutes(2))
        {
            var minutes = (int)Math.Ceiling(wait.TotalMinutes);
            return string.Create(CultureInfo.InvariantCulture, $"{minutes} minutes");
        }

        var seconds = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
        return string.Create(CultureInfo.InvariantCulture, $"{seconds} second{(seconds == 1 ? string.Empty : "s")}");
    }

    /// <summary>Drops counts idle past their window, at most once every <see cref="SweepEvery"/>. Under the lock.</summary>
    private void SweepIfDue(DateTimeOffset now)
    {
        if (now - _lastSweep < SweepEvery)
        {
            return;
        }

        _lastSweep = now;
        foreach (var (key, count) in _counts.Where(c => c.Value.LastSeen <= now - c.Value.Window).ToList())
        {
            _ = _counts.Remove(key);
        }
    }

    private void Exit(Guid tenantId)
    {
        lock (_lock)
        {
            var left = _inFlight.GetValueOrDefault(tenantId) - 1;
            if (left <= 0)
            {
                _ = _inFlight.Remove(tenantId);
            }
            else
            {
                _inFlight[tenantId] = left;
            }
        }
    }

    private sealed class Count(TimeSpan window)
    {
        public TimeSpan Window { get; } = window;

        public Queue<DateTimeOffset> Taken { get; } = new();

        public DateTimeOffset LastSeen { get; set; }
    }

    private sealed class ProviderRoute;

    /// <summary>One place, given back once however often it is disposed.</summary>
    private sealed class Lease(CallerLimits limits, Guid tenantId) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                limits.Exit(tenantId);
            }
        }
    }
}
