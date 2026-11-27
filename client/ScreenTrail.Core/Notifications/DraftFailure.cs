namespace ScreenTrail.Core.Notifications;

/// <summary>
/// What Review shows when the note could not be written (ST-073, Spec §5 S3).
///
/// <b>The session is still there, and saying so is the whole job.</b> A technician comes back to a
/// failed draft having spent twenty minutes fixing something on a customer's machine. The screenshots,
/// the click timeline and what they said out loud are all intact and are most of what they came for. An
/// error where the work used to be would send them to write the note from memory, which is the outcome
/// this product exists to prevent, so every flag below is true, in every failure, without exception.
/// </summary>
/// <param name="Reason">The service's own words about what went wrong. Plain language, never a stack trace.</param>
/// <param name="CanRetryNow">
/// Whether pressing Retry could work. False for a deployment with no provider configured, where every
/// retry fails in the same millisecond.
/// </param>
public sealed record DraftFailure(string Reason, bool CanRetryNow)
{
    /// <summary>
    /// The heading, in the product's voice (review finding E-10): no "We", like every other message. The
    /// note pane, the balloon and the receipt all say it this way.
    /// </summary>
    public const string Headline = "This session could not be drafted";

    /// <summary>The part that matters (Spec §6): said under the reason, always.</summary>
    public const string Kept = "Your screenshots and transcript are kept.";

    /// <summary>What a deployment with no drafting set up is told, in place of the service's words.</summary>
    public const string NotSetUpReason = "Drafting is not set up for your company yet. Ask your ScreenTrail admin.";

    /// <summary>
    /// What the service says when the deployment has no model provider.
    ///
    /// Matched as a prefix rather than compared whole: the sentence may gain detail, and a technician
    /// should not get a Retry button back because somebody appended a clause to it.
    /// </summary>
    private const string NoProvider = "No summarization provider is configured";

    /// <summary>Waiting for a network rather than having failed. Spec §5 S3's offline case.</summary>
    public static DraftFailure Offline { get; } = new("The draft is waiting for a network.", CanRetryNow: false)
    {
        IsOffline = true,
    };

    /// <summary>True when nothing has failed yet and the work is queued (ST-064).</summary>
    public bool IsOffline { get; init; }

    /// <summary>The deployment has no drafting provider, so no retry and no wait will help.</summary>
    public bool IsNotSetUp => Reason.StartsWith(NoProvider, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The reason in plain words, for the line under the heading (review finding E-10): the service's own
    /// sentence, except that no provider becomes who can fix it. Null when the service gave none, so the
    /// pane does not say "The draft could not be created" under "could not be drafted".
    /// </summary>
    public string? Plain => IsNotSetUp ? NotSetUpReason : Reason == Unknown ? null : Reason;

    private const string Unknown = "The draft could not be created.";

    // These three are unconditional, which is the property worth keeping rather than the one worth
    // optimising away. Making them static to satisfy CA1822 would move the rule off the failure it
    // belongs to and make the next person wonder whether some failure is different. None is.
#pragma warning disable CA1822

    /// <summary>Always. The timeline is not the draft, and it did not fail.</summary>
    public bool ShowTimeline => true;

    /// <summary>Always. Redacted frames are readable whether or not a note was written (INV-1).</summary>
    public bool ShowFrames => true;

    /// <summary>Always. What the technician said is the half of the session a note most needs.</summary>
    public bool ShowTranscript => true;
#pragma warning restore CA1822

    /// <summary>
    /// Why Publish is disabled, shown beside the button rather than in its tooltip.
    ///
    /// Spec v0.4.3 moved it out of the tooltip because a tooltip on a disabled control is the one place a
    /// keyboard user can never reach it, and changed the offline wording because telling somebody to
    /// retry something that cannot succeed makes them think the problem is theirs.
    /// </summary>
    /// <remarks>
    /// Review finding E-10: it used to blame the network whenever Retry could not help, which a deployment
    /// with no provider, or a failure with no reason, has nothing to do with.
    /// </remarks>
    public string PublishDisabledReason =>
        IsOffline ? "There is no note to publish yet. It will be drafted when you are back online."
        : IsNotSetUp ? "There is no note to publish yet. Ask your ScreenTrail admin to set up drafting."
        : CanRetryNow ? "There is no note to publish yet. Try drafting again first."
        : "There is no note to publish yet. This session could not be drafted.";

    public static DraftFailure For(string? reason) =>
        new(string.IsNullOrWhiteSpace(reason) ? Unknown : reason, CanRetry(reason));

    /// <summary>
    /// Whether retrying could produce a different answer.
    ///
    /// A deployment with no provider will refuse every time, so offering Retry there is Spec v0.4.3's
    /// mistake in a new place: a button that cannot help, in front of somebody who will press it twice
    /// and then ring support.
    /// </summary>
    public static bool CanRetry(string? reason) =>
        !string.IsNullOrWhiteSpace(reason)
        && !reason.StartsWith(NoProvider, StringComparison.OrdinalIgnoreCase);
}
