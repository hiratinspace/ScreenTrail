using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using ScreenTrail.Api.Auth;
using ScreenTrail.Api.Data;
using ScreenTrail.Api.Endpoints;
using ScreenTrail.Api.Providers.ConnectWise;
using ScreenTrail.Api.Providers.Hudu;
using ScreenTrail.Api.Tests.Providers.ConnectWise;
using ScreenTrail.Api.Tests.Providers.Hudu;

namespace ScreenTrail.Api.Tests.Providers;

/// <summary>
/// The routes that make the backend call a tenant's PSA or documentation platform are counted per
/// device, per tenant and in flight, after the caller is known (D-06, E-05): a device, or the owner's
/// Test connection, cannot have the backend ask the tenant's provider without bound. The clock is held,
/// so a window ends only when a test moves it.
/// </summary>
public sealed class ProviderCallLimitTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static readonly Uri Check = new("/v1/integrations/connectwise/check", UriKind.Relative);

    [Fact]
    public async Task TheSeventhCheckInAMinuteIsRefusedWithRetryAfterAndAnotherDeviceIsNot()
    {
        var cw = new ScriptedConnectWise();
        var clock = new HeldClock();
        using var host = HostWith(cw, clock);
        var (tenantId, first, second) = await SeedAsync();
        using var a = Client(host, api.Issuer.ForDevice(tenantId, first.UserId, first.DeviceId).Token);
        using var b = Client(host, api.Issuer.ForDevice(tenantId, second.UserId, second.DeviceId).Token);
        using var stored = await a.StoreIntegrationAsOwnerAsync(api, "connectwise", new StoreIntegrationRequest("https://na.myconnectwise.net", "acme+PUB:PRIV-1234"));

        var answered = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            using var ok = await a.PostAsync(Check, null, TestContext.Current.CancellationToken);
            answered.Add(ok.StatusCode);
        }

        using var seventh = await a.PostAsync(Check, null, TestContext.Current.CancellationToken);
        using var other = await b.PostAsync(Check, null, TestContext.Current.CancellationToken);
        var asked = cw.Requests.Count;
        clock.Advance(TimeSpan.FromMinutes(1));
        using var later = await a.PostAsync(Check, null, TestContext.Current.CancellationToken);

        Assert.All(answered, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, seventh.StatusCode);
        Assert.True(int.Parse(seventh.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture) > 0);
        Assert.Contains("Wait", await seventh.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal(7, asked);
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
    }

    [Fact]
    public async Task TwoHundredSearchesFromOneDeviceAreRefusedPastTheLimitAndNeverReachThePsa()
    {
        var cw = new ScriptedConnectWise();
        using var host = HostWith(cw, new HeldClock());
        var (tenantId, first, _) = await SeedAsync();
        using var a = Client(host, api.Issuer.ForDevice(tenantId, first.UserId, first.DeviceId).Token);
        using var stored = await a.StoreIntegrationAsOwnerAsync(api, "connectwise", new StoreIntegrationRequest("https://na.myconnectwise.net", "acme+PUB:PRIV-1234"));

        var answers = new List<HttpStatusCode>();
        string? retryAfter = null;
        for (var i = 0; i < 200; i++)
        {
            using var response = await a.GetAsync(new Uri("/v1/psa/tickets?q=printer", UriKind.Relative), TestContext.Current.CancellationToken);
            answers.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                retryAfter ??= response.Headers.GetValues("Retry-After").Single();
            }
        }

        // 120 a minute per device: four 30-character searches typed in one minute, one request a keystroke.
        Assert.Equal(120, answers.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(80, answers.Count(s => s == HttpStatusCode.TooManyRequests));
        Assert.All(answers.Skip(120), s => Assert.Equal(HttpStatusCode.TooManyRequests, s));
        Assert.Equal(120, cw.Requests.Count);
        Assert.NotNull(retryAfter);
    }

    [Fact]
    public async Task TheOwnersTestConnectionIsCountedToo()
    {
        var cw = new ScriptedConnectWise();
        using var host = HostWith(cw, new HeldClock());
        var (tenantId, first, _) = await SeedAsync();
        using var a = Client(host, api.Issuer.ForDevice(tenantId, first.UserId, first.DeviceId).Token);
        using var stored = await a.StoreIntegrationAsOwnerAsync(api, "connectwise", new StoreIntegrationRequest("https://na.myconnectwise.net", "acme+PUB:PRIV-1234"));
        using var owner = await host.Factory.OwnerClientAsync(api, tenantId);

        var answered = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++)
        {
            using var response = await owner.PostAsync(new Uri("/v1/admin/integrations/connectwise/check", UriKind.Relative), null, TestContext.Current.CancellationToken);
            answered.Add(response.StatusCode);
        }

        Assert.Equal([.. Enumerable.Repeat(HttpStatusCode.OK, 6), HttpStatusCode.TooManyRequests], answered);
        Assert.Equal(6, cw.Requests.Count);
    }

    [Fact]
    public async Task ASearchLongerThanAHundredCharactersIsRefusedWithoutAskingThePsa()
    {
        var cw = new ScriptedConnectWise();
        using var host = HostWith(cw, new HeldClock());
        var (tenantId, first, _) = await SeedAsync();
        using var a = Client(host, api.Issuer.ForDevice(tenantId, first.UserId, first.DeviceId).Token);
        using var stored = await a.StoreIntegrationAsOwnerAsync(api, "connectwise", new StoreIntegrationRequest("https://na.myconnectwise.net", "acme+PUB:PRIV-1234"));

        using var longest = await a.GetAsync(new Uri("/v1/psa/tickets?q=" + new string('p', 100), UriKind.Relative), TestContext.Current.CancellationToken);
        var askedForLongest = cw.Requests.Count;
        using var tooLong = await a.GetAsync(new Uri("/v1/psa/tickets?q=" + new string('p', 101), UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, longest.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(askedForLongest, cw.Requests.Count);
    }

    [Fact]
    public async Task ATenantHasAtMostSixteenProviderCallsInFlight()
    {
        // A PSA that never answers holds each search; past sixteen at once for the tenant, the next is
        // refused at once rather than held as well (E-05: a slow site cannot take every tenant's sockets).
        var cw = new ScriptedConnectWise { HangingRoute = "/service/tickets" };
        using var host = HostWith(cw, new HeldClock());
        var (tenantId, first, second) = await SeedAsync();
        using var a = Client(host, api.Issuer.ForDevice(tenantId, first.UserId, first.DeviceId).Token);
        using var b = Client(host, api.Issuer.ForDevice(tenantId, second.UserId, second.DeviceId).Token);
        using var stored = await a.StoreIntegrationAsOwnerAsync(api, "connectwise", new StoreIntegrationRequest("https://na.myconnectwise.net", "acme+PUB:PRIV-1234"));
        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var held = Enumerable.Range(0, 16)
            .Select(i => (i % 2 == 0 ? a : b).GetAsync(new Uri("/v1/psa/tickets?q=printer", UriKind.Relative), giveUp.Token))
            .ToList();
        while (cw.Requests.Count < 16)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        // Either the seventeenth is answered, or it reaches the PSA and is held with the rest.
        var seventeenth = b.GetAsync(new Uri("/v1/psa/tickets?q=printer", UriKind.Relative), giveUp.Token);
        while (!seventeenth.IsCompleted && cw.Requests.Count < 17)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        var reachedThePsa = cw.Requests.Count;
        await giveUp.CancelAsync();
        foreach (var call in held.Append(seventeenth))
        {
            try
            {
                (await call).Dispose();
            }
            catch (OperationCanceledException)
            {
                // The held searches end with the test giving up on them.
            }
        }

        Assert.Equal(16, reachedThePsa);
        Assert.True(seventeenth.IsCompletedSuccessfully);
        using var refused = await seventeenth;
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    private WebApplicationFactoryHost HostWith(ScriptedConnectWise cw, HeldClock clock) => new(api.WithWebHostBuilder(builder =>
    {
        _ = builder.UseSetting("ConnectWise:ClientId", "11111111-2222-3333-4444-555555555555");
        _ = builder.ConfigureTestServices(services =>
        {
            clock.Install(services);
            _ = services.Configure<HttpClientFactoryOptions>(nameof(ConnectWiseProvider), options => options.HttpMessageHandlerBuilderActions.Add(handler => handler.PrimaryHandler = cw));
            _ = services.Configure<HttpClientFactoryOptions>(nameof(HuduProvider), options => options.HttpMessageHandlerBuilderActions.Add(handler => handler.PrimaryHandler = new ScriptedHudu()));
        });
    }));

    private static HttpClient Client(WebApplicationFactoryHost host, string token)
    {
        var client = host.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(Guid TenantId, (Guid UserId, Guid DeviceId) First, (Guid UserId, Guid DeviceId) Second)> SeedAsync()
    {
        var tenantId = Guid.NewGuid();
        var first = (UserId: Guid.NewGuid(), DeviceId: Guid.NewGuid());
        var second = (UserId: Guid.NewGuid(), DeviceId: Guid.NewGuid());
        _ = await api.UseAsync(async db =>
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme IT", Seats = 5, CreatedAt = DateTimeOffset.UtcNow });
            foreach (var (userId, deviceId) in new[] { first, second })
            {
                db.Users.Add(new User { Id = userId, TenantId = tenantId, Email = $"{userId:N}@acme.example", DisplayName = "A Technician", CreatedAt = DateTimeOffset.UtcNow });
                db.Devices.Add(new Device { Id = deviceId, TenantId = tenantId, UserId = userId, Name = "TECH-LAPTOP", TokenHash = DeviceTokens.Hash(DeviceTokens.Create()), ActivatedAt = DateTimeOffset.UtcNow });
            }

            return await db.SaveChangesAsync();
        });
        return (tenantId, first, second);
    }

    private sealed class WebApplicationFactoryHost(WebApplicationFactory<Program> factory) : IDisposable
    {
        public WebApplicationFactory<Program> Factory { get; } = factory;

        public void Dispose() => Factory.Dispose();
    }
}
