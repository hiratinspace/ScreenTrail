using ScreenTrail.Core.Audit;
using ScreenTrail.Core.Net;
using ScreenTrail.Core.Settings;
using ScreenTrail.Shared.Ipc;
using ScreenTrail.Shared.Settings;

namespace ScreenTrail.Tests.Settings;

/// <summary>The service's side of Settings → Privacy (ST-081): read with the locks, save and apply, refuse what cannot be saved, export the audit log.</summary>
public sealed class SettingsCommandsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "screentrail-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReadingReportsTheSettingsAndWhatTheAdminLocked()
    {
        var commands = Commands(new TenantPolicy("p1", 3, true, true, false));

        var reported = Assert.IsType<SettingsReported>(await commands.ReplyToAsync(new GetSettingsCommand { RequestId = 1 }));

        Assert.True(reported.LocalOnlyLocked);
        Assert.True(reported.RetentionLocked);
        Assert.Equal("p1", reported.PolicyVersion);
        Assert.True(reported.EffectiveLocalOnly);
        Assert.Equal(3, reported.EffectiveRetentionDays);
        Assert.Equal(7, reported.Settings.RetentionDays); // the technician's own, kept for when the policy lets go
    }

    [Fact]
    public async Task ALockedFieldIsReportedAsTheAdminSetIt()
    {
        // The admin locked local-only off over a technician who had it on: sessions upload, so the
        // screen must not say local-only is on (INV-8, INV-11).
        Save(new ClientSettings { LocalOnly = true, RetentionDays = 7 });
        var commands = Commands(new TenantPolicy("p1", 3, false, true, false));

        var reported = Assert.IsType<SettingsReported>(await commands.ReplyToAsync(new GetSettingsCommand { RequestId = 1 }));

        Assert.True(reported.LocalOnlyLocked);
        Assert.False(reported.EffectiveLocalOnly);
        Assert.Equal(3, reported.EffectiveRetentionDays);
        Assert.True(reported.Settings.LocalOnly);
        Assert.Equal(7, reported.Settings.RetentionDays);
    }

    [Fact]
    public async Task AnUnlockedLocalOnlyUnderAnAdminPolicyIsTheTechniciansOwn()
    {
        Save(new ClientSettings { LocalOnly = true, RetentionDays = 7 });
        var commands = Commands(new TenantPolicy("p2", 90, false, false, false));

        var reported = Assert.IsType<SettingsReported>(await commands.ReplyToAsync(new GetSettingsCommand { RequestId = 1 }));

        Assert.False(reported.LocalOnlyLocked);
        Assert.True(reported.EffectiveLocalOnly);
        Assert.True(reported.RetentionLocked);
        Assert.Equal(90, reported.EffectiveRetentionDays); // beyond the technician's 30, in its own field
    }

    [Fact]
    public async Task WithNoAdminPolicyWhatIsInForceIsTheTechniciansOwn()
    {
        Save(new ClientSettings { LocalOnly = true, RetentionDays = 12 });
        var commands = Commands(TenantPolicy.Default);

        var reported = Assert.IsType<SettingsReported>(await commands.ReplyToAsync(new GetSettingsCommand { RequestId = 1 }));

        Assert.False(reported.LocalOnlyLocked);
        Assert.False(reported.RetentionLocked);
        Assert.True(reported.EffectiveLocalOnly);
        Assert.Equal(12, reported.EffectiveRetentionDays);
    }

    [Fact]
    public async Task ThePrivacyScreenShowsTheAdminsValuesAndStillSavesTheTechniciansOwn()
    {
        // Service to screen and back, through the wire format: a 90-day policy that locked local-only
        // off is shown as it is in force, and a save keeps the technician's own values underneath.
        Save(new ClientSettings { LocalOnly = true, RetentionDays = 7 });
        var commands = Commands(new TenantPolicy("p3", 90, false, true, false));
        var panel = new PrivacyPanel(new CommandsGateway(commands));

        await panel.LoadAsync();

        Assert.False(panel.LocalOnly);
        Assert.Equal(90, panel.RetentionDays);
        Assert.Null(panel.Problem);
        panel.Telemetry = true;
        Assert.True(await panel.SaveAsync());
        var stored = new ClientSettingsStore(Path.Combine(_dir, "settings.json")).Load();
        Assert.True(stored.LocalOnly);
        Assert.Equal(7, stored.RetentionDays);
        Assert.True(stored.Telemetry);
    }

    [Fact]
    public async Task SavingWritesAppliesAndRefusesABadDocument()
    {
        var applied = new List<ClientSettings>();
        var commands = Commands(TenantPolicy.Default, applied.Add);

        var ok = await commands.HandleAsync(new SetSettingsCommand { RequestId = 2, Settings = new ClientSettings { Telemetry = true, ExcludedProcesses = ["keepass"] } });
        var bad = await commands.HandleAsync(new SetSettingsCommand { RequestId = 3, Settings = new ClientSettings { RetentionDays = 99 } });

        Assert.True(ok!.Ok);
        Assert.False(bad!.Ok);
        Assert.Contains("30 days", bad.Error, StringComparison.Ordinal);
        Assert.True(Assert.Single(applied).Telemetry);
        Assert.True(new ClientSettingsStore(Path.Combine(_dir, "settings.json")).Load().Telemetry);
    }

    [Fact]
    public async Task TwoSavesAtOnceLeaveTheFileAndWhatIsInForceTheSame()
    {
        // Two connections can save at the same moment. Each writes the file and then applies, and the
        // service keeps what was applied in memory, so the second save has to wait until the first has
        // done both. Otherwise the file says one thing and the guard enforces the other until the next save.
        ClientSettings? inForce = null;
        Task<CommandResult?>? second = null;
        var calls = 0;
        SettingsCommands? commands = null;
        commands = Commands(TenantPolicy.Default, settings =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                second = Task.Run(() => commands!.HandleAsync(new SetSettingsCommand { RequestId = 7, Settings = new ClientSettings { LocalOnly = true } }), TestContext.Current.CancellationToken);
                _ = second.Wait(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
            }

            inForce = settings;
        });

        var first = await commands.HandleAsync(new SetSettingsCommand { RequestId = 6, Settings = new ClientSettings { LocalOnly = false } }, TestContext.Current.CancellationToken);
        Assert.NotNull(second);
        var then = await second;

        Assert.True(first!.Ok);
        Assert.True(then!.Ok);
        Assert.True(new ClientSettingsStore(Path.Combine(_dir, "settings.json")).Load().LocalOnly);
        Assert.True(inForce!.LocalOnly);
    }

    [Fact]
    public async Task TheAuditLogIsExportedWithItsVerification()
    {
        // Where the file is and what verifying it found. The log itself stays off the pipe (E-2).
        var commands = Commands(TenantPolicy.Default, export: new AuditExportResult("/data/exports/audit.json", 12, new AuditVerification(false, 3, 0, 4), null));

        var exported = Assert.IsType<AuditExported>(await commands.ReplyToAsync(new ExportAuditCommand { RequestId = 4 }));

        Assert.Equal("/data/exports/audit.json", exported.Path);
        Assert.Equal(12, exported.Rows);
        Assert.False(exported.Intact);
        Assert.Contains("row 4 does not follow", exported.Note, StringComparison.Ordinal);
        Assert.Null(await commands.ReplyToAsync(new StartCommand { RequestId = 5 }));
    }

    [Theory]
    [InlineData("ssn off")]
    [InlineData("passwords off")]
    [InlineData("emails on")]
    [InlineData("pattern removed")]
    [InlineData("app removed")]
    [InlineData("local-only off")]
    public async Task ASaveThatChangesMaskingOrWeakensItWithoutAConfirmationIsRefused(string change)
    {
        // DEC-06: every change to a redaction switch needs a token on the pipe, and so does any save that
        // takes a pattern, an excluded app or the technician's own local-only away (A-03 (d)).
        var before = new ClientSettings { LocalOnly = true, CustomPatterns = ["ACME-[0-9]{6}"], ExcludedProcesses = ["acmevault"] };
        Save(before);
        var after = change switch
        {
            "ssn off" => before with { Ssn = false },
            "passwords off" => before with { Passwords = false },
            "emails on" => before with { Emails = true },
            "pattern removed" => before with { CustomPatterns = [] },
            "app removed" => before with { ExcludedProcesses = [] },
            _ => before with { LocalOnly = false },
        };
        var applied = new List<ClientSettings>();
        var commands = Commands(TenantPolicy.Default, applied.Add);

        var refused = await commands.HandleAsync(new SetSettingsCommand { RequestId = 2, Settings = after }, TestContext.Current.CancellationToken);

        Assert.False(refused!.Ok);
        Assert.Empty(applied);
        var stored = new ClientSettingsStore(Path.Combine(_dir, "settings.json")).Load();
        Assert.Equal(before.Ssn, stored.Ssn);
        Assert.Equal(before.Passwords, stored.Passwords);
        Assert.Equal(before.Emails, stored.Emails);
        Assert.Equal(before.CustomPatterns, stored.CustomPatterns);
        Assert.Equal(before.ExcludedProcesses, stored.ExcludedProcesses);
        Assert.True(stored.LocalOnly);
    }

    [Fact]
    public async Task ASaveThatOnlyAddsProtectionNeedsNoConfirmation()
    {
        // Adding a pattern, an excluded app or local-only, or changing telemetry or retention, is saved as before.
        var commands = Commands(TenantPolicy.Default);

        var saved = await commands.HandleAsync(new SetSettingsCommand { RequestId = 2, Settings = new ClientSettings { LocalOnly = true, RetentionDays = 3, Telemetry = true, CustomPatterns = ["ACME-[0-9]{6}"], ExcludedProcesses = ["acmevault"] } }, TestContext.Current.CancellationToken);

        Assert.True(saved!.Ok, saved.Error);
    }

    [Fact]
    public async Task AConfirmedSaveGoesThroughOnceAndOnlyFromTheConnectionThatAsked()
    {
        var commands = Commands(TenantPolicy.Default);
        var window = Guid.NewGuid();
        var issued = commands.Confirm(new RequestConfirmationCommand { RequestId = 1, Action = SettingsCommands.ConfirmAction }, window);
        Assert.NotNull(issued);
        Assert.Matches("^[0-9a-f]{32}$", issued.Token);

        var fromAnother = await commands.HandleAsync(new SetSettingsCommand { RequestId = 2, Settings = new ClientSettings { Passwords = false }, Confirmation = issued.Token }, Guid.NewGuid(), TestContext.Current.CancellationToken);
        var fromTheWindow = await commands.HandleAsync(new SetSettingsCommand { RequestId = 3, Settings = new ClientSettings { Passwords = false }, Confirmation = issued.Token }, window, TestContext.Current.CancellationToken);
        var again = await commands.HandleAsync(new SetSettingsCommand { RequestId = 4, Settings = new ClientSettings { Passwords = true }, Confirmation = issued.Token }, window, TestContext.Current.CancellationToken);

        Assert.False(fromAnother!.Ok);
        Assert.True(fromTheWindow!.Ok, fromTheWindow.Error);
        Assert.False(again!.Ok);
        Assert.False(new ClientSettingsStore(Path.Combine(_dir, "settings.json")).Load().Passwords);
        Assert.Null(commands.Confirm(new RequestConfirmationCommand { RequestId = 5, Action = "erase_everything" }, window));
    }

    [Fact]
    public async Task InAnInstalledCopyTheBuiltInRulesCannotBeSavedOff()
    {
        // DEC-06: until the tenant's policy can set a floor, an installed build keeps the four built-in
        // rules on. Emails, patterns and the rest stay the technician's own.
        var commands = new SettingsCommands(
            new ClientSettingsStore(Path.Combine(_dir, "settings.json")),
            () => TenantPolicy.Default,
            _ => { },
            _ => Task.FromResult(new AuditExportResult("/data/exports/audit.json", 0, new AuditVerification(true, 0, 0, null), null)),
            builtInRulesLocked: true);
        var window = Guid.NewGuid();

        var off = await commands.HandleAsync(new SetSettingsCommand { RequestId = 2, Settings = new ClientSettings { Passwords = false }, Confirmation = commands.Confirm(new RequestConfirmationCommand { RequestId = 1, Action = SettingsCommands.ConfirmAction }, window)!.Token }, window, TestContext.Current.CancellationToken);
        var emails = await commands.HandleAsync(new SetSettingsCommand { RequestId = 4, Settings = new ClientSettings { Emails = true }, Confirmation = commands.Confirm(new RequestConfirmationCommand { RequestId = 3, Action = SettingsCommands.ConfirmAction }, window)!.Token }, window, TestContext.Current.CancellationToken);
        var reported = Assert.IsType<SettingsReported>(await commands.ReplyToAsync(new GetSettingsCommand { RequestId = 5 }, TestContext.Current.CancellationToken));

        Assert.False(off!.Ok);
        Assert.True(emails!.Ok, emails.Error);
        Assert.True(reported.BuiltInRulesLocked);
        Assert.True(reported.Settings.Passwords);
        Assert.True(reported.Settings.Emails);
    }

    private void Save(ClientSettings settings) => Assert.True(new ClientSettingsStore(Path.Combine(_dir, "settings.json")).Save(settings));

    [Fact]
    public async Task AnExportThatCouldNotBeWrittenIsRefusedWithItsReason()
    {
        var commands = Commands(TenantPolicy.Default, export: AuditExportResult.Refused("The audit log could not be written to disk."));

        Assert.Null(await commands.ReplyToAsync(new ExportAuditCommand { RequestId = 6 }));
        var refused = await commands.HandleAsync(new ExportAuditCommand { RequestId = 6 });

        Assert.False(refused!.Ok);
        Assert.Equal("The audit log could not be written to disk.", refused.Error);

        // Said once. The reason is not kept for a later command that happens to reuse the id.
        Assert.Equal("The audit log could not be exported.", (await commands.HandleAsync(new ExportAuditCommand { RequestId = 6 }))!.Error);
    }

    private SettingsCommands Commands(TenantPolicy policy, Action<ClientSettings>? apply = null, AuditExportResult? export = null) => new(
        new ClientSettingsStore(Path.Combine(_dir, "settings.json")),
        () => policy,
        apply ?? (_ => { }),
        _ => Task.FromResult(export ?? new AuditExportResult("/data/exports/audit.json", 0, new AuditVerification(true, 0, 0, null), null)));

    /// <summary>The pipe's gateway without the pipe: each reply goes through the wire framing, as it would between the two processes.</summary>
    private sealed class CommandsGateway(SettingsCommands commands) : IPrivacyGateway
    {
        public async Task<SettingsSnapshot?> LoadAsync(CancellationToken ct = default) =>
            SettingsSnapshot.From(await OverTheWireAsync<SettingsReported>(await commands.ReplyToAsync(new GetSettingsCommand { RequestId = 1 }, ct)));

        public async Task<string?> SaveAsync(ClientSettings settings, CancellationToken ct = default)
        {
            var result = (await commands.HandleAsync(new SetSettingsCommand { RequestId = 2, Settings = settings }, ct))!;
            return result.Ok ? null : result.Error;
        }

        public Task<AuditSaved> ExportAuditAsync(string destination, CancellationToken ct = default) =>
            throw new NotSupportedException("The export is not under test here.");

        private static async Task<T> OverTheWireAsync<T>(IpcEvent? reply)
            where T : IpcEvent
        {
            using var stream = new MemoryStream();
            await IpcFraming.WriteAsync(stream, reply!);
            stream.Position = 0;
            return Assert.IsType<T>(await IpcFraming.ReadAsync<IpcEvent>(stream));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
