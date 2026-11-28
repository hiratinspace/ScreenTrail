using System.Collections.Concurrent;
using ScreenTrail.Core.Audit;
using ScreenTrail.Core.Ipc;
using ScreenTrail.Core.Net;
using ScreenTrail.Core.Store;
using ScreenTrail.Shared.Ipc;
using ScreenTrail.Shared.Settings;

namespace ScreenTrail.Core.Settings;

/// <summary>
/// The service's side of Settings → Privacy (ST-081): the technician's settings on disk, applied in place
/// when they change, reported with which fields the admin's policy locked and the values in force; and
/// the audit log exported with its verification. Same two entry points as the other command classes.
///
/// The export is written to a file by <see cref="AuditExporter"/> and only its place and verdict are
/// answered: the log outgrows one pipe message within weeks (2026-10-12 review, E-2). A refusal is kept
/// by request id between the two calls, as <c>PublishCommands</c> does, so the result says why.
///
/// A save that changes a redaction switch, either way, or that takes a custom pattern, an excluded app or
/// the technician's own local-only away, carries a token this class issued a moment ago to the same
/// connection (<see cref="ConfirmAction"/>, DEC-06, A-03 (d)), as a discard does. Settings asks for it on
/// every save; a single crafted message on the pipe no longer changes what is masked.
/// </summary>
/// <param name="audit">
/// Where a save that changed a setting is recorded, one <c>settings_changed</c> row per setting with its
/// fixed label and never its value (ST-045, INV-10). The store, in the service.
/// </param>
/// <param name="builtInRulesLocked">
/// An installed build (DEC-06): until the tenant's policy carries a floor that only a tenant admin can
/// lower, the four built-in rules stay on, and a save that switches one off is refused.
/// </param>
public sealed class SettingsCommands(
    ClientSettingsStore store,
    Func<TenantPolicy> policy,
    Action<ClientSettings> apply,
    Func<CancellationToken, Task<AuditExportResult>> exportAudit,
    IAuditLog? audit = null,
    bool builtInRulesLocked = false)
{
    /// <summary>The <c>request_confirmation</c> action whose token a privacy save carries (contract v17).</summary>
    public const string ConfirmAction = "change_settings";

    /// <summary>What a save that needed a token and had none, or a spent one, is told.</summary>
    public const string UnconfirmedLine = "This change to what is masked was not confirmed, so nothing was saved. Save again from Settings.";

    private readonly ClientSettingsStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly Func<TenantPolicy> _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    private readonly Action<ClientSettings> _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    private readonly Func<CancellationToken, Task<AuditExportResult>> _exportAudit = exportAudit ?? throw new ArgumentNullException(nameof(exportAudit));
    private readonly IAuditLog? _audit = audit;
    private readonly ConcurrentDictionary<int, string> _refusals = new();
    private readonly Confirmations _confirmations = new();

    // A save writes the file and then applies, and the service keeps what was applied in memory. Two
    // connections saving at once must not finish with the file saying one thing and the guard the other.
    private readonly Lock _saving = new();

    public async Task<IpcEvent?> ReplyToAsync(IpcCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        switch (command)
        {
            case GetSettingsCommand:
                {
                    // One read, so the locks and what is in force describe the same document. Settings
                    // stays the technician's own, which is what a save sends back; the screen shows the
                    // effective values for the locked fields (INV-8, INV-11).
                    var tenant = _policy();
                    var settings = _store.Load();
                    var applied = PolicyApplication.Resolve(tenant, settings);
                    return new SettingsReported
                    {
                        RequestId = command.RequestId,
                        Settings = settings,
                        LocalOnlyLocked = applied.Enforced,
                        RetentionLocked = applied.RetentionEnforced,
                        PolicyVersion = tenant.Version,
                        EffectiveLocalOnly = applied.LocalOnly,
                        EffectiveRetentionDays = applied.Retention.Days,
                        BuiltInRulesLocked = builtInRulesLocked,
                    };
                }

            case ExportAuditCommand:
                {
                    var export = await _exportAudit(ct).ConfigureAwait(false);
                    if (export is not { Path: { } path, Verification: { } verdict })
                    {
                        _refusals[command.RequestId] = export.Refusal ?? "The audit log could not be exported.";
                        return null;
                    }

                    return new AuditExported
                    {
                        RequestId = command.RequestId,
                        Path = path,
                        Rows = export.Rows,
                        Intact = verdict.Intact,
                        Note = verdict.Describe(),
                    };
                }

            default:
                return null;
        }
    }

    /// <summary>
    /// A token for the next save from this connection, or null when the request is for another action. Good
    /// once, for 30 seconds, only on <paramref name="caller"/>'s connection.
    /// </summary>
    public ConfirmationIssued? Confirm(RequestConfirmationCommand request, Guid caller)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request is { Action: ConfirmAction, SessionIds: null }
            ? new ConfirmationIssued { Token = _confirmations.Issue(DestructiveAction.ChangeSettings, caller), Phrase = "SAVE" }
            : null;
    }

    /// <summary>As <see cref="HandleAsync(IpcCommand, Guid, CancellationToken)"/> from a connection with no token of its own.</summary>
    public Task<CommandResult?> HandleAsync(IpcCommand command, CancellationToken ct = default) => HandleAsync(command, Guid.Empty, ct);

    public async Task<CommandResult?> HandleAsync(IpcCommand command, Guid caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        switch (command)
        {
            case SetSettingsCommand set:
                {
                    // A peer's document is read the way the file is: a null list is empty, not an
                    // exception in the middle of a save. A missing document is refused.
                    if (set.Settings is null)
                    {
                        return new CommandResult { RequestId = command.RequestId, Ok = false, Error = "No settings were sent." };
                    }

                    var settings = set.Settings.WithDefaultsForNulls();
                    var problems = settings.Problems();
                    if (problems.Count > 0)
                    {
                        return new CommandResult { RequestId = command.RequestId, Ok = false, Error = problems[0] };
                    }

                    if (builtInRulesLocked && settings.TurnsABuiltInRuleOff())
                    {
                        return new CommandResult { RequestId = command.RequestId, Ok = false, Error = ClientSettingsRules.BuiltInRulesLockedLine };
                    }

                    IReadOnlyList<(SettingChange Change, long? Count)> changed;
                    lock (_saving)
                    {
                        var before = _store.Load();

                        // A presented token is spent whether or not it was needed: single use.
                        var confirmed = _confirmations.Spend(set.Confirmation, DestructiveAction.ChangeSettings, caller);
                        if (ClientSettingsRules.NeedsConfirmation(before, settings) && !confirmed)
                        {
                            return new CommandResult { RequestId = command.RequestId, Ok = false, Error = UnconfirmedLine };
                        }

                        if (!_store.Save(settings))
                        {
                            return new CommandResult { RequestId = command.RequestId, Ok = false, Error = "The settings could not be written to disk." };
                        }

                        _apply(settings);
                        changed = SettingChanges.Between(before, settings);
                    }

                    // After the save took, so every row is a change that is in force (ST-045).
                    if (_audit is not null)
                    {
                        foreach (var (change, count) in changed)
                        {
                            await _audit.RecordAsync(AuditTypes.SettingsChanged, null, count, AuditDetail.Of(change), ct).ConfigureAwait(false);
                        }
                    }

                    return new CommandResult { RequestId = command.RequestId, Ok = true };
                }

            case GetSettingsCommand:
                return new CommandResult { RequestId = command.RequestId, Ok = false, Error = "The settings could not be read." };

            case ExportAuditCommand:
                return new CommandResult
                {
                    RequestId = command.RequestId,
                    Ok = false,
                    Error = _refusals.TryRemove(command.RequestId, out var reason) ? reason : "The audit log could not be exported.",
                };

            default:
                return null;
        }
    }
}
