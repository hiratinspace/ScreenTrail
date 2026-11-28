using ScreenTrail.Shared.Settings;

namespace ScreenTrail.Core.Audit;

/// <summary>Who began or ended a session, as the detail of <c>session_started</c> and <c>session_stopped</c>.</summary>
public enum SessionCause
{
    /// <summary>The technician: the tray, a hotkey, the pipe's start or stop.</summary>
    Technician,

    /// <summary>The service: a remote tool came to the front, or the technician left it for longer than the grace.</summary>
    Automatic,

    /// <summary>The service at start, finishing a session a crash or a shutdown left recording.</summary>
    Recovered,
}

/// <summary>
/// Which setting a save changed, as the detail of <c>settings_changed</c> (INV-10). A name and, for the
/// switches, the direction; never the value. A custom pattern or an excluded program's name is the
/// technician's own text and stays in the settings file: the row says only that the list changed and how
/// long it is now.
/// </summary>
public enum SettingChange
{
    LocalOnlyOn,
    LocalOnlyOff,
    TelemetryOn,
    TelemetryOff,

    /// <summary><c>count</c> is the days kept now.</summary>
    Retention,

    SsnOn,
    SsnOff,
    CardsOn,
    CardsOff,
    ApiKeysOn,
    ApiKeysOff,
    PasswordsOn,
    PasswordsOff,
    EmailsOn,
    EmailsOff,

    /// <summary><c>count</c> is how many custom patterns there are now.</summary>
    CustomPatterns,

    /// <summary><c>count</c> is how many programs are excluded now.</summary>
    ExcludedApps,

    AllWindowsOn,
    AllWindowsOff,
}

/// <summary>
/// Why the connection reporting the recording indicator changed, as the detail of
/// <c>indicator_source_changed</c> (security review B-03). A label only: never a process, an id or a path.
/// </summary>
public enum IndicatorSourceChange
{
    /// <summary>The connection that reported before has closed, as a UI that restarted leaves it.</summary>
    AfterTheLastClosed,

    /// <summary>The connection that reported before is still open and has stopped reporting.</summary>
    AfterTheLastLapsed,
}

/// <summary>What a settings save changed, for the audit log: one entry per setting whose value is new.</summary>
public static class SettingChanges
{
    public static IReadOnlyList<(SettingChange Change, long? Count)> Between(ClientSettings before, ClientSettings after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var changes = new List<(SettingChange, long?)>();
        Switch(changes, before.LocalOnly, after.LocalOnly, SettingChange.LocalOnlyOn, SettingChange.LocalOnlyOff);
        Switch(changes, before.Telemetry, after.Telemetry, SettingChange.TelemetryOn, SettingChange.TelemetryOff);
        if (before.RetentionDays != after.RetentionDays)
        {
            changes.Add((SettingChange.Retention, after.RetentionDays));
        }

        Switch(changes, before.Ssn, after.Ssn, SettingChange.SsnOn, SettingChange.SsnOff);
        Switch(changes, before.Cards, after.Cards, SettingChange.CardsOn, SettingChange.CardsOff);
        Switch(changes, before.ApiKeys, after.ApiKeys, SettingChange.ApiKeysOn, SettingChange.ApiKeysOff);
        Switch(changes, before.Passwords, after.Passwords, SettingChange.PasswordsOn, SettingChange.PasswordsOff);
        Switch(changes, before.Emails, after.Emails, SettingChange.EmailsOn, SettingChange.EmailsOff);
        List(changes, before.CustomPatterns, after.CustomPatterns, SettingChange.CustomPatterns, StringComparer.Ordinal);
        List(changes, before.ExcludedProcesses, after.ExcludedProcesses, SettingChange.ExcludedApps, StringComparer.OrdinalIgnoreCase);
        return changes;
    }

    public static IReadOnlyList<(SettingChange Change, long? Count)> Between(CaptureSettings before, CaptureSettings after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var changes = new List<(SettingChange, long?)>();
        Switch(changes, before.CaptureAllWindows, after.CaptureAllWindows, SettingChange.AllWindowsOn, SettingChange.AllWindowsOff);
        return changes;
    }

    private static void Switch(List<(SettingChange, long?)> changes, bool before, bool after, SettingChange on, SettingChange off)
    {
        if (before != after)
        {
            changes.Add((after ? on : off, null));
        }
    }

    private static void List(List<(SettingChange, long?)> changes, IReadOnlyList<string>? before, IReadOnlyList<string>? after, SettingChange change, StringComparer comparer)
    {
        // Null reads as empty: a document from a wire can say null where the type promises a list.
        var was = before ?? [];
        var now = after ?? [];
        if (!was.SequenceEqual(now, comparer))
        {
            changes.Add((change, now.Count));
        }
    }
}
