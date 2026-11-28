using System.Text.RegularExpressions;

namespace ScreenTrail.Tests.Security;

/// <summary>
/// What the customer-facing security documents say about when screenshots leave the device (review
/// finding A-04).
///
/// A finished session is drafted at once: the redacted screenshots, the masked text read from them and the
/// masked transcript are queued as the session ends and sent within the outbox's 30-second tick, before
/// anyone opens Review (<c>SessionMachine.FinalizeAndDraftAsync</c>, <c>BundlingDrafter</c>,
/// <c>CaptureHost.DrainOutboxAsync</c>). Review decides what is published to the ticket. The documents a
/// pilot customer reads must say that, and must not say the technician sees every screenshot before any
/// of it leaves. The words are checked, not paraphrased: a sentence is what a customer holds the product to.
/// </summary>
public sealed partial class DocumentClaimsTests
{
    private static readonly string[] CustomerDocuments = ["data-flow.md", "questionnaire.md", "threat-model.md"];

    [Theory]
    [InlineData("before anything leaves the device")]
    [InlineData("before it goes anywhere")]
    public void NoDocumentSaysReviewComesBeforeTheFirstSend(string claim)
    {
        foreach (var name in CustomerDocuments)
        {
            Assert.DoesNotContain(claim, Text(name), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("data-flow.md")]
    [InlineData("questionnaire.md")]
    public void TheDocumentsSayWhenScreenshotsGoForDrafting(string name)
    {
        var text = Text(name);

        Assert.Contains("when the session ends", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("before the technician opens Review", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Review governs what is published", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The document's text with its line breaks and runs of spaces read as single spaces, so a rewrap does not hide a sentence.</summary>
    private static string Text(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "security", name);
            if (File.Exists(candidate))
            {
                return Spaces().Replace(File.ReadAllText(candidate), " ");
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"docs/security/{name} was not found above the test binary.", name);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
