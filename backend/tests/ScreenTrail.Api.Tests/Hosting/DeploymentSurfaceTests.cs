using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ScreenTrail.Api.Auth;
using ScreenTrail.Api.Data;

namespace ScreenTrail.Api.Tests.Hosting;

/// <summary>
/// What a deployment exposes (ST-007). The reverse proxy in <c>deploy/web/Caddyfile</c> forwards
/// <c>/v1/*</c> and <c>/health</c> to the API and serves the web page for everything else, so outside
/// Development the API must map nothing but those: a route mapped anywhere else would be unreachable
/// through the proxy (and if somebody widened the proxy to reach it, the OpenAPI map and the demo PSA
/// are exactly what must not be). The smoke test in <c>deploy/smoke.sh</c> checks the proxy's half.
/// </summary>
public sealed class DeploymentSurfaceTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public void InProductionEveryRouteIsHealthOrUnderV1()
    {
        using var production = api.WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production));

        var routes = Routes(production.Services);

        Assert.Contains("/health", routes);
        Assert.Contains(routes, r => r.StartsWith("/v1/", StringComparison.Ordinal));
        Assert.DoesNotContain(routes, r => r != "/health" && !r.StartsWith("/v1/", StringComparison.Ordinal));
    }

    [Fact]
    public void InDevelopmentTheOpenApiMapIsTheRouteOutsideThem()
    {
        // The control: the enumeration does see a route outside /v1 when one is mapped.
        var routes = Routes(api.Services);

        Assert.Contains("/swagger/v1/swagger.json", routes);
    }

    [Fact]
    public void WithAProxyListedTheFrameworksOwnSwitchIsRefused()
    {
        var listed = new TrustedProxyOptions();
        listed.KnownProxies.Add("172.30.80.2");

        var thrown = Assert.Throws<InvalidOperationException>(() => listed.Validate("true"));
        Assert.Contains("ASPNETCORE_FORWARDEDHEADERS_ENABLED", thrown.Message, StringComparison.Ordinal);
        _ = Assert.Throws<InvalidOperationException>(() => listed.Validate(" True "));

        listed.Validate(null);
        listed.Validate("false");
        listed.Validate();

        // With nothing listed the switch changes nothing (ForwardedHeadersEnabledTests), so it is not refused.
        new TrustedProxyOptions().Validate("true");
    }

    [Fact]
    public void TheServiceDoesNotStartWithBoth()
    {
        using var both = api.WithWebHostBuilder(builder => builder
            .UseSetting("ForwardedHeaders:KnownProxies:0", "172.30.80.2")
            .UseSetting(TrustedProxyOptions.FrameworkSwitch, "true"));

        var thrown = Assert.ThrowsAny<Exception>(() => both.Services);
        Assert.Contains("ASPNETCORE_FORWARDEDHEADERS_ENABLED", thrown.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRouteThatReadsAJsonBodyDeclaresABodyLimit()
    {
        // D-01. Kestrel's default is 30,000,000 bytes; a route that binds a body says how much it reads.
        var unlimited = Bodied(api.Services)
            .Where(e => e.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize is null)
            .Select(Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(unlimited);
    }

    [Theory]
    [InlineData("POST /v1/sessions/summarize", 16 * 1024 * 1024)]
    [InlineData("POST /v1/sessions/publish", 32 * 1024 * 1024)]
    [InlineData("POST /v1/metrics/sessions", 16 * 1024)]
    [InlineData("POST /v1/admin/invites", 16 * 1024)]
    [InlineData("PUT /v1/admin/policy", 16 * 1024)]
    public void EachBodyLimitIsTheRoutesOwnOrTheGroupsSixteenKilobytes(string route, long limit)
    {
        // The two large routes keep their own limits over the group's.
        var endpoint = Assert.Single(Bodied(api.Services), e => Name(e) == route);

        Assert.Equal(limit, endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
    }

    [Fact]
    public async Task AFourMegabyteMetricIsRefusedBeforeItIsRead()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        _ = await api.UseAsync(async db =>
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "Body limits", Seats = 2, CreatedAt = DateTimeOffset.UtcNow });
            db.Users.Add(new User { Id = userId, TenantId = tenantId, Email = $"{userId:N}@limits.example", DisplayName = "Tech", CreatedAt = DateTimeOffset.UtcNow });
            db.Devices.Add(new Device { Id = deviceId, TenantId = tenantId, UserId = userId, Name = "PC-1", TokenHash = DeviceTokens.Hash(DeviceTokens.Create()), ActivatedAt = DateTimeOffset.UtcNow });
            return await db.SaveChangesAsync();
        });
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Issuer.ForDevice(tenantId, userId, deviceId).Token);
        var body = "{\"session_id\":\"" + new string('a', 4 * 1024 * 1024) + "\",\"started_at\":\"2026-09-25T14:02:00+00:00\",\"duration_ms\":1,\"frames\":0,\"transcript_segments\":0,\"frames_purged_unredacted\":0,\"edit_ratio\":null,\"published\":false}";

        using var response = await client.PostAsync(new Uri("/v1/metrics/sessions", UriKind.Relative), new StringContent(body, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/v1/admin/invites")]
    [InlineData("PUT", "/v1/admin/policy")]
    public async Task AFourMegabyteOwnerBodyIsRefusedBeforeItIsRead(string method, string path)
    {
        var tenantId = Guid.NewGuid();
        _ = await api.UseAsync(async db =>
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "Body limits", Seats = 2, CreatedAt = DateTimeOffset.UtcNow });
            return await db.SaveChangesAsync();
        });
        using var owner = await api.OwnerClientAsync(api, tenantId);
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = new StringContent("{\"email\":\"t@example.test\",\"displayName\":\"" + new string('a', 4 * 1024 * 1024) + "\"}", Encoding.UTF8, "application/json"),
        };

        using var response = await owner.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    private static IEnumerable<RouteEndpoint> Bodied(IServiceProvider services) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAcceptsMetadata>() is not null);

    private static string Name(RouteEndpoint endpoint) =>
        $"{string.Join('|', endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])} /{endpoint.RoutePattern.RawText!.TrimStart('/')}";

    private static List<string> Routes(IServiceProvider services) =>
        [.. services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/'))];
}
