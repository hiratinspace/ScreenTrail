using ScreenTrail.Core.Hud;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Core.Shell;

/// <summary>
/// The one set of words for a privacy pause (INV-6; review finding E-02, design system 10.10). The pill,
/// the tray and the shell's status chip say <see cref="Live"/> while it lasts; the filmstrip's gap and the
/// timeline say <see cref="Past"/> afterwards. They used to name the same event five ways: a sign-in
/// screen was a "sensitive field", every reason was a "sensitive window" in the tray, and an excluded app
/// was a pause on the pill and "not capturing" on the timeline. "Elevated" is an admin's word, not a
/// technician's: it is an admin window.
/// </summary>
public static class SuppressionWords
{
    public const string PasswordField = "Paused: password field";
    public const string SignInScreen = "Paused: sign-in screen";
    public const string ExcludedApp = "Paused: excluded app";
    public const string AdminWindow = "Paused: admin window";
    public const string Paused = "Paused";

    /// <summary>
    /// The words while the pause lasts, from the service's reason (<see cref="CaptureReasons"/>). Out of
    /// scope is not a pause: it is "Not capturing" with the app, and never without one where it is known.
    /// </summary>
    public static string Live(string? reason, string? process = null) => reason switch
    {
        CaptureReasons.PasswordField => PasswordField,
        CaptureReasons.SensitiveContext => SignInScreen,
        CaptureReasons.ExcludedApp => process is { Length: > 0 } app ? $"{ExcludedApp} ({ProcessNames.Friendly(app)})" : ExcludedApp,
        CaptureReasons.ElevatedWindow => AdminWindow,
        CaptureReasons.OutOfScope => process is { Length: > 0 } app ? $"Not capturing: {ProcessNames.Friendly(app)}" : "Not capturing: out of scope",
        _ => Paused,
    };

    /// <summary>The words once it is over, on the filmstrip's gap and the timeline: the same reasons, in the past.</summary>
    public static string Past(CaptureStateReason? reason) => reason switch
    {
        CaptureStateReason.PasswordField => "Paused: a password field had focus",
        CaptureStateReason.SensitiveContext => "Paused: a sign-in screen was in front",
        CaptureStateReason.ExcludedApp => "Paused: an excluded app was in front",
        CaptureStateReason.ElevatedWindow => "Paused: an admin window was in front",
        CaptureStateReason.OutOfScope => "Not capturing: the window was not part of the session",
        CaptureStateReason.User => "Paused by you",
        _ => Paused,
    };
}
