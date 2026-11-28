using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using ScreenTrail.Api.Auth;
using ScreenTrail.Api.Data;
using ScreenTrail.Api.Endpoints;
using ScreenTrail.Api.Providers.ConnectWise;
using ScreenTrail.Api.Providers.Hudu;
using ScreenTrail.Api.Publish;
using ScreenTrail.Api.Summarize;
using ScreenTrail.Api.Tests.Providers.ConnectWise;
using ScreenTrail.Api.Tests.Providers.Hudu;

namespace ScreenTrail.Api.Tests.Publish;

/// <summary>
/// What a publish may take from the device and what the backend decides itself (E-02): how often one
/// device and one tenant publish, the reviewer line on a note the customer reads, the company a
/// knowledge-base article is filed under, and the time a time entry starts. The clock is held, so a
/// window ends only when a test moves it.
/// </summary>
public sealed class PublishTrustTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static readonly Uri Publish = new("/v1/sessions/publish", UriKind.Relative);

    [Fact]
    public async Task TheThirtyFirstPublishInTenMinutesIsRefusedAndAnotherDeviceStillPublishes()
    {
        var cw = new ScriptedConnectWise();
        var clock = new HeldClock();
        using var host = Host(cw, new ScriptedHudu(), clock);
        var (a, b, _) = await ConnectedClientsAsync(host);

        var answers = new List<HttpStatusCode>();
        for (var i = 0; i < 40; i++)
        {
            using var response = await a.PostAsJsonAsync(Publish, Bundle(clock) with { Destinations = ["time_entry"] }, TestContext.Current.CancellationToken);
            answers.Add(response.StatusCode);
        }

        var reachedThePsa = cw.Requests.Count;
        using var other = await b.PostAsJsonAsync(Publish, Bundle(clock) with { Destinations = ["time_entry"] }, TestContext.Current.CancellationToken);
        using var refused = await a.PostAsJsonAsync(Publish, Bundle(clock) with { Destinations = ["time_entry"] }, TestContext.Current.CancellationToken);
        var body = await refused.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(10));
        using var later = await a.PostAsJsonAsync(Publish, Bundle(clock) with { Destinations = ["time_entry"] }, TestContext.Current.CancellationToken);

        Assert.Equal(30, answers.Count(s => s == HttpStatusCode.OK));
        Assert.All(answers.Skip(30), s => Assert.Equal(HttpStatusCode.TooManyRequests, s));
        Assert.Equal(30, reachedThePsa);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(int.Parse(refused.Headers.GetValues("Retry-After").Single(), CultureInfo.InvariantCulture) > 0);
        Assert.Equal("too_many_requests", body.GetProperty("error").GetString());
        Assert.Contains("publish", body.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
    }

    [Fact]
    public async Task ANoteTheCustomerReadsAlwaysSaysWhoReviewedIt()
    {
        // ST-093 AC3 keeps the footer a choice for internal notes; a Discussion note is the customer's
        // and carries the reviewer whatever the request says.
        var cw = new ScriptedConnectWise();
        var clock = new HeldClock();
        using var host = Host(cw, new ScriptedHudu(), clock);
        var (a, _, _) = await ConnectedClientsAsync(host);

        using var discussion = await a.PostAsJsonAsync(Publish, Bundle(clock) with { NoteType = "discussion", Footer = false, Destinations = ["ticket_note"] }, TestContext.Current.CancellationToken);
        var customer = NoteText(cw, 0);
        using var internalNote = await a.PostAsJsonAsync(Publish, Bundle(clock) with { NoteType = "internal", Footer = false, Destinations = ["ticket_note"] }, TestContext.Current.CancellationToken);
        var team = NoteText(cw, 1);

        Assert.Equal(HttpStatusCode.OK, discussion.StatusCode);
        Assert.EndsWith("Drafted with ScreenTrail, reviewed by A Technician.", customer, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, internalNote.StatusCode);
        Assert.DoesNotContain("reviewed by", team, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnArticleIsFiledUnderTheCompanyThePsaGivesTheTicketNotTheOneTheDeviceNames()
    {
        // Ticket 48213 is Acme Dental's in the PSA (Hudu company 7); the device names another customer.
        var hudu = new ScriptedHudu();
        var clock = new HeldClock();
        using var host = Host(new ScriptedConnectWise(), hudu, clock);
        var (a, _, _) = await ConnectedClientsAsync(host, withHudu: true);

        using var response = await a.PostAsJsonAsync(Publish, Bundle(clock) with { Company = "Borough Legal, LLC", Destinations = ["kb_article"] }, TestContext.Current.CancellationToken);
        var published = await response.Content.ReadFromJsonAsync<PublishResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var kb = Assert.Single(published!.Results);
        Assert.True(kb.Ok, kb.Error);
        var create = hudu.Requests.Single(r => r.RequestUri!.AbsolutePath.EndsWith("/api/v1/articles", StringComparison.Ordinal));
        var article = JsonDocument.Parse(hudu.Bodies[hudu.Requests.IndexOf(create)]!).RootElement.GetProperty("article");
        Assert.Equal(7, article.GetProperty("company_id").GetInt32());
    }

    [Fact]
    public async Task AnArticleForATicketThePsaDoesNotHaveIsNotFiled()
    {
        var hudu = new ScriptedHudu();
        var clock = new HeldClock();
        using var host = Host(new ScriptedConnectWise(), hudu, clock);
        var (a, _, _) = await ConnectedClientsAsync(host, withHudu: true);

        using var response = await a.PostAsJsonAsync(Publish, Bundle(clock) with { TicketId = "99999", Company = "Acme Dental", Destinations = ["kb_article"] }, TestContext.Current.CancellationToken);
        var published = await response.Content.ReadFromJsonAsync<PublishResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var kb = Assert.Single(published!.Results);
        Assert.False(kb.Ok);
        Assert.DoesNotContain(hudu.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/api/v1/articles", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(11)]
    [InlineData(-366 * 24 * 60)]
    public async Task ATimeEntryThatStartsInTheFutureOrBeforeAnySessionCanBeKeptIsRefused(int minutesFromNow)
    {
        var cw = new ScriptedConnectWise();
        var clock = new HeldClock();
        using var host = Host(cw, new ScriptedHudu(), clock);
        var (a, _, _) = await ConnectedClientsAsync(host);

        using var response = await a.PostAsJsonAsync(Publish, Bundle(clock) with { StartedAt = clock.GetUtcNow().AddMinutes(minutesFromNow) }, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("rejected", body.GetProperty("error").GetString());
        Assert.Empty(cw.Requests);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(-30)]
    [InlineData(-365 * 24 * 60 + 1)]
    public async Task ATimeEntryInsideTheWindowIsPublished(int minutesFromNow)
    {
        var cw = new ScriptedConnectWise();
        var clock = new HeldClock();
        using var host = Host(cw, new ScriptedHudu(), clock);
        var (a, _, _) = await ConnectedClientsAsync(host);

        using var response = await a.PostAsJsonAsync(Publish, Bundle(clock) with { StartedAt = clock.GetUtcNow().AddMinutes(minutesFromNow), Destinations = ["time_entry"] }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string NoteText(ScriptedConnectWise cw, int nth)
    {
        var requests = cw.Requests;
        var bodies = cw.Bodies;
        var notes = requests.Select((r, i) => (r, i)).Where(x => x.r.RequestUri!.AbsolutePath.EndsWith("/notes", StringComparison.Ordinal)).ToList();
        return JsonDocument.Parse(bodies[notes[nth].i]!).RootElement.GetProperty("text").GetString()!;
    }

    private static PublishBundle Bundle(HeldClock clock) => new()
    {
        SessionId = "s-0001",
        TicketId = "48213",
        Company = "Acme Dental",
        NoteType = "internal",
        Minutes = 30,
        StartedAt = clock.GetUtcNow().AddHours(-1),
        Billable = true,
        Footer = true,
        Destinations = ["ticket_note", "time_entry"],
        Note = new DraftJson
        {
            Problem = "Printer offline in reception.",
            Steps = [new DraftStepJson { Text = "Checked the spooler service; it was stopped.", Confidence = "high" }],
            Result = "Printing works again.",
            FollowUps = [],
            SuggestedTitle = "Printer offline: Acme Dental",
            SuggestedTimeMinutes = 23,
            KbCandidate = true,
            KbReason = "Recurring",
            Source = "cloud",
            PromptVersion = "note_v2",
        },
        Frames = [],
    };

    private WebApplicationFactory<Program> Host(ScriptedConnectWise cw, ScriptedHudu hudu, HeldClock clock) => api.WithWebHostBuilder(builder =>
    {
        _ = builder.UseSetting("ConnectWise:ClientId", "11111111-2222-3333-4444-555555555555");
        _ = builder.ConfigureTestServices(services =>
        {
            clock.Install(services);
            _ = services.Configure<HttpClientFactoryOptions>(nameof(ConnectWiseProvider), options => options.HttpMessageHandlerBuilderActions.Add(handler => handler.PrimaryHandler = cw));
            _ = services.Configure<HttpClientFactoryOptions>(nameof(HuduProvider), options => options.HttpMessageHandlerBuilderActions.Add(handler => handler.PrimaryHandler = hudu));
        });
    });

    /// <summary>Two devices of one tenant whose ConnectWise (and, when asked, Hudu) credential is in the vault.</summary>
    private async Task<(HttpClient A, HttpClient B, Guid TenantId)> ConnectedClientsAsync(WebApplicationFactory<Program> host, bool withHudu = false)
    {
        var tenantId = Guid.NewGuid();
        var devices = new List<(Guid UserId, Guid DeviceId)> { (Guid.NewGuid(), Guid.NewGuid()), (Guid.NewGuid(), Guid.NewGuid()) };
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
        var clients = devices.Select(d =>
        {
            var client = host.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Issuer.ForDevice(tenantId, d.UserId, d.DeviceId).Token);
            return client;
        }).ToList();
        using var cw = await clients[0].StoreIntegrationAsOwnerAsync(api, "connectwise", new StoreIntegrationRequest("https://na.myconnectwise.net", "acme+PUB:PRIV-1234"));
        if (withHudu)
        {
            using var h = await clients[0].StoreIntegrationAsOwnerAsync(api, "hudu", new StoreIntegrationRequest("https://acme.huducloud.com", "hudu-key-5678"));
        }

        return (clients[0], clients[1], tenantId);
    }
}
