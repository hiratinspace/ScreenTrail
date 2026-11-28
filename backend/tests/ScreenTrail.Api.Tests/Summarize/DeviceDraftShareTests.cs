using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ScreenTrail.Api.Auth;
using ScreenTrail.Api.Data;
using ScreenTrail.Api.Providers;
using ScreenTrail.Api.Summarize;

namespace ScreenTrail.Api.Tests.Summarize;

/// <summary>
/// One device spends at most its share of the tenant's daily drafting budget (E-15): with two devices
/// each may claim half of it, so the first cannot spend the second's, and a tenant of one device keeps
/// the whole budget for it.
/// </summary>
public sealed class DeviceDraftShareTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static readonly Uri Summarize = new("/v1/sessions/summarize", UriKind.Relative);

    [Fact]
    public async Task OneDeviceCannotSpendTheOtherDevicesShareOfTheDay()
    {
        // A one-dollar day, ten cents a draft, two devices: half a dollar each.
        using var host = Host();
        var (tenantId, devices) = await SeedAsync(2);
        using var first = Client(host, tenantId, devices[0]);
        using var second = Client(host, tenantId, devices[1]);

        var answers = new List<HttpStatusCode>();
        for (var i = 0; i < 8; i++)
        {
            using var response = await first.PostAsJsonAsync(Summarize, Bundle($"s-a-{i}"), TestContext.Current.CancellationToken);
            answers.Add(response.StatusCode);
        }

        using var refused = await first.PostAsJsonAsync(Summarize, Bundle("s-a-last"), TestContext.Current.CancellationToken);
        var body = await refused.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        using var other = await second.PostAsJsonAsync(Summarize, Bundle("s-b-0"), TestContext.Current.CancellationToken);

        Assert.Equal([.. Enumerable.Repeat(HttpStatusCode.OK, 5), .. Enumerable.Repeat(HttpStatusCode.PaymentRequired, 3)], answers);
        Assert.Equal(HttpStatusCode.PaymentRequired, refused.StatusCode);
        Assert.Equal("cost_cap_reached", body.GetProperty("status").GetString());
        Assert.Contains("This PC", body.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task ATenantOfOneDeviceKeepsTheWholeBudget()
    {
        using var host = Host();
        var (tenantId, devices) = await SeedAsync(1);
        using var only = Client(host, tenantId, devices[0]);

        var answers = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++)
        {
            using var response = await only.PostAsJsonAsync(Summarize, Bundle($"s-{i}"), TestContext.Current.CancellationToken);
            answers.Add(response.StatusCode);
        }

        Assert.Equal([.. Enumerable.Repeat(HttpStatusCode.OK, 10), HttpStatusCode.PaymentRequired], answers);
    }

    private static SummarizeBundle Bundle(string sessionId) => new()
    {
        SessionId = sessionId,
        DurationMs = 60_000,
        Frames = [new BundleFrame("f1", 1, "Services")],
        Transcript = [new BundleSegment("t1", 2, "restarting it")],
    };

    private WebApplicationFactory<Program> Host() => api.WithWebHostBuilder(builder =>
    {
        _ = builder.UseSetting("Summarization:ApiKey", "not-a-real-key");
        _ = builder.UseSetting("Summarization:DailyCostCapUsd", "1");
        _ = builder.UseSetting("Summarization:MaxSessionCostUsd", "0.10");
        _ = builder.ConfigureTestServices(services => services.AddScoped<ILlmProvider>(_ => new TenCentModel()));
    });

    private HttpClient Client(WebApplicationFactory<Program> host, Guid tenantId, (Guid UserId, Guid DeviceId) device)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Issuer.ForDevice(tenantId, device.UserId, device.DeviceId).Token);
        return client;
    }

    private async Task<(Guid TenantId, List<(Guid UserId, Guid DeviceId)> Devices)> SeedAsync(int count)
    {
        var tenantId = Guid.NewGuid();
        var devices = Enumerable.Range(0, count).Select(_ => (UserId: Guid.NewGuid(), DeviceId: Guid.NewGuid())).ToList();
        _ = await api.UseAsync(async db =>
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme IT", Seats = 5, CreatedAt = DateTimeOffset.UtcNow });
            foreach (var (userId, deviceId) in devices)
            {
                db.Users.Add(new User { Id = userId, TenantId = tenantId, Email = $"{userId:N}@acme.example", DisplayName = "A Technician", CreatedAt = DateTimeOffset.UtcNow });
                db.Devices.Add(new Device { Id = deviceId, TenantId = tenantId, UserId = userId, Name = "TECH-LAPTOP", TokenHash = DeviceTokens.Hash(DeviceTokens.Create()), ActivatedAt = DateTimeOffset.UtcNow });
            }

            return await db.SaveChangesAsync();
        });
        return (tenantId, devices);
    }

    /// <summary>A model that answers at once and costs ten cents a draft.</summary>
    private sealed class TenCentModel : ILlmProvider
    {
        public string Name => "ten-cent";

        public Task<ProviderResult<LlmDraft>> DraftAsync(SummarizeBundle bundle, string? repair, CancellationToken ct = default) =>
            Task.FromResult(ProviderResult.Success(new LlmDraft(
                """
                {"problem": "p", "steps": [], "result": "r", "follow_ups": [], "suggested_title": "t",
                 "suggested_time_minutes": 1, "kb_candidate": false, "kb_reason": "k", "source": "cloud",
                 "prompt_version": "note_v2"}
                """,
                0.10m)));
    }
}
