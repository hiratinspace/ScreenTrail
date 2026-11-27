using ScreenTrail.Core.Outbox;
using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Core.Notifications;

/// <summary>What pressing the notification does. Spec §6 gives every message at most one.</summary>
public enum NotificationAction
{
    /// <summary>Nothing to press. Used where there is genuinely nothing a technician can do yet.</summary>
    None,

    /// <summary>Open Review for the session the notification is about.</summary>
    Review,

    /// <summary>Open the session, whatever state it is in. The failure paths use this.</summary>
    Open,
}

/// <summary>How a notice is marked: the balloon's icon.</summary>
public enum NotificationLevel
{
    /// <summary>Something to know: a draft is ready, an update is waiting.</summary>
    Info,

    /// <summary>Something that needs a look: a draft not created, a publish not confirmed.</summary>
    Warning,
}

/// <param name="Text">
/// The body: what it is about and what pressing it does ("Select to open it."), each sentence with its
/// full stop (review finding E-14). Shown to a technician between sessions.
/// </param>
/// <param name="Key">
/// What makes this notification the same notification. The service repeats state on every reconnect and
/// on every <c>get_state</c>, and two identical toasts say the thing happened twice.
/// </param>
/// <param name="Title">The event, in a few words ("Draft ready to check"), where Windows puts it in bold.</param>
/// <param name="Level">The balloon's icon.</param>
public sealed record Notification(string Text, NotificationAction Action, string Key, string Title = Notification.DefaultTitle, NotificationLevel Level = NotificationLevel.Info)
{
    /// <summary>The title of a notice that names no event of its own.</summary>
    public const string DefaultTitle = "ScreenTrail";
}

/// <summary>
/// What ScreenTrail says, and when it says nothing (ST-073, Spec §6).
///
/// <b>Silence is the default and the most important case.</b> Spec §4 forbids interrupting a recording,
/// and a toast over a customer's screen during a support call is the one notification that can do real
/// damage: it is visible to the person on the other end, and it arrives at the moment the technician's
/// attention is worth most. Nothing is said while a session is running, including about work left over
/// from the last one.
///
/// The wording is Spec §6's and is asserted verbatim by the tests. That is not pedantry: these are the
/// only words a technician reads while looking at somebody else's machine, and each one names its object
/// and offers the next step because "something went wrong" costs a support call.
/// </summary>
public static class Notices
{
    /// <returns>What to say, or null to say nothing.</returns>
    public static Notification? For(CaptureStateSnapshot? capture, OutboxWaiting waiting = default)
    {
        if (capture is null)
        {
            // The UI has not heard from the service. The HUD says so already; a toast would be noise
            // about a state that usually resolves itself in a second.
            return null;
        }

        // Spec §4: never interrupt recording. Everything below waits until the session is over.
        if (capture.State is CaptureStates.Recording or CaptureStates.Paused
            or CaptureStates.Suppressed or CaptureStates.Finalizing)
        {
            return null;
        }

        return capture.State switch
        {
            // The event in the title, the draft and what a click does in the body (review finding E-14).
            CaptureStates.DraftReady => new Notification(
                Describe(capture) is { } draft ? $"{draft}. {SelectToOpen}" : SelectToOpen,
                NotificationAction.Review,
                $"draft-ready:{capture.SessionId}",
                "Draft ready to check"),

            CaptureStates.DraftFailed => new Notification(
                "Your screenshots and transcript are kept. Select to see why.",
                NotificationAction.Open,
                $"draft-failed:{capture.SessionId}",
                DraftFailure.Headline,
                NotificationLevel.Warning),

            _ => Queued(waiting),
        };
    }

    /// <summary>
    /// What the queue has to say for itself, when there is no session to talk about.
    ///
    /// Work nobody knows the outcome of comes first. The outbox parks such an attempt rather than
    /// repeating it (publishing twice is worse than publishing late), so it is the one state that will
    /// not resolve itself and the one that has to ask for a person.
    /// </summary>
    private static Notification? Queued(OutboxWaiting waiting)
    {
        if (waiting.Uncertain > 0)
        {
            return new Notification(
                "It may already be on the ticket. Check the ticket before publishing again.",
                NotificationAction.Open,
                $"outbox-uncertain:{waiting.Uncertain}",
                "Publish not confirmed",
                NotificationLevel.Warning);
        }

        if (waiting.Drafts > 0)
        {
            return new Notification(
                "Your draft is created when you are back online.",
                NotificationAction.None,
                "outbox-offline",
                "Waiting for a connection");
        }

        return null;
    }

    /// <summary>What pressing a notice that opens something does, said in its body (review finding E-14).</summary>
    public const string SelectToOpen = "Select to open it.";

    /// <summary>
    /// The session in the technician's words: its draft title and the customer it belongs to, joined by a
    /// comma rather than a middle dot ("Printer offline, Acme Dental"); null when the draft has neither.
    /// </summary>
    private static string? Describe(CaptureStateSnapshot capture) =>
        (capture.DraftTitle, capture.DraftCompany) switch
        {
            (not null, not null) => $"{capture.DraftTitle}, {capture.DraftCompany}",
            (not null, null) => capture.DraftTitle,
            (null, not null) => capture.DraftCompany,
            _ => null,
        };
}

/// <summary>
/// Raises each notification once (ST-073).
///
/// The service repeats its state on every reconnect, on every <c>get_state</c> and after every UI
/// restart. Without this, closing and reopening the window would announce a draft that has been ready
/// for an hour, and two identical toasts would say the thing happened twice.
/// </summary>
public sealed class Notifier
{
    private string? _last;

    /// <returns>What to say about this state, or null when it has already been said.</returns>
    public Notification? Observe(CaptureStateSnapshot? capture, OutboxWaiting waiting = default)
    {
        var notice = Notices.For(capture, waiting);
        if (notice is null)
        {
            // Not cleared: a session that goes quiet does not make an old notification new again.
            return null;
        }

        if (notice.Key == _last)
        {
            return null;
        }

        _last = notice.Key;
        return notice;
    }
}
