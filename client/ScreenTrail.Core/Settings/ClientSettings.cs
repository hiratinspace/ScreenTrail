using System.Text.Json;
using ScreenTrail.Core.Privacy;
using ScreenTrail.Shared.Settings;

namespace ScreenTrail.Core.Settings;

/// <summary>The rules over <see cref="ClientSettings"/> that need the redaction engine, kept here so the shared contracts assembly stays contract-only.</summary>
public static class ClientSettingsRules
{
    /// <summary>
    /// Why a save was refused in an installed build that would switch one of the four built-in rules off
    /// (DEC-06). Until the tenant's policy carries a floor that only a tenant admin can lower, they stay on.
    /// </summary>
    public const string BuiltInRulesLockedLine = "In this copy of ScreenTrail, masking of social security numbers, card numbers, API keys and passwords stays on.";

    /// <summary>Everything wrong with this document, in the words the screen shows. Empty means it can be saved.</summary>
    public static IReadOnlyList<string> Problems(this ClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = settings.WithDefaultsForNulls();
        var problems = new List<string>();
        if (settings.RetentionDays is < ClientSettings.MinRetentionDays or > ClientSettings.MaxRetentionDays)
        {
            problems.Add($"Retention is between {ClientSettings.MinRetentionDays} and {ClientSettings.MaxRetentionDays} days.");
        }

        foreach (var pattern in settings.CustomPatterns)
        {
            if (RedactionPolicy.ValidateCustomPattern(pattern) is { } problem)
            {
                problems.Add($"The pattern \"{pattern}\" cannot be saved: {problem}");
            }
        }

        foreach (var process in settings.ExcludedProcesses)
        {
            if (ClientSettings.ProcessNameProblem(process) is { } problem)
            {
                problems.Add(problem);
            }
        }

        return problems;
    }

    public static RedactionPolicy ToRedactionPolicy(this ClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new RedactionPolicy
        {
            Ssn = settings.Ssn,
            Cards = settings.Cards,
            ApiKeys = settings.ApiKeys,
            Passwords = settings.Passwords,
            Emails = settings.Emails,
            CustomPatterns = settings.CustomPatterns,
        };
    }

    /// <summary>
    /// The document with a null list read as empty and null entries in a list dropped. JSON can say null
    /// where the type promises a list, from the file or from a peer on the pipe, and one that reached the
    /// rules or the applier threw.
    /// </summary>
    public static ClientSettings WithDefaultsForNulls(this ClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Read through nullable views: the declared types promise these are never null, and a document
        // that came off a wire or a disk has not kept that promise.
        IReadOnlyList<string?>? patterns = settings.CustomPatterns;
        IReadOnlyList<string?>? processes = settings.ExcludedProcesses;
        if (patterns is not null && processes is not null && !patterns.Contains(null) && !processes.Contains(null))
        {
            return settings;
        }

        return settings with
        {
            CustomPatterns = patterns is null ? [] : [.. patterns.OfType<string>()],
            ExcludedProcesses = processes is null ? [] : [.. processes.OfType<string>()],
        };
    }

    /// <summary>The four built-in rules on, whatever the document says: an installed build until the tenant floor (DEC-06).</summary>
    public static ClientSettings WithBuiltInRulesOn(this ClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings with { Ssn = true, Cards = true, ApiKeys = true, Passwords = true };
    }

    /// <summary>Whether this document has any of the four built-in rules off.</summary>
    public static bool TurnsABuiltInRuleOff(this ClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return !(settings.Ssn && settings.Cards && settings.ApiKeys && settings.Passwords);
    }

    /// <summary>
    /// Whether going from <paramref name="before"/> to <paramref name="after"/> needs a confirmation token on
    /// the pipe (DEC-06, A-03 (d)): any redaction switch changed, either way, or a custom pattern, an excluded
    /// app or the technician's own local-only taken away. Adding protection needs none.
    /// </summary>
    public static bool NeedsConfirmation(ClientSettings before, ClientSettings after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        before = before.WithDefaultsForNulls();
        after = after.WithDefaultsForNulls();
        return before.Ssn != after.Ssn
            || before.Cards != after.Cards
            || before.ApiKeys != after.ApiKeys
            || before.Passwords != after.Passwords
            || before.Emails != after.Emails
            || (before.LocalOnly && !after.LocalOnly)
            || before.CustomPatterns.Except(after.CustomPatterns, StringComparer.Ordinal).Any()
            || before.ExcludedProcesses.Except(after.ExcludedProcesses, StringComparer.OrdinalIgnoreCase).Any();
    }

    /// <summary>
    /// The strictest reading of a settings file that failed the rules or could not be read (DEC-06), rather
    /// than the defaults: local-only on, every pattern on, emails included, telemetry off, and every custom
    /// pattern and excluded app that is valid in what was found or in the copy last applied. Retention is the
    /// first of the two that is in range, or the shipped seven days. The result passes <see cref="Problems"/>.
    /// </summary>
    /// <param name="found">What the file held, when it parsed; null when it could not be read or parsed.</param>
    /// <param name="lastApplied">The copy the service last saved or applied, when there is a usable one.</param>
    public static ClientSettings Strictest(ClientSettings? found, ClientSettings? lastApplied)
    {
        found = found?.WithDefaultsForNulls();
        lastApplied = lastApplied?.WithDefaultsForNulls();
        var defaults = new ClientSettings();
        int[] retentions = [.. new[] { found?.RetentionDays, lastApplied?.RetentionDays }.OfType<int>()];
        var retention = retentions.Where(d => d is >= ClientSettings.MinRetentionDays and <= ClientSettings.MaxRetentionDays).DefaultIfEmpty(defaults.RetentionDays).First();
        return new ClientSettings
        {
            LocalOnly = true,
            Telemetry = false,
            RetentionDays = retention,
            Ssn = true,
            Cards = true,
            ApiKeys = true,
            Passwords = true,
            Emails = true,
            CustomPatterns =
            [
                .. (lastApplied?.CustomPatterns ?? []).Concat(found?.CustomPatterns ?? [])
                    .Where(p => RedactionPolicy.ValidateCustomPattern(p) is null)
                    .Distinct(StringComparer.Ordinal),
            ],
            ExcludedProcesses =
            [
                .. (lastApplied?.ExcludedProcesses ?? []).Concat(found?.ExcludedProcesses ?? [])
                    .Where(p => ClientSettings.ProcessNameProblem(p) is null)
                    .Distinct(StringComparer.OrdinalIgnoreCase),
            ],
        };
    }
}

/// <summary>What reading the settings file found: the document to use, and whether the file was refused for it.</summary>
/// <param name="Refused">The file failed the rules or could not be read, so <paramref name="Settings"/> is the strictest reading (DEC-06).</param>
public sealed record SettingsRead(ClientSettings Settings, bool Refused);

/// <summary>
/// The settings on disk beside the store. Not a secret. A file that fails the pipe's rules, or that cannot
/// be read, is taken at its strictest (DEC-06): read as the defaults it would lift a local-only, custom
/// patterns and exclusions the technician had chosen, and the first save would write the defaults over them.
///
/// Beside it, <see cref="AppliedPath"/> keeps the document the service last saved or applied. The strict
/// reading keeps the technician's patterns and exclusions from it, and the start compares against it to
/// record a change made to the file while the service was not running (A-03 (c)). Both are plain JSON in
/// the technician's profile: this notices damage and edits, it does not stand against a process running
/// as the technician that rewrites both.
/// </summary>
public sealed class ClientSettingsStore(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Path => path;

    /// <summary>The copy of the document the service last saved or applied.</summary>
    public string AppliedPath => path + ".applied";

    public ClientSettings Load() => Read().Settings;

    /// <summary>
    /// The document to use. No file at all is a first start and the defaults, as before, unless the
    /// service applied one earlier: then the file was removed or cannot be seen, and it is refused.
    /// </summary>
    public SettingsRead Read()
    {
        if (!File.Exists(path))
        {
            return LoadApplied() is { } earlier
                ? new SettingsRead(ClientSettingsRules.Strictest(null, earlier), Refused: true)
                : new SettingsRead(new ClientSettings(), Refused: false);
        }

        var found = Parse(path);
        if (found is not null && found.Problems().Count == 0)
        {
            return new SettingsRead(found, Refused: false);
        }

        return new SettingsRead(ClientSettingsRules.Strictest(found, LoadApplied()), Refused: true);
    }

    /// <summary>The copy last saved or applied, or null when there is none that passes the rules.</summary>
    public ClientSettings? LoadApplied() =>
        File.Exists(AppliedPath) && Parse(AppliedPath) is { } applied && applied.Problems().Count == 0 ? applied : null;

    /// <summary>Writes the file and the copy beside it. True when the file itself was written.</summary>
    public bool Save(ClientSettings settings)
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
    public bool RecordApplied(ClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Write(AppliedPath, settings);
    }

    private static ClientSettings? Parse(string file)
    {
        try
        {
            return JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(file), Json)?.WithDefaultsForNulls();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool Write(string file, ClientSettings settings)
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
