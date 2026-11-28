using System.Globalization;
using ScreenTrail.Api.Data;

namespace ScreenTrail.Api.Tenancy;

/// <summary>
/// The operator's <c>--set-policy &lt;tenant-id&gt; key=value ...</c> (ST-047, ST-113) until the web admin
/// exists. It starts from the tenant's latest policy and changes only the fields it names (security review
/// D-02): a field left out keeps its value, where it used to be written back as its default, so setting
/// retention alone lifted a locked local-only.
///
/// An unknown key, a malformed value or a key given twice is refused and nothing is written. Turning
/// <c>locked</c> or <c>local-only</c> off where it is on is refused unless the arguments also hold
/// <c>--confirm-unlock</c>: devices leave local-only within the hour and drafting resumes.
/// </summary>
public static class PolicyArguments
{
    /// <summary>The operator's word that lifting a local-only lock is meant.</summary>
    public const string ConfirmUnlock = "--confirm-unlock";

    public const string Usage = "--set-policy <tenant-id> [retention=1-365] [local-only=true|false] [locked=true|false] [all-windows=true|false] [update-channel=stable|pilot] [update-channel-locked=true|false] [update-hours=0-168] [update-local-only=true|false] [--confirm-unlock]";

    /// <summary>One field the new row changed, before and after, as the operator typed them.</summary>
    public sealed record Change(string Field, string Before, string After);

    /// <summary>The row written, or null when no field changed and nothing was written.</summary>
    public sealed record Result(Policy? Policy, IReadOnlyList<Change> Changes);

    private static readonly string[] Keys = ["retention", "local-only", "locked", "all-windows", "update-channel", "update-channel-locked", "update-hours", "update-local-only"];

    /// <summary>The arguments after the tenant id that belong to the flag: up to the next other <c>--</c> option.</summary>
    public static IReadOnlyList<string> Of(IEnumerable<string> afterTenant) =>
        [.. afterTenant.TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal) || a == ConfirmUnlock)];

    /// <exception cref="ArgumentException">An argument the flag does not take, or a lock lifted without <see cref="ConfirmUnlock"/>. Nothing is written.</exception>
    public static async Task<Result> SetAsync(ScreenTrailContext db, Guid tenantId, IReadOnlyList<string> arguments, TimeProvider time, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(time);

        var confirmUnlock = false;
        var named = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var argument in arguments)
        {
            if (argument == ConfirmUnlock)
            {
                confirmUnlock = true;
                continue;
            }

            var parts = argument.Split('=', 2);
            var key = parts[0].ToLowerInvariant();
            if (parts.Length != 2 || Array.IndexOf(Keys, key) < 0)
            {
                throw new ArgumentException($"'{argument}' is not a setting this flag takes. Usage: {Usage}", nameof(arguments));
            }

            if (!named.TryAdd(key, parts[1]))
            {
                throw new ArgumentException($"'{key}' is given twice.", nameof(arguments));
            }
        }

        // No policy yet is the defaults a device already has: the same values GET /v1/policy answers.
        var latest = await Policies.LatestAsync(db, tenantId, ct).ConfigureAwait(false);
        var defaults = new Policies.UpdateSettings();
        var retention = latest?.RetentionDays ?? 7;
        var localOnly = latest?.LocalOnly ?? false;
        var locked = latest?.LocalOnlyLocked ?? false;
        var allWindows = latest?.CaptureAllWindows ?? false;
        var channel = latest?.UpdateChannel ?? defaults.Channel;
        var channelLocked = latest?.UpdateChannelLocked ?? defaults.ChannelLocked;
        var hours = latest?.UpdateCheckHours ?? defaults.CheckHours;
        var underLocalOnly = latest?.UpdateChecksUnderLocalOnly ?? defaults.UnderLocalOnly;

        var changes = new List<Change>();
        void Note(string field, object before, object after)
        {
            var b = Text(before);
            var a = Text(after);
            if (b != a)
            {
                changes.Add(new Change(field, b, a));
            }
        }

        foreach (var (key, value) in named)
        {
            switch (key)
            {
                case "retention":
                    var days = Number(key, value, 1, 365);
                    Note(key, retention, days);
                    retention = days;
                    break;
                case "local-only":
                    var lo = Flag(key, value);
                    Note(key, localOnly, lo);
                    localOnly = lo;
                    break;
                case "locked":
                    var lk = Flag(key, value);
                    Note(key, locked, lk);
                    locked = lk;
                    break;
                case "all-windows":
                    var aw = Flag(key, value);
                    Note(key, allWindows, aw);
                    allWindows = aw;
                    break;
                case "update-channel":
                    if (value is not ("stable" or "pilot"))
                    {
                        throw new ArgumentException($"update-channel is stable or pilot, not '{value}'.", nameof(arguments));
                    }

                    Note(key, channel, value);
                    channel = value;
                    break;
                case "update-channel-locked":
                    var ucl = Flag(key, value);
                    Note(key, channelLocked, ucl);
                    channelLocked = ucl;
                    break;
                case "update-hours":
                    var h = Number(key, value, 0, 168);
                    Note(key, hours, h);
                    hours = h;
                    break;
                default:
                    var ulo = Flag(key, value);
                    Note(key, underLocalOnly, ulo);
                    underLocalOnly = ulo;
                    break;
            }
        }

        var lifts = changes.Where(c => c is { Field: "locked" or "local-only", Before: "true", After: "false" }).Select(c => c.Field).ToList();
        if (lifts.Count > 0 && !confirmUnlock)
        {
            throw new ArgumentException(
                $"This turns {string.Join(" and ", lifts)} off: within the hour the tenant's devices leave local-only and send sessions for drafting again. Nothing was written. Add {ConfirmUnlock} if that is meant.",
                nameof(arguments));
        }

        if (changes.Count == 0)
        {
            return new Result(null, changes);
        }

        var set = await Policies.SetAsync(db, tenantId, retention, localOnly, locked, allWindows, new Policies.UpdateSettings(channel, channelLocked, hours, underLocalOnly), time, ct).ConfigureAwait(false);
        return new Result(set, changes);

        int Number(string key, string value, int min, int max) =>
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max
                ? n
                : throw new ArgumentException($"{key} is a whole number from {min} to {max}, not '{value}'.", nameof(arguments));

        bool Flag(string key, string value) =>
            bool.TryParse(value, out var b) ? b : throw new ArgumentException($"{key} is true or false, not '{value}'.", nameof(arguments));
    }

    private static string Text(object value) => value switch
    {
        bool b => b ? "true" : "false",
        int i => i.ToString(CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
