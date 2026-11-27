namespace ScreenTrail.Core.Lifecycle;

/// <summary>
/// How the UI process starts, and which copy runs (review findings D-02 and D-03).
///
/// A technician who opens ScreenTrail from Start expects a window. The start at sign-in, and Restart
/// Manager's restart after another program's installer, are not the technician asking: those start in
/// the tray, as every start did before, and say so with <see cref="BackgroundFlag"/>.
///
/// One copy runs per user and session. A second start finds the first holding <see cref="InstanceName"/>,
/// asks it through <see cref="ShowSignalName"/> to show its window when the second was asked for one, and
/// exits: two copies made two tray icons and two pills.
/// </summary>
public static class UiStart
{
    /// <summary>A start nobody asked to see: sign-in, Restart Manager.</summary>
    public const string BackgroundFlag = "--background";

    /// <summary>Whether a start with these arguments opens the window.</summary>
    public static bool OpensWindow(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return !args.Contains(BackgroundFlag, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The Run value that starts the UI at sign-in.</summary>
    public static string SignInCommand(string executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        return $"\"{executable}\" {BackgroundFlag}";
    }

    /// <summary>
    /// The Run value ScreenTrail wrote before the flag: the quoted path alone. Only that exact value is
    /// rewritten, so a value somebody changed by hand is left as it is.
    /// </summary>
    public static bool IsSignInCommandWithoutTheFlag(string? value, string executable) =>
        value is not null && string.Equals(value, $"\"{executable}\"", StringComparison.OrdinalIgnoreCase);

    /// <summary>The mutex the running copy holds. Local: one per Windows session, and the user's SID within it.</summary>
    public static string InstanceName(string userSid) => @"Local\ScreenTrail.UI." + Checked(userSid);

    /// <summary>The event a second start sets to ask the running copy for its window.</summary>
    public static string ShowSignalName(string userSid) => @"Local\ScreenTrail.UI.Show." + Checked(userSid);

    private static string Checked(string userSid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userSid);
        return userSid.Contains('\\', StringComparison.Ordinal) ? throw new ArgumentException("A SID has no backslash.", nameof(userSid)) : userSid;
    }
}
