using ScreenTrail.Core.Review.Publish;

namespace ScreenTrail.Tests.Review;

/// <summary>Review finding D-19: the suggested ticket is looked up after the pane opens, within a limit.</summary>
public sealed class SuggestedTicketLookupTests
{
    private static readonly TicketMatch Match = new("48213", "Printer offline", "Acme");

    [Fact]
    public async Task AMatchIsFoundAndTheWordsWereShownWhileAsking()
    {
        var asked = 0;
        var found = await SuggestedTicketLookup.RunAsync("48213", _ => Task.FromResult(true), (_, _) => Task.FromResult<IReadOnlyList<TicketMatch>>([new("1", "x", "y"), Match]), () => asked++, CancellationToken.None);
        Assert.Equal(Match, found);
        Assert.Equal(1, asked);
        Assert.Equal("Looking up #48213", SuggestedTicketLookup.Words("48213"));
    }

    [Fact]
    public async Task NotAllowedAsksNothingAndSaysNothing()
    {
        var searched = 0;
        var asked = 0;
        var found = await SuggestedTicketLookup.RunAsync("48213", _ => Task.FromResult(false), (_, _) => { searched++; return Task.FromResult<IReadOnlyList<TicketMatch>>([Match]); }, () => asked++, CancellationToken.None);
        Assert.Null(found);
        Assert.Equal(0, searched);
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task ANumberThePsaDoesNotKnowIsNotSuggested()
    {
        var found = await SuggestedTicketLookup.RunAsync("48213", _ => Task.FromResult(true), (_, _) => Task.FromResult<IReadOnlyList<TicketMatch>>([new("482130", "x", "y")]), () => { }, CancellationToken.None);
        Assert.Null(found);
    }

    [Fact]
    public async Task ASlowPsaLeavesThePickerAsItIs()
    {
        var found = await SuggestedTicketLookup.RunAsync(
            "48213",
            _ => Task.FromResult(true),
            async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return [Match];
            },
            () => { },
            CancellationToken.None,
            TimeSpan.FromMilliseconds(50));
        Assert.Null(found);
    }

    [Fact]
    public async Task APaneThatWentAwayStopsTheLookup()
    {
        using var gone = new CancellationTokenSource();
        await gone.CancelAsync();
        var found = await SuggestedTicketLookup.RunAsync("48213", ct => Task.FromCanceled<bool>(ct), (_, _) => Task.FromResult<IReadOnlyList<TicketMatch>>([Match]), () => { }, gone.Token);
        Assert.Null(found);
    }
}
