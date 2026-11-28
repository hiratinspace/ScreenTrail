using System.Collections.Concurrent;
using ScreenTrail.Core.Audit;
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
/// </summary>
/// <param name="audit">
/// Where a save that changed a setting is recorded, one <c>settings_changed</c> row per setting with its
/// fixed label and never its value (ST-045, INV-10). The store, in the service.
/// </param>
public sealed class SettingsCommands(
    ClientSettingsStore store,
    Func<TenantPolicy> policy,
    Action<ClientSettings> apply,
    Func<CancellationToken, Task<AuditExportResult>> exportAudit,
    IAuditLog? audit = null)
{
    private readonly ClientSettingsStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly Func<TenantPolicy> _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    private readonly Action<ClientSettings> _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    private readonly Func<CancellationToken, Task<AuditExportResult>> _exportAudit = exportAudit ?? throw new ArgumentNullException(nameof(exportAudit));
    private readonly IAuditLog? _audit = audit;
    private readonly ConcurrentDictionary<int, string> _refusals = new();

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

    public async Task<CommandResult?> HandleAsync(IpcCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        switch (command)
        {
            case SetSettingsCommand set:
                {
                    var problems = set.Settings.Problems();
                    if (problems.Count > 0)
                    {
                        return new CommandResult { RequestId = command.RequestId, Ok = false, Error = problems[0] };
                    }

                    IReadOnlyList<(SettingChange Change, long? Count)> changed;
                    lock (_saving)
                    {
                        var before = _store.Load();
                        if (!_store.Save(set.Settings))
                        {
                            return new CommandResult { RequestId = command.RequestId, Ok = false, Error = "The settings could not be written to disk." };
                        }

                        _apply(set.Settings);
                        changed = SettingChanges.Between(before, set.Settings);
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
