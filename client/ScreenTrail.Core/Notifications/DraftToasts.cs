using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Core.Notifications;

/// <summary>
/// How long a "Draft ready" toast is left in Windows' notification centre (security review F-09, owner
/// decision DEC-02). Its text names the note and the customer, and Windows keeps it in a store of its own,
/// outside ScreenTrail's encrypted one. So the UI takes it away once its session is no longer the draft
/// waiting (it was opened, published, discarded, or another session began), and clears ScreenTrail's whole
/// history on "Delete everything" and at uninstall. The text and the toast's arguments are unchanged.
/// </summary>
public static class DraftToasts
{
    /// <summary>The start of every "Draft ready" notice's key, and so of its toast's tag.</summary>
    public const string KeyPrefix = "draft-ready:";

    /// <summary>
    /// The tags, of those ScreenTrail's toasts carry now, to remove for what capture is doing: every
    /// "Draft ready" toast but the one for a session the service says is the draft ready now. Nothing while
    /// the service is not answering: what it last said may be out of date, and a toast is not a capture.
    /// </summary>
    /// <param name="shown">The tags of ScreenTrail's toasts in Windows' notification centre.</param>
    /// <param name="known">What capture is doing, as a connected service last said it; null when not connected.</param>
    public static IReadOnlyList<string> ToRemove(IEnumerable<string> shown, CaptureStateSnapshot? known)
    {
        ArgumentNullException.ThrowIfNull(shown);
        if (known is null)
        {
            return [];
        }

        var keep = known is { State: CaptureStates.DraftReady, SessionId: { Length: > 0 } id }
            ? ToastContent.Tag(new Notification(string.Empty, NotificationAction.Review, KeyPrefix + id))
            : null;
        return [.. shown.Where(tag => tag.StartsWith(KeyPrefix, StringComparison.Ordinal) && !string.Equals(tag, keep, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>How long the uninstaller waits for Windows to clear one user's toasts before it goes on.</summary>
    public static readonly TimeSpan LongestClear = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Runs <paramref name="clear"/> on a thread of its own and waits at most <paramref name="wait"/>:
    /// true when it finished, false when it threw or had not finished by then. The uninstaller has a minute
    /// for everything it does, and a notification platform that does not answer must not spend it.
    /// </summary>
    public static bool TryWithin(Action clear, TimeSpan wait)
    {
        ArgumentNullException.ThrowIfNull(clear);
        var done = false;
        var thread = new Thread(() =>
        {
            try
            {
                clear();
                Volatile.Write(ref done, true);
            }
            catch (Exception)
            {
                // Not cleared. What went wrong says nothing a count does not.
            }
        })
        {
            IsBackground = true,
            Name = "ScreenTrail toast history",
        };
        thread.Start();
        return thread.Join(wait) && Volatile.Read(ref done);
    }
}
