using System.Text.Json;
using ScreenTrail.Core.Audit;
using ScreenTrail.Core.Ipc;
using ScreenTrail.Core.Net;
using ScreenTrail.Core.Speech;
using ScreenTrail.Core.Store;
using ScreenTrail.Shared.Ipc;
using ScreenTrail.Shared.Settings;

namespace ScreenTrail.Core.Settings;

/// <summary>
/// The capture settings on disk beside the store (ST-080). Not a secret. A file that fails the pipe's rules,
/// or that cannot be read, is taken at its strictest (DEC-06): never all windows, and each member that
/// breaks a rule at its shipped default. As for <see cref="ClientSettingsStore"/>, <see cref="AppliedPath"/>
/// keeps the document last saved or applied, for the strict reading and for the start's comparison.
/// </summary>
public sealed class CaptureSettingsStore(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Path => path;

    /// <summary>The copy of the document the service last saved or applied.</summary>
    public string AppliedPath => path + ".applied";

    public CaptureSettings Load() => Read().Settings;

    /// <summary>The document to use, and whether the file was refused for it. No file and no earlier copy is the defaults, as before.</summary>
    public (CaptureSettings Settings, bool Refused) Read()
    {
        if (!File.Exists(path))
        {
            return LoadApplied() is { } earlier ? (CaptureSettingsDocument.Strictest(null, earlier), true) : (new CaptureSettings(), false);
        }

        var found = Parse(path);
        if (found is not null && found.Problems().Count == 0)
        {
            return (found, false);
        }

        return (CaptureSettingsDocument.Strictest(found, LoadApplied()), true);
    }

    /// <summary>The copy last saved or applied, or null when there is none that passes the rules.</summary>
    public CaptureSettings? LoadApplied() =>
        File.Exists(AppliedPath) && Parse(AppliedPath) is { } applied && applied.Problems().Count == 0 ? applied : null;

    /// <summary>Writes the file and the copy beside it. True when the file itself was written.</summary>
    public bool Save(CaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Write(path, settings))
        {
            return false;
        }

        _ = Write(AppliedPath, settings);
        return true;
    }

    /// <summary>Records what the service applied at start, for the next start to compare against.</summary>
    public bool RecordApplied(CaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Write(AppliedPath, settings);
    }

    private static CaptureSettings? Parse(string file)
    {
        try
        {
            return JsonSerializer.Deserialize<CaptureSettings>(File.ReadAllText(file), Json)?.WithDefaultsForNulls();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool Write(string file, CaptureSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(file))!);
            var temporary = file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json));
            File.Move(temporary, file, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>How a capture settings document that crossed a boundary (the file, the pipe) is made whole.</summary>
public static class CaptureSettingsDocument
{
    /// <summary>
    /// The document with every null member read as absent, which is the shipped default: no list of
    /// disabled tools, no hotkey of the technician's own, medium sensitivity. JSON can say null where the
    /// type says a value always exists, and one that reached the hotkey or tool code threw at start, on
    /// every restart. Null and blank entries inside a list or a map are dropped for the same reason. The
    /// members that were given are kept, so one null does not cost the rest of the file.
    /// </summary>
    public static CaptureSettings WithDefaultsForNulls(this CaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var defaults = new CaptureSettings();

        // Read through nullable views: the declared types promise these are never null, and a document
        // that came off a wire has not kept that promise.
        IReadOnlyList<string?>? tools = settings.DisabledTools;
        IReadOnlyDictionary<string, string>? hotkeys = settings.Hotkeys;
        string? sensitivity = settings.Sensitivity;

        var cleanTools = tools is null ? defaults.DisabledTools : [.. tools.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!)];
        var cleanHotkeys = hotkeys is null
            ? defaults.Hotkeys
            : hotkeys.Where(h => !string.IsNullOrWhiteSpace(h.Key) && !string.IsNullOrWhiteSpace(h.Value))
                .ToDictionary(h => h.Key, h => h.Value, StringComparer.Ordinal);

        return settings with
        {
            DisabledTools = cleanTools,
            Hotkeys = cleanHotkeys,
            Sensitivity = string.IsNullOrWhiteSpace(sensitivity) ? defaults.Sensitivity : sensitivity,

            // A blank choice is no choice: Windows' default, as an absent one is.
            MicrophoneId = string.IsNullOrWhiteSpace(settings.MicrophoneId) ? null : settings.MicrophoneId.Trim(),
        };
    }

    /// <summary>
    /// The strictest reading of a capture settings file that failed the rules or could not be read (DEC-06):
    /// what was found, or failing that the copy last applied, with all windows off and each member that
    /// breaks a rule at its shipped default. The result passes <see cref="CaptureSettingsRules.Problems"/>.
    /// </summary>
    public static CaptureSettings Strictest(CaptureSettings? found, CaptureSettings? lastApplied)
    {
        var defaults = new CaptureSettings();
        var start = (found ?? lastApplied ?? defaults).WithDefaultsForNulls();
        var strict = start with
        {
            CaptureAllWindows = false,
            GraceSeconds = start.GraceSeconds is >= CaptureSettings.MinGraceSeconds and <= CaptureSettings.MaxGraceSeconds ? start.GraceSeconds : null,
            Sensitivity = CaptureSettingsRules.Sensitivities.Contains(start.Sensitivity, StringComparer.OrdinalIgnoreCase) ? start.Sensitivity : defaults.Sensitivity,
            SpeechModel = start.SpeechModel is { } model && SpeechModels.All.Any(m => string.Equals(m.Name, model, StringComparison.OrdinalIgnoreCase)) ? model : null,
            MicrophoneId = start.MicrophoneId is { } microphone && microphone.Length <= CaptureSettingsRules.MaxMicrophoneIdLength && !microphone.Any(char.IsControl) ? microphone : null,
        };

        // Hotkeys that do not parse or that clash are the shipped chords, all of them.
        return strict.Problems().Count == 0 ? strict : strict with { Hotkeys = defaults.Hotkeys };
    }
}

/// <summary>
/// The service's side of Settings → Capture (ST-080): the document with the registry's tools, the
/// devices connected and what the policy forces beside it; save, validate and apply in place; and the
/// microphone's live level while the card is on screen (AC3).
///
/// A save that turns all windows on carries a token this class issued a moment ago to the same connection
/// (<see cref="ConfirmAction"/>), as a discard does (INV-5, A-03 (d)): the screen's tick is the UI's, and a
/// single crafted message on the pipe no longer widens capture. Turning it off needs none.
/// </summary>
/// <param name="microphones">The capture devices connected now (<see cref="IMicrophone.Devices"/>), asked each time the card loads.</param>
/// <param name="meter">The level meter, or null where there is none to offer; the meter's requests are then refused.</param>
/// <param name="audit">
/// Where a save that widens or narrows what is captured is recorded, as <c>settings_changed</c> with a
/// fixed label (ST-045, INV-10). The store, in the service.
/// </param>
public sealed class CaptureSettingsCommands(
    CaptureSettingsStore store,
    Func<IReadOnlyList<ToolRow>> tools,
    Func<IReadOnlyList<MicrophoneDevice>> microphones,
    Func<TenantPolicy> policy,
    Func<int> defaultGraceSeconds,
    Action<CaptureSettings> apply,
    MicrophoneMeter? meter = null,
    IAuditLog? audit = null)
{
    /// <summary>The <c>request_confirmation</c> action whose token a capture save carries (contract v17).</summary>
    public const string ConfirmAction = "change_capture_settings";

    /// <summary>What a save that needed a token and had none, or a spent one, is told.</summary>
    public const string UnconfirmedLine = "Capturing every window was not confirmed, so nothing was saved. Save again from Settings.";

    private readonly Confirmations _confirmations = new();

    private readonly CaptureSettingsStore _store = store ?? throw new ArgumentNullException(nameof(store));

    // Save and apply as one step, for the reason SettingsCommands gives: the file and the scope in force
    // must end on the same document when two connections save at once.
    private readonly Lock _saving = new();

    public async Task<IpcEvent?> ReplyToAsync(IpcCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        switch (command)
        {
            case GetCaptureSettingsCommand:
                return new CaptureSettingsReported
                {
                    RequestId = command.RequestId,
                    Settings = _store.Load(),
                    Tools = tools(),
                    Microphones = [.. microphones().Select(m => new MicrophoneRow(m.Id, m.Name, m.IsDefault))],
                    PolicyForcesAllWindows = policy().CaptureAllWindows,
                    DefaultGraceSeconds = defaultGraceSeconds(),
                };

            case GetMicrophoneLevelCommand level when meter is not null:
                {
                    var reading = await meter.ReadAsync(level.DeviceId, ct).ConfigureAwait(false);
                    return new MicrophoneLevelReported
                    {
                        RequestId = command.RequestId,
                        State = reading.State,
                        Level = reading.Level,
                        DeviceId = reading.DeviceId,
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
            ? new ConfirmationIssued { Token = _confirmations.Issue(DestructiveAction.ChangeCaptureSettings, caller), Phrase = "SAVE" }
            : null;
    }

    /// <summary>As <see cref="HandleAsync(IpcCommand, Guid, CancellationToken)"/> from a connection with no token of its own.</summary>
    public Task<CommandResult?> HandleAsync(IpcCommand command, CancellationToken ct = default) => HandleAsync(command, Guid.Empty, ct);

    public async Task<CommandResult?> HandleAsync(IpcCommand command, Guid caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        switch (command)
        {
            case StopMicrophoneLevelCommand:
                if (meter is not null)
                {
                    await meter.StopAsync().ConfigureAwait(false);
                }

                // Nothing open is the outcome asked for, whether or not there was a meter to close.
                return new CommandResult { RequestId = command.RequestId, Ok = true };

            case GetMicrophoneLevelCommand:
                return new CommandResult { RequestId = command.RequestId, Ok = false, Error = "The level meter is not available in this build." };

            default:
                {
                    var saved = Save(command, caller, out var changed);

                    // After the save took, so every row is a change that is in force (ST-045).
                    if (audit is not null)
                    {
                        foreach (var (change, count) in changed)
                        {
                            await audit.RecordAsync(AuditTypes.SettingsChanged, null, count, AuditDetail.Of(change), ct).ConfigureAwait(false);
                        }
                    }

                    return saved;
                }
        }
    }

    private CommandResult? Save(IpcCommand command, Guid caller, out IReadOnlyList<(SettingChange Change, long? Count)> changed)
    {
        changed = [];
        switch (command)
        {
            case SetCaptureSettingsCommand set:
                {
                    // A peer's document is read the way the file is: a null member is the default, not
                    // an exception in the middle of a save. A missing document is refused.
                    if (set.Settings is null)
                    {
                        return new CommandResult { RequestId = command.RequestId, Ok = false, Error = "No capture settings were sent." };
                    }

                    var settings = set.Settings.WithDefaultsForNulls();
                    var problems = settings.Problems();
                    if (problems.Count > 0)
                    {
                        return new CommandResult { RequestId = command.RequestId, Ok = false, Error = problems[0] };
                    }

                    lock (_saving)
                    {
                        var before = _store.Load();

                        // A presented token is spent whether or not it was needed: single use.
                        var confirmed = _confirmations.Spend(set.Confirmation, DestructiveAction.ChangeCaptureSettings, caller);
                        if (settings.CaptureAllWindows && !before.CaptureAllWindows && !confirmed)
                        {
                            return new CommandResult { RequestId = command.RequestId, Ok = false, Error = UnconfirmedLine };
                        }

                        if (!_store.Save(settings))
                        {
                            return new CommandResult { RequestId = command.RequestId, Ok = false, Error = "The settings could not be written to disk." };
                        }

                        apply(settings);
                        changed = SettingChanges.Between(before, settings);
                    }

                    return new CommandResult { RequestId = command.RequestId, Ok = true };
                }

            case GetCaptureSettingsCommand:
                return new CommandResult { RequestId = command.RequestId, Ok = false, Error = "The capture settings could not be read." };

            default:
                return null;
        }
    }
}
