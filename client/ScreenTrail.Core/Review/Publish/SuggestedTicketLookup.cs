namespace ScreenTrail.Core.Review.Publish;

/// <summary>
/// The suggested ticket's lookup, after the pane is on screen (review finding D-19). Opening a session
/// used to wait for it: the ticket number read off the window was searched for at the PSA, over the
/// internet (often 0.5 to 3 s, with no limit), before the pane was returned, so every click on such a
/// session showed the loading card for the PSA's round trip.
///
/// Now the pane opens, and this asks: whether the lookup is allowed at all (never for a local-only session,
/// ScreenTrail.Core.Shell.ReviewOpening), then the PSA, within <see cref="Limit"/>. While the PSA is asked the To row
/// says <see cref="Words"/>; a match is suggested only if the technician has not picked a ticket meanwhile.
/// </summary>
public static class SuggestedTicketLookup
{
    /// <summary>How long the PSA has to answer before the picker is left as it is.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    /// <summary>The To row's words while the PSA is asked.</summary>
    public static string Words(string number) => $"Looking up #{number}";

    /// <summary>The match to suggest, or null: not allowed, not found, too slow, or the pane went away.</summary>
    /// <param name="may">Whether the lookup is allowed (local-only and the tenant's integrations).</param>
    /// <param name="search">The PSA search.</param>
    /// <param name="asking">Called once the PSA is being asked, to show the words.</param>
    /// <param name="limit">The time allowed; <see cref="Limit"/> unless a test says otherwise.</param>
    public static async Task<TicketMatch?> RunAsync(
        string hinted,
        Func<CancellationToken, Task<bool>> may,
        Func<string, CancellationToken, Task<IReadOnlyList<TicketMatch>>> search,
        Action asking,
        CancellationToken ct,
        TimeSpan? limit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hinted);
        ArgumentNullException.ThrowIfNull(may);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(asking);
        using var within = CancellationTokenSource.CreateLinkedTokenSource(ct);
        within.CancelAfter(limit ?? Limit);
        try
        {
            if (!await may(within.Token).ConfigureAwait(true))
            {
                return null;
            }

            asking();
            var matches = await search(hinted, within.Token).ConfigureAwait(true);
            return within.IsCancellationRequested ? null : matches.FirstOrDefault(m => m.Id == hinted);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
