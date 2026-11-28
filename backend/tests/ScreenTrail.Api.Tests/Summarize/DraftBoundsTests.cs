using System.Text.Json.Nodes;
using ScreenTrail.Api.Summarize;

namespace ScreenTrail.Api.Tests.Summarize;

/// <summary>
/// The numbers <c>research/prompts/schema.json</c> puts on a draft: how long each text may be, how many
/// steps, follow-ups and references it may hold, and the minutes between 0 and a day. Claude reads them
/// only as description text and Gemini is sent no schema, so the validator is the one place that holds
/// a draft to them. A draft past one is refused like any other broken draft (one repair, then the device
/// drafts), before it reaches the client that stores and shows it.
/// </summary>
public sealed class DraftBoundsTests
{
    [Fact]
    public void NegativeMinutesAreRefused() =>
        Assert.NotEmpty(DraftValidator.Check(Draft() with { SuggestedTimeMinutes = -5 }, Bundle()));

    [Fact]
    public void MinutesAreBoundedWhenTheSessionHasNoDuration() =>
        Assert.NotEmpty(DraftValidator.Check(Draft() with { SuggestedTimeMinutes = long.MaxValue }, Bundle() with { DurationMs = null }));

    [Fact]
    public void MinutesAreBoundedWhenTheSessionDurationIsZero() =>
        Assert.NotEmpty(DraftValidator.Check(Draft() with { SuggestedTimeMinutes = 1440 }, Bundle() with { DurationMs = 0 }));

    [Fact]
    public void ATitleLongerThanTheSchemaAllowsIsRefused() =>
        Assert.NotEmpty(DraftValidator.Check(Draft() with { SuggestedTitle = new string('a', 5_000) }, Bundle()));

    [Fact]
    public void MoreStepsThanTheSchemaAllowsAreRefused()
    {
        var steps = Enumerable.Range(0, 500).Select(_ => Step("Found the Print Spooler service stopped.")).ToArray();
        Assert.NotEmpty(DraftValidator.Check(Draft() with { Steps = steps }, Bundle()));
    }

    [Fact]
    public void AStepLongerThanTheSchemaAllowsIsRefused() =>
        Assert.NotEmpty(DraftValidator.Check(Draft() with { Steps = [Step(string.Join(' ', Enumerable.Repeat("spooler", 20_000)))] }, Bundle()));

    public static TheoryData<string, DraftJson> OnePastEachBound => new()
    {
        { "problem", Draft() with { Problem = new string('a', 601) } },
        { "result", Draft() with { Result = new string('a', 601) } },
        { "suggested_title", Draft() with { SuggestedTitle = new string('a', 71) } },
        { "kb_reason", Draft() with { KbReason = new string('a', 401) } },
        { "follow_ups", Draft() with { FollowUps = [.. Enumerable.Repeat("Check the driver version.", 11)] } },
        { "follow_ups item", Draft() with { FollowUps = [new string('a', 401)] } },
        { "steps", Draft() with { Steps = [.. Enumerable.Range(0, 31).Select(_ => Step("Found the Print Spooler service stopped."))] } },
        { "step text", Draft() with { Steps = [Step(new string('a', 401))] } },
        { "frame_refs", Draft() with { Steps = [Step("Found it stopped.") with { FrameRefs = [.. Enumerable.Repeat("f1", 11)] }] } },
        { "transcript_refs", Draft() with { Steps = [Step("Found it stopped.") with { TranscriptRefs = [.. Enumerable.Repeat("t1", 11)] }] } },
        { "minutes", Draft() with { SuggestedTimeMinutes = 1441 } },
    };

    [Theory]
    [MemberData(nameof(OnePastEachBound))]
    public void OnePastAnyBoundIsRefusedAndTheReasonNamesTheFieldNotTheValue(string field, DraftJson draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var reasons = DraftValidator.Check(draft, Bundle() with { DurationMs = null });

        Assert.NotEmpty(reasons);
        Assert.All(reasons, reason => Assert.DoesNotContain("aaaa", reason, StringComparison.Ordinal));
        Assert.Contains(reasons, reason => reason.Contains(field.Split(' ')[0], StringComparison.Ordinal));
    }

    [Fact]
    public void ADraftExactlyAtEveryBoundStillPasses()
    {
        // What a model may write must still be stored. Lengths count characters as the schema and the
        // Python checks do, so a title of 70 emoji is 70, not the 140 UTF-16 units .NET would count.
        var draft = Draft() with
        {
            Problem = Words(600),
            Result = Words(600),
            SuggestedTitle = string.Concat(Enumerable.Repeat("\U0001F5A8", 70)),
            KbReason = Words(400),
            FollowUps = [.. Enumerable.Repeat(Words(400), 10)],
            Steps = [.. Enumerable.Range(0, 30).Select(_ => Step(Words(400)) with
            {
                FrameRefs = [.. Enumerable.Repeat("f1", 10)],
                TranscriptRefs = [.. Enumerable.Repeat("t1", 10)],
            })],
            SuggestedTimeMinutes = 1440,
        };

        Assert.Empty(DraftValidator.Check(draft, Bundle() with { DurationMs = null }));
        Assert.Empty(DraftValidator.Check(draft with { SuggestedTimeMinutes = 0 }, Bundle()));
        Assert.Empty(DraftValidator.Check(draft with { SuggestedTimeMinutes = 21 }, Bundle()));
    }

    [Fact]
    public void TheBoundsAreTheSchemasOwn()
    {
        // The validator's numbers and the schema's are one set. If the schema changes, this fails until
        // DraftValidator.Bounds follows it.
        using var stream = typeof(DraftValidator).Assembly.GetManifestResourceStream("ScreenTrail.Api.Prompts.schema.json")
            ?? throw new InvalidOperationException("schema.json is not embedded in the API.");
        var schema = JsonNode.Parse(stream)!;
        var properties = schema["properties"]!;
        var step = schema["$defs"]!["Step"]!["properties"]!;

        Assert.Equal(DraftValidator.Bounds.Problem, (int)properties["problem"]!["maxLength"]!);
        Assert.Equal(DraftValidator.Bounds.Result, (int)properties["result"]!["maxLength"]!);
        Assert.Equal(DraftValidator.Bounds.Title, (int)properties["suggested_title"]!["maxLength"]!);
        Assert.Equal(DraftValidator.Bounds.KbReason, (int)properties["kb_reason"]!["maxLength"]!);
        Assert.Equal(DraftValidator.Bounds.FollowUps, (int)properties["follow_ups"]!["maxItems"]!);
        Assert.Equal(DraftValidator.Bounds.FollowUp, (int)properties["follow_ups"]!["items"]!["maxLength"]!);
        Assert.Equal(DraftValidator.Bounds.Steps, (int)properties["steps"]!["maxItems"]!);
        Assert.Equal(DraftValidator.Bounds.StepText, (int)step["text"]!["maxLength"]!);
        Assert.Equal(DraftValidator.Bounds.FrameRefs, (int)step["frame_refs"]!["maxItems"]!);
        Assert.Equal(DraftValidator.Bounds.TranscriptRefs, (int)step["transcript_refs"]!["maxItems"]!);
        Assert.Equal(DraftValidator.Bounds.MinutesAtLeast, (int)properties["suggested_time_minutes"]!["minimum"]!);
        Assert.Equal(DraftValidator.Bounds.MinutesAtMost, (int)properties["suggested_time_minutes"]!["maximum"]!);
    }

    /// <summary>Ordinary words, exactly <paramref name="length"/> characters, that no rule refuses.</summary>
    private static string Words(int length) =>
        string.Concat(Enumerable.Repeat("spooler ", (length / 8) + 1))[..length];

    private static DraftJson Draft() => new()
    {
        Problem = "Nothing would print from the reception workstation.",
        Steps = [Step("Found the Print Spooler service stopped.")],
        Result = "Printing works again.",
        FollowUps = ["Check the driver version if it stops again."],
        SuggestedTitle = "Printer offline: print spooler stopped",
        SuggestedTimeMinutes = 10,
        KbCandidate = true,
        KbReason = "A common fix worth writing down.",
        Source = "cloud",
        PromptVersion = "note_v2",
    };

    private static DraftStepJson Step(string text) => new()
    {
        Text = text,
        Confidence = "high",
        FrameRefs = ["f1"],
        TranscriptRefs = ["t1"],
    };

    private static SummarizeBundle Bundle() => new()
    {
        SessionId = "s1",
        DurationMs = 20 * 60 * 1000,
        Frames = [new BundleFrame("f1", 1_000, "Services Print Spooler Stopped", Excluded: false)],
        Transcript = [new BundleSegment("t1", 1_200, "clearing the queue now")],
    };
}
