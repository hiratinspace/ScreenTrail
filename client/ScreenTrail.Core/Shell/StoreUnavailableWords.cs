using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Core.Shell;

/// <summary>
/// What the tray, the pill and the shell say while the service's store will not open (F-10, contract v19).
/// Nothing is being captured, so it starts the way every resting state does; then why, in words a
/// technician can take to whoever looks after the PC.
/// </summary>
public static class StoreUnavailableWords
{
    /// <summary>The state, as the tray's first row, the pill and the shell's status say it.</summary>
    public const string Line = "Not recording: saved sessions cannot be opened";

    /// <summary>The tray row that asks the service to try the store again.</summary>
    public const string RetryText = "&Try opening saved sessions again";

    /// <summary>Why, for the reason the service gave; a reason this build does not know is said plainly.</summary>
    public static string Why(string? reason) => reason switch
    {
        StoreUnavailableReasons.KeyRefused =>
            "Windows would not open ScreenTrail's key for this account, as after a password reset or a move to another PC. Try again once the password is back, or use Delete everything on the Privacy page to start afresh.",
        _ =>
            "ScreenTrail's saved sessions would not open with its key. Try again, or use Delete everything on the Privacy page to start afresh.",
    };
}
