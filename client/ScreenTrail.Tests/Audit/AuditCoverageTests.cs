using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ScreenTrail.Core.Audit;
using ScreenTrail.Core.Net;
using ScreenTrail.Core.Review.Publish;
using ScreenTrail.Core.Settings;
using ScreenTrail.Core.Store;
using ScreenTrail.Shared.Ipc;
using ScreenTrail.Shared.Schema;
using ScreenTrail.Shared.Settings;
using ScreenTrail.Tests.Sessions;
using static ScreenTrail.Tests.Review.ReviewFixture;

namespace ScreenTrail.Tests.Audit;

/// <summary>
/// The audit log records the events that change what is captured, masked, kept or sent (ST-045): a session
/// starting, pausing, resuming and stopping, a settings save, a policy taking effect, a blur in Review and a
/// publish. Each is driven through the class that does it in the service, against the real store, and the
/// row is looked for by its wire name, which is what a customer reading an export sees.
///
/// Every new row is a type, a count and a fixed label (INV-10): a test here also checks that what was saved
/// (a custom pattern, a ticket) never reaches the log.
/// </summary>
public sealed class AuditCoverageTests : IAsyncDisposable
{
    private static readonly RemoteTool Rdp = new() { Kind = RemoteToolKind.Rdp };
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "screentrail-tests", Guid.NewGuid().ToString("N"));
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private SqliteSessionStore? _store;
    private MachineHarness? _harness;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASessionRecordsItsStartPauseResumeAndStop()
    {
        _harness = await MachineHarness.StartAsync();
        var machine = _harness.Machine;

        Assert.True(await machine.StartAsync(Rdp, ct: Ct));
        var session = machine.SessionId;
        Assert.True(await machine.PauseAsync(Ct));
        Assert.True(await machine.ResumeAsync(Ct));
        Assert.True(await machine.StopAsync(Ct));

        var rows = (await _harness.Store.GetAuditRecordsAsync(session, Ct))
            .Where(r => r.Type.StartsWith("session_", StringComparison.Ordinal) && r.Type != AuditTypes.SessionDiscarded)
            .ToList();

        Assert.Equal(["session_started", "session_paused", "session_resumed", "session_stopped"], rows.Select(r => r.Type));
        Assert.Equal("Technician", rows[0].Detail);
        Assert.Equal("Technician", rows[3].Detail);
        Assert.True((await _harness.Store.VerifyAuditAsync(Ct)).Intact);
    }

    [Fact]
    public async Task ASettingsSaveRecordsWhichSettingChangedAndNeverItsValue()
    {
        var store = await OpenAsync();
        var commands = new SettingsCommands(
            new ClientSettingsStore(Path.Combine(_dir, "settings.json")),
            () => TenantPolicy.Default,
            _ => { },
            _ => Task.FromResult(new AuditExportResult("/data/exports/audit.json", 0, new AuditVerification(true, 0, 0, null), null)),
            audit: store);

        var saved = new ClientSettings { Passwords = false, CustomPatterns = ["acme-ticket-\\d{6}"] };

        // A switch going off needs the connection's token since DEC-06.
        var window = Guid.NewGuid();
        var token = commands.Confirm(new RequestConfirmationCommand { RequestId = 0, Action = SettingsCommands.ConfirmAction }, window)!.Token;
        Assert.True((await commands.HandleAsync(new SetSettingsCommand { RequestId = 1, Settings = saved, Confirmation = token }, window, Ct))!.Ok);

        var rows = (await store.GetAuditRecordsAsync(ct: Ct)).Where(r => r.Type == "settings_changed").ToList();
        Assert.Equal(["CustomPatterns", "PasswordsOff"], rows.Select(r => r.Detail).Order(StringComparer.Ordinal));
        Assert.Equal(1, rows.Single(r => r.Detail == "CustomPatterns").Count);
        Assert.DoesNotContain("acme", AuditExport.ToCsv(await store.GetAuditRecordsAsync(ct: Ct)), StringComparison.OrdinalIgnoreCase);

        // Saving the same document again changes nothing, and says nothing.
        Assert.True((await commands.HandleAsync(new SetSettingsCommand { RequestId = 2, Settings = saved }, Ct))!.Ok);
        Assert.Equal(2, (await store.GetAuditRecordsAsync(ct: Ct)).Count(r => r.Type == "settings_changed"));
    }

    [Fact]
    public async Task ASettingsFileChangedOnDiskIsRecordedAtTheNextStart()
    {
        // A-03 (c): an edit made to the files while the service was not running is found at start and
        // recorded as the pipe's saves are, the setting's label and never its value.
        var store = await OpenAsync();
        var settingsPath = Path.Combine(_dir, "settings.json");
        var capturePath = Path.Combine(_dir, "capture.json");
        var settings = new ClientSettingsStore(settingsPath);
        var capture = new CaptureSettingsStore(capturePath);
        Assert.True(settings.Save(new ClientSettings { CustomPatterns = ["acme-ticket-\\d{6}"] }));
        Assert.True(capture.Save(new CaptureSettings()));
        await File.WriteAllTextAsync(settingsPath, """{ "passwords": false, "custom_patterns": [] }""", Ct);
        await File.WriteAllTextAsync(capturePath, """{ "capture_all_windows": true }""", Ct);

        Assert.True(await SettingsAtStart.RecordAsync(store, settings, settings.Load(), capture, capture.Load(), Ct));

        var rows = (await store.GetAuditRecordsAsync(ct: Ct)).Where(r => r.Type == "settings_changed").ToList();
        Assert.Equal(["AllWindowsOn", "CustomPatterns", "PasswordsOff"], rows.Select(r => r.Detail).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("acme", AuditExport.ToCsv(await store.GetAuditRecordsAsync(ct: Ct)), StringComparison.OrdinalIgnoreCase);

        // The next start finds nothing new, and says nothing.
        Assert.True(await SettingsAtStart.RecordAsync(store, settings, settings.Load(), capture, capture.Load(), Ct));
        Assert.Equal(3, (await store.GetAuditRecordsAsync(ct: Ct)).Count(r => r.Type == "settings_changed"));
    }

    [Fact]
    public async Task AFirstStartAfterTheUpgradeRecordsNothing()
    {
        // Files an older build wrote, with nothing beside them to compare against: nothing is said, and the
        // next start compares against what this one applied.
        var store = await OpenAsync();
        Directory.CreateDirectory(_dir);
        var settingsPath = Path.Combine(_dir, "settings.json");
        await File.WriteAllTextAsync(settingsPath, """{ "passwords": false }""", Ct);
        var settings = new ClientSettingsStore(settingsPath);
        var capture = new CaptureSettingsStore(Path.Combine(_dir, "capture.json"));

        Assert.True(await SettingsAtStart.RecordAsync(store, settings, settings.Load(), capture, capture.Load(), Ct));
        await File.WriteAllTextAsync(settingsPath, """{ "passwords": true }""", Ct);
        Assert.True(await SettingsAtStart.RecordAsync(store, settings, settings.Load(), capture, capture.Load(), Ct));

        var row = Assert.Single(await store.GetAuditRecordsAsync(ct: Ct), r => r.Type == "settings_changed");
        Assert.Equal("PasswordsOn", row.Detail);
    }

    [Fact]
    public async Task ACaptureSettingsSaveThatWidensTheScopeIsRecorded()
    {
        var store = await OpenAsync();
        var commands = new CaptureSettingsCommands(
            new CaptureSettingsStore(Path.Combine(_dir, "capture.json")),
            () => [new ToolRow("rdp", "Remote Desktop")],
            () => [],
            () => TenantPolicy.Default,
            () => 120,
            _ => { },
            audit: store);

        var saved = new CaptureSettings { CaptureAllWindows = true };

        // All windows on needs the connection's token since contract v17 (INV-5).
        var window = Guid.NewGuid();
        var token = commands.Confirm(new RequestConfirmationCommand { RequestId = 0, Action = CaptureSettingsCommands.ConfirmAction }, window)!.Token;
        Assert.True((await commands.HandleAsync(new SetCaptureSettingsCommand { RequestId = 1, Settings = saved, Confirmation = token }, window, Ct))!.Ok);

        var row = Assert.Single(await store.GetAuditRecordsAsync(ct: Ct), r => r.Type == "settings_changed");
        Assert.Equal("AllWindowsOn", row.Detail);
    }

    [Fact]
    public async Task APolicyThatTakesEffectIsRecordedWithItsVersion()
    {
        var store = await OpenAsync();
        var body = new { version = "p20260925140200000", retentionDays = 3, localOnly = true, localOnlyLocked = true, captureAllWindows = false };
        var sync = new PolicySync(
            new HttpClient(new Replies(body)) { BaseAddress = new Uri("https://api.screentrail.example/") },
            () => "token",
            new FilePolicyCache(Path.Combine(_dir, "policy.json")),
            audit: store);

        Assert.True(await sync.FetchAsync(Ct));
        Assert.True(await sync.FetchAsync(Ct));

        // Once: the second fetch brought the same policy, so nothing new took effect.
        var row = Assert.Single(await store.GetAuditRecordsAsync(ct: Ct), r => r.Type == "policy_applied");
        Assert.Equal("p20260925140200000", row.Detail);
    }

    [Fact]
    public async Task ACachedPolicyInForceAtStartIsRecordedOnce()
    {
        // F-06: until the first fetch the file kept between runs decides; that it did is in the log.
        var store = await OpenAsync();
        var cache = new FilePolicyCache(Path.Combine(_dir, "policy.json"));
        cache.Save(new TenantPolicy("p20260925140200000", 3, true, true, false));
        var body = new { version = "p20260925140200000", retentionDays = 3, localOnly = true, localOnlyLocked = true, captureAllWindows = false };
        var sync = new PolicySync(new HttpClient(new Replies(body)) { BaseAddress = new Uri("https://api.screentrail.example/") }, () => "token", cache, audit: store);

        await sync.RecordInForceAtStartAsync(Ct);
        Assert.True(await sync.FetchAsync(Ct));

        // The fetch brought the same policy, so nothing new took effect.
        var row = Assert.Single(await store.GetAuditRecordsAsync(ct: Ct), r => r.Type == "policy_applied");
        Assert.Equal("p20260925140200000", row.Detail);
    }

    [Fact]
    public async Task NoPolicyKeptBetweenRunsRecordsNothingAtStart()
    {
        var store = await OpenAsync();
        var sync = new PolicySync(new HttpClient(new Replies(new { })) { BaseAddress = new Uri("https://api.screentrail.example/") }, () => "token", new FilePolicyCache(Path.Combine(_dir, "policy.json")), audit: store);

        await sync.RecordInForceAtStartAsync(Ct);

        Assert.DoesNotContain(await store.GetAuditRecordsAsync(ct: Ct), r => r.Type == "policy_applied");
    }

    [Fact]
    public async Task APolicyVersionThatIsNotALabelIsLeftOutOfItsRow()
    {
        // The version is the backend's text, not an enum: the row is still written, without it.
        var store = await OpenAsync();
        var body = new { version = "p20260925\n", retentionDays = 3, localOnly = true, localOnlyLocked = true, captureAllWindows = false };
        var sync = new PolicySync(
            new HttpClient(new Replies(body)) { BaseAddress = new Uri("https://api.screentrail.example/") },
            () => "token",
            new FilePolicyCache(Path.Combine(_dir, "policy.json")),
            audit: store);

        Assert.True(await sync.FetchAsync(Ct));

        var row = Assert.Single(await store.GetAuditRecordsAsync(ct: Ct), r => r.Type == "policy_applied");
        Assert.Null(row.Detail);
    }

    [Fact]
    public async Task ABlurInReviewIsRecordedAgainstItsSession()
    {
        var store = await OpenAsync();
        await SeedAsync(store, ["f1"]);

        await store.ApplyUserBlurAsync("f1", new byte[] { 0xB1 }, new MaskedRegion { X = 0, Y = 0, Width = 10, Height = 10, Kind = MaskKind.UserBlur }, Ct);

        var row = Assert.Single(await store.GetAuditRecordsAsync("s1", Ct), r => r.Type == "frame_blurred");
        Assert.Equal(1, row.Count);
        Assert.True((await store.VerifyAuditAsync(Ct)).Intact);
    }

    [Fact]
    public async Task APublishRecordsEachDestinationThatLanded()
    {
        var store = await OpenAsync();
        await SeedAsync(store, ["f1"]);
        var commands = new PublishCommands(store, new LandingGateway());

        _ = Assert.IsType<SessionPublished>(await commands.ReplyToAsync(
            new PublishSessionCommand
            {
                RequestId = 7,
                SessionId = "s1",
                TicketId = "48213",
                TicketCompany = "Acme Dental",
                NoteType = "internal",
                Minutes = 30,
                Destinations = ["ticket_note", "time_entry"],
                Note = Draft(),
                FrameIds = ["f1"],
            },
            Ct));

        var rows = (await store.GetAuditRecordsAsync("s1", Ct)).Where(r => r.Type == "published").ToList();
        Assert.Equal(["ticket_note", "time_entry"], rows.Select(r => r.Detail).Order(StringComparer.Ordinal));
        var export = AuditExport.ToCsv(await store.GetAuditRecordsAsync(ct: Ct));
        Assert.DoesNotContain("48213", export, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme", export, StringComparison.Ordinal);
    }

    private async Task<SqliteSessionStore> OpenAsync() =>
        _store ??= await SqliteSessionStore.OpenAsync(Path.Combine(_dir, "store.db"), new FixedKey(_key), ct: Ct);

    private static async Task SeedAsync(SqliteSessionStore store, string[] frames)
    {
        await store.CreateSessionAsync(new NewSession("s1", DateTimeOffset.UnixEpoch, Rdp, false, null), Ct);
        foreach (var id in frames)
        {
            await store.SaveRedactedFrameAsync(
                "s1",
                new StagedFrame(id, 1000, FrameTrigger.Click, 1600, 900, null, new byte[] { 1 }),
                new RedactionOutcome(new byte[] { 2 }, "t", [], false, DateTimeOffset.UnixEpoch),
                Ct);
        }

        await store.SaveDraftAsync("s1", Draft(), Ct);
        await store.FinalizeSessionAsync("s1", new FinalizeInfo(10_000, false), Ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_store is not null)
        {
            await _store.DisposeAsync();
        }

        if (_harness is not null)
        {
            await _harness.DisposeAsync();
        }

        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private sealed class FixedKey(byte[] key) : IStoreKeyProvider
    {
        public byte[] GetKey() => (byte[])key.Clone();
    }

    /// <summary>The same policy, answered fresh to every fetch.</summary>
    private sealed class Replies(object body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
    }

    /// <summary>A backend on which every destination lands. Nothing else is asked of it here.</summary>
    private sealed class LandingGateway : IPsaGateway
    {
        public Task<GatewayAnswer<IReadOnlyList<PublishOutcomeRow>>> PublishAsync(PublishWire bundle, CancellationToken ct = default) =>
            Task.FromResult(GatewayAnswer.Of<IReadOnlyList<PublishOutcomeRow>>([.. bundle.Destinations.Select(d => new PublishOutcomeRow(d, true, "1"))]));

        public Task<GatewayAnswer<IReadOnlyList<IntegrationInfo>>> IntegrationsAsync(CancellationToken ct = default) => Refused<IReadOnlyList<IntegrationInfo>>();

        public Task<GatewayAnswer<IReadOnlyList<TicketRow>>> SearchTicketsAsync(string query, CancellationToken ct = default) => Refused<IReadOnlyList<TicketRow>>();

        public Task<GatewayAnswer<CompanyMappingsAnswer>> CompanyMappingsAsync(CancellationToken ct = default) => Refused<CompanyMappingsAnswer>();

        public Task<GatewayAnswer<bool>> MapCompanyAsync(string psaCompany, string docCompanyId, CancellationToken ct = default) => Refused<bool>();

        public Task<GatewayAnswer<IReadOnlyList<IntegrationDetailRow>>> IntegrationDetailsAsync(CancellationToken ct = default) => Refused<IReadOnlyList<IntegrationDetailRow>>();

        public Task<GatewayAnswer<bool>> StoreIntegrationAsync(string provider, string siteUrl, string secret, CancellationToken ct = default) => Refused<bool>();

        public Task<GatewayAnswer<bool>> RemoveIntegrationAsync(string provider, CancellationToken ct = default) => Refused<bool>();

        public Task<GatewayAnswer<IntegrationCheckRow>> CheckIntegrationAsync(string provider, CancellationToken ct = default) => Refused<IntegrationCheckRow>();

        public Task<GatewayAnswer<bool>> UnmapCompanyAsync(string psaCompany, CancellationToken ct = default) => Refused<bool>();

        private static Task<GatewayAnswer<T>> Refused<T>() => Task.FromResult(GatewayAnswer.Refused<T>("Not under test."));
    }
}
