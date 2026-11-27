using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenTrail.Core.Shell;

/// <summary>
/// How ScreenTrail looks, as the technician chose it in Settings, Appearance (ST-070). Windows high
/// contrast is not one of these: it is never chosen here, and it takes over from any of them while it is on.
/// </summary>
public enum ThemeChoice
{
    /// <summary>Follow Windows' "Choose your app mode", and change when it does. The default.</summary>
    System,
    Light,
    Dark,
}

public static class ThemeChoices
{
    /// <summary>Whether <paramref name="choice"/> shows the light palette, given how Windows' app mode is set now.</summary>
    public static bool UsesLight(ThemeChoice choice, bool windowsUsesLight) => choice switch
    {
        ThemeChoice.Light => true,
        ThemeChoice.Dark => false,
        _ => windowsUsesLight,
    };

    /// <summary>A value the enum does not name, from a hand-edited file, is read as <see cref="ThemeChoice.System"/>.</summary>
    public static ThemeChoice Known(ThemeChoice choice) => Enum.IsDefined(choice) ? choice : ThemeChoice.System;
}

/// <param name="Width">Zero means "not set yet", so the shell picks its own size the first time.</param>
public sealed record ShellPreferences
{
    public ShellView View { get; init; } = ShellView.Review;

    /// <summary>
    /// Settings, Appearance (ST-070). Follows Windows unless the technician picked Light or Dark. Files
    /// written before this was a choice carry a "Theme" key that always said Dark, which nobody chose; it
    /// is not read, so those installations follow Windows too.
    /// </summary>
    public ThemeChoice Appearance { get; init; } = ThemeChoice.System;

    public double Width { get; init; }

    public double Height { get; init; }

    public bool Maximised { get; init; }

    /// <summary>
    /// Where the shell window was closed, one per arrangement of monitors, most recent first (review finding
    /// D-04). <see cref="Width"/>, <see cref="Height"/> and <see cref="Maximised"/> were never read or written;
    /// they stay so an older file still parses.
    /// </summary>
    public WindowPlacementList Placements { get; init; } = WindowPlacementList.Empty;

    /// <summary>
    /// Where the HUD sits, so it comes back where the technician left it (Spec §5 S2 "position persists").
    /// </summary>
    public double HudX { get; init; }

    public double HudY { get; init; }

    /// <summary>
    /// Whether Review's timeline panel is open (ST-076, Spec §5 S3 "Alt+T toggles; state persists").
    ///
    /// Unlike the HUD's hidden flag, this one is safe to remember: an open or closed transcript panel
    /// changes nothing about whether capture is indicated, and a technician who works with it open should
    /// not reopen it for every session.
    /// </summary>
    public bool TimelineExpanded { get; init; }

    /// <summary>When the onboarding wizard was finished (ST-083). Null means it runs at the next start; the tray runs it again on request.</summary>
    public DateTimeOffset? OnboardedAt { get; init; }

    /// <summary>
    /// "Start UI at login" as the technician last saved it (review finding C-I7). The installer starts the
    /// UI for every user from the machine's Run key, which a technician cannot change, so the start at
    /// sign-in reads this and ends quietly when it is false. Null, never saved, is on (owner decision 4).
    /// </summary>
    public bool? StartAtSignIn { get; init; }
}

/// <summary>
/// Remembers where the shell was and how it looked (ST-070, Spec §4).
///
/// Written atomically through a temporary file, because the moment this is saved is when the app is
/// closing, which is also when a machine is most likely to be shutting down or losing power. A half-
/// written preferences file that fails to parse would reset a technician's window and theme every time
/// something went wrong, which looks exactly like the app losing their settings at random.
///
/// <b>A file that cannot be read is not an error.</b> Preferences are a convenience; failing to start the
/// UI because a JSON file is corrupt would turn a cosmetic problem into "ScreenTrail will not open" in the
/// middle of a support call. A bad file is replaced by defaults and overwritten on the next save.
///
/// Nothing here is content (INV-10): a window size, a theme, and which of three views was open.
///
/// <b>"Hide the HUD" is deliberately not here.</b> Spec §5 S2 makes it a per-session choice and INV-4
/// allows a tray-only session only where "the user explicitly chose" it. Persisting it would turn one
/// right-click into a standing tray-only mode across reboots that the technician never opted into: a
/// recording with no visible indicator, which is the thing INV-4 exists to prevent. It lives with the
/// session instead, and ends with it.
/// </summary>
public sealed class ShellPreferencesStore(string path)
{
    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Where the UI keeps them: beside the store, under the user's local app data.</summary>
    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScreenTrail",
        "shell.json");

    private readonly Lock _gate = new();

    private ShellPreferences? _cached;

    private DateTime _cachedStamp;

    public string Path => path;

    /// <summary>
    /// The preferences, read from the file only when it has changed since the last read or write (review
    /// finding D-29): the shell asks on many clicks, on the UI thread, and each ask was a read and a parse.
    /// The file's time is still looked at every time, so a file changed or deleted behind the shell (Erase
    /// everything deletes it) is read again rather than written back from memory.
    /// </summary>
    public ShellPreferences Load()
    {
        try
        {
            var stamp = Stamp();
            lock (_gate)
            {
                if (_cached is not null && stamp == _cachedStamp)
                {
                    return _cached;
                }
            }

            var loaded = File.Exists(path)
                ? JsonSerializer.Deserialize<ShellPreferences>(File.ReadAllText(path), Format) ?? new ShellPreferences()
                : new ShellPreferences();
            Remember(loaded, stamp);
            return loaded;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return new ShellPreferences();
        }
    }

    /// <summary>The file's last write, or MinValue when there is none.</summary>
    private DateTime Stamp() => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

    private void Remember(ShellPreferences preferences, DateTime stamp)
    {
        lock (_gate)
        {
            _cached = preferences;
            _cachedStamp = stamp;
        }
    }

    /// <summary>Returns false when the preferences could not be written. Never throws: this is cosmetic.</summary>
    public bool Save(ShellPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        try
        {
            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Written beside the real file and moved over it, so a reader never sees a partial one.
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, Format));
            File.Move(temporary, path, overwrite: true);
            Remember(preferences, Stamp());
            return true;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// Everything a bad path or a bad file can throw.
    ///
    /// The list includes the ones a malformed path raises (a null character, a name too long, a shape
    /// Windows does not support), and not only the ones a failed write does. Those arrive from a roaming
    /// profile or a settings file just as easily as from a bug, and a method whose contract is "returns
    /// false, never throws" has to mean it: this is the one place in the app where failing loudly would
    /// turn a cosmetic problem into "ScreenTrail will not open".
    /// </summary>
    private static bool IsExpected(Exception ex) =>
        ex is JsonException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or System.Security.SecurityException;
}
