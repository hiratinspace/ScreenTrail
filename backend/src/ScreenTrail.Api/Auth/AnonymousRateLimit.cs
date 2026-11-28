using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ScreenTrail.Api.Auth;

/// <summary>
/// How many anonymous calls one client may make (ST-099 hardening, ADR-0009). Every default is the
/// closed one: ten calls a minute from one address, which is several sign-ins for a person and very few
/// guesses for anybody else.
/// </summary>
public sealed class RateLimitOptions
{
    public const string Section = "RateLimit";

    /// <summary>Calls one client address may make to the limited routes in one window.</summary>
    public int PermitLimit { get; set; } = 10;

    /// <summary>The window those calls are counted in. A refused call is told to wait this long.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Refuses, at startup, a limit that would not limit anything or would lock everyone out for good.</summary>
    public void Validate()
    {
        if (PermitLimit is < 1 or > 100_000)
        {
            throw new InvalidOperationException("RateLimit:PermitLimit must be between 1 and 100000.");
        }

        if (Window < TimeSpan.FromSeconds(1) || Window > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException("RateLimit:Window must be between 1 second and 1 hour.");
        }
    }
}

/// <summary>
/// The limit on the routes anybody can call without a session or a token: the admin's sign-in nonce,
/// sign-in and signup, and a device's activation with an invite code (ST-099 hardening, ADR-0009).
/// Sign-out is left out on purpose, so ending a stolen session never waits for a window to pass. Each costs a signature check, a database lookup or a guess at a code, so each is counted
/// per client address in a fixed window, and the call past the limit is refused with 429, a
/// <c>Retry-After</c> and no body, before it is read, authenticated or checked against Google's keys.
///
/// <b>The address is the connection's,</b> unless the connection comes from a proxy the operator listed
/// (<see cref="TrustedProxyOptions"/>), in which case it is the client that proxy names. From anybody
/// else <c>X-Forwarded-For</c> and its kin are never read: a header the caller writes cannot choose its
/// own partition. An IPv6 caller is counted by its /64, which is what one subscriber is
/// usually given, so rotating through its own addresses does not buy more calls.
/// </summary>
public static class AnonymousRateLimit
{
    /// <summary>Sign-in and signup, which share one count: each checks a Google token and may write a session or a tenant.</summary>
    public const string Policy = "anonymous-per-client";

    /// <summary>
    /// The sign-in nonce, counted on its own and <see cref="NonceMultiple"/> times as generously (D-11): it
    /// costs one signature and writes nothing, and a neighbour who spends it must not lock the office out
    /// of signing in.
    /// </summary>
    public const string NoncePolicy = "anonymous-nonce";

    /// <summary>A device's activation with an invite code, counted on its own (D-11), so a sign-in flood does not stop a PC being set up, nor the reverse.</summary>
    public const string ActivationPolicy = "anonymous-activation";

    /// <summary>Every anonymous count, for the routes and tests that check a route is counted by one of them.</summary>
    public static readonly IReadOnlyList<string> Policies = [Policy, NoncePolicy, ActivationPolicy];

    /// <summary>
    /// How many times the sign-in limit the nonce allows: sixty a minute at the default ten. The page asks
    /// for one each time it draws the sign-in button, so a person reloading the page uses a few.
    /// </summary>
    public const int NonceMultiple = 6;

    public static IServiceCollection AddAnonymousRateLimit(this IServiceCollection services, RateLimitOptions limits)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(limits);
        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Three counts per client address, each its own window (D-11): one route's callers cannot
            // spend another's.
            foreach (var (policy, permits) in new[] { (Policy, limits.PermitLimit), (NoncePolicy, limits.PermitLimit * NonceMultiple), (ActivationPolicy, limits.PermitLimit) })
            {
                options.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(context.Connection.RemoteIpAddress),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permits,
                        Window = limits.Window,
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));
            }

            // No body: which limit, how many are left and whose partition this is help only somebody
            // tuning a script. Retry-After is all a person's browser needs.
            options.OnRejected = (rejected, _) =>
            {
                var wait = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var after) && after > TimeSpan.Zero ? after : limits.Window;
                rejected.HttpContext.Response.Headers.RetryAfter = ((long)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
        });
    }

    /// <summary>
    /// Refuses, with 403 and before it is counted, a call to a limited route that a browser says a page
    /// other than the admin's own started (D-11 with G-02). Without this a page could spend an office's
    /// allowance from its visitors' browsers, which share the office's address. Accepted are only the
    /// admin's own page (<c>same-origin</c>), an address the person typed or opened from a bookmark
    /// (<c>none</c>), and no header at all, which is a device or an older browser: never <c>same-site</c>,
    /// since a page on a sibling host is not the admin, nor a value a later browser might add.
    ///
    /// The same holds for every route under <c>/v1/admin</c>, counted or not (D-12): sign-out included,
    /// so another page cannot sign the owner out, and the owner's routes, which the cookie's SameSite and
    /// the CSRF header already guard, refuse such a page before anything reads the session.
    /// </summary>
    public static IApplicationBuilder UseCrossSiteRefusal(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            if ((context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>() is not null
                    || context.Request.Path.StartsWithSegments("/v1/admin", StringComparison.OrdinalIgnoreCase))
                && !FromTheAdminsOwnPage(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            return next(context);
        });

    /// <summary>True when the request carries no <c>Sec-Fetch-Site</c>, or says <c>same-origin</c> or <c>none</c>.</summary>
    internal static bool FromTheAdminsOwnPage(HttpRequest request)
    {
        var site = request.Headers["Sec-Fetch-Site"];
        return site.Count == 0
            || (site.Count == 1 && (string.Equals(site[0], "same-origin", StringComparison.OrdinalIgnoreCase) || string.Equals(site[0], "none", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// The partition for a client address: IPv4 as it is, IPv4 carried in IPv6 as the IPv4 it is, IPv6
    /// by its /64. A connection with no address (a Unix socket, a test host) shares one partition.
    /// </summary>
    public static string PartitionKey(IPAddress? address)
    {
        if (address is null)
        {
            return "none";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes).ToString() + "/64";
    }
}
