using ScreenTrail.Core.Review;
using ScreenTrail.Core.Review.Publish;
using ScreenTrail.Shared.Schema;
using static ScreenTrail.Tests.Review.ReviewFixture;

namespace ScreenTrail.Tests.Review;

/// <summary>
/// "Needs a look" (E-01, DEC-01): every sentence of the note that tells the reader to do something, or
/// holds an address, a number to call or a command, is marked in Review, and a Discussion note, which
/// the customer reads, waits for the technician's tick. The backend's instruction rule stays a hint; this
/// is the control a model cannot word its way past, because a person reads what it marks.
/// </summary>
public sealed class NeedsALookTests
{
    private static readonly string[] ConnectWise = ["connectwise"];

    /// <summary>
    /// Sentences aimed at the reader, in many shapes, as text on a customer's screen could steer a model
    /// into writing them. Hosts are reserved names (RFC 2606 and 6761).
    /// </summary>
    public static TheoryData<string, string> SteeredSentences => new()
    {
        { "bare domain, no scheme", "Go to helpdesk-portal.example/reset and sign in to finish." },
        { "defanged address", "Open hxxps://helpdesk-portal[.]example/reset to finish." },
        { "another scheme", "Fetch the tool from ftp://files.helpdesk-portal.example/tool.exe." },
        { "protocol-relative link in markdown", "[Reset here](//helpdesk-portal.example/reset)" },
        { "UNC path", "Map \\\\files.helpdesk-portal.example\\share and open setup." },
        { "phone number", "Please call 1-800-555-0100 and read them the code on screen." },
        { "email address", "Email the invoice to accounts@helpdesk-portal.example today." },
        { "an installer command", "Use msiexec /i agent.msi /qn on the server." },
        { "a PowerShell command", "Add-MpPreference -ExclusionPath C:\\ on each workstation." },
        { "a boot setting command", "Reboot, then bcdedit /set testsigning on." },
        { "a group membership command", "net localgroup administrators helpdesk /add" },
        { "a shell command", "cmd /c del C:\\backup\\*.bak" },
        { "a security control turned off", "Turn off BitLocker on the laptop before shipping it." },
        { "stop and exclude", "Stop the Defender service and exclude C:\\Temp from scanning." },
        { "enter a code", "Enter the six digit code on the page that opens." },
        { "a verb and a colon", "Install: the remote agent from the shared drive." },
        { "look-alike letter (Cyrillic i)", "D\u0456sable the firewall on every workstation." },
        { "look-alike letter (Cyrillic i), install", "\u0456nstall the remote agent on every workstation." },
        { "another language", "Deaktivieren Sie die Firewall und starten Sie den Agenten." },
        { "a character that does not show", "Dis\u200Bable the firewall on every workstation." },
        { "a plain link", "Go to https://helpdesk-portal.example/reset and sign in." },
        { "a number to call, no verb", "The vendor's line is +1 (800) 555-0100." },
    };

    [Theory]
    [MemberData(nameof(SteeredSentences))]
    public void ASentenceAimedAtTheReaderIsMarked(string what, string text)
    {
        Assert.True(NeedsALook.Reasons(text).Count > 0, $"Not marked ({what}): {text}");
    }

    /// <summary>The way an honest note reads: what was found, what was done, what is true now.</summary>
    [Theory]
    [InlineData("The print spooler was stopped.")]
    [InlineData("Checked the spooler service.")]
    [InlineData("Restarted the Print Spooler service and cleared the queue.")]
    [InlineData("Printing works again.")]
    [InlineData("The password was [REDACTED] and the card ended [REDACTED].")]
    [InlineData("Updated the driver to version 3.2.1 after error 0x80070005.")]
    [InlineData("Installed KB5034441 and rebooted at 10:30.")]
    [InlineData("Ticket #48213 is waiting on the vendor.")]
    [InlineData("The customer confirmed the scanner works.")]
    [InlineData("Found setup.log in the downloads folder.")]
    public void AnHonestSentenceIsNotMarked(string text)
    {
        Assert.Empty(NeedsALook.Reasons(text));
    }

    [Theory]
    [InlineData("Call the number on the screen.", "tells the reader to do something")]
    [InlineData("The office is at portal.example.", "has a web, email or network address")]
    [InlineData("The vendor's line is 800-555-0100.", "has a number to call")]
    [InlineData("The fix was msiexec /x agent.msi.", "has a command")]
    [InlineData("The f\u0456rewall was off.", "mixes letters from different alphabets")]
    [InlineData("Starten Sie den Agenten und die Firewall neu.", "is in another language")]
    public void EachMarkSaysWhyInPlainWords(string text, string reason)
    {
        Assert.Contains(reason, NeedsALook.Reasons(text));
    }

    [Fact]
    public void TheMarksNameWhereEachSentenceIsAndQuoteItAsWritten()
    {
        var note = Draft(Step("Checked the spooler service."), Step("Restarted it. Then call 1-800-555-0100 for help."))
            with
        {
            Problem = "Nothing printed. Visit portal.example to see why.",
            FollowUps = ["Watch the queue for a week.", "Nothing else is open."],
        };

        var marks = NeedsALook.Find(note);

        Assert.Equal(
            [("Problem", "Visit portal.example to see why."), ("Step 2", "Then call 1-800-555-0100 for help."), ("Follow-up 1", "Watch the queue for a week.")],
            marks.Select(mark => (mark.Where, mark.Sentence)));
        Assert.All(marks, mark => Assert.NotEmpty(mark.Reason));
    }

    [Fact]
    public void TheWordingIsPlainAndHasNoDashes()
    {
        var note = Draft() with { Result = "Call 1-800-555-0100 now." };
        var panel = Panel(note);
        panel.NoteIs(note);
        panel.NoteType = NoteType.Discussion;

        foreach (var text in new[] { PublishPanel.NeedsALookFirst, PublishPanel.NeedsALookLabel, panel.NeedsALookSummary!, NeedsALook.Find(note)[0].Reason })
        {
            Assert.DoesNotContain('\u2014', text);
            Assert.DoesNotContain('\u2013', text);
        }

        Assert.Equal("1 sentence needs a look", panel.NeedsALookSummary);
    }

    [Fact]
    public async Task ADiscussionNoteWithAMarkedSentenceWaitsForTheTick()
    {
        var note = Draft() with { FollowUps = ["Call 1-800-555-0100 and read them the code."] };
        var sent = new List<PublishRequest>();
        var panel = Panel(note, sent);
        panel.NoteIs(note);

        panel.NoteType = NoteType.Discussion;
        Assert.True(panel.NeedsALookTick);
        Assert.Equal(PublishPanel.NeedsALookFirst, panel.BlockedBecause);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => panel.PublishAsync(note, []));
        Assert.Empty(sent);

        panel.NeedsALookChecked = true;
        Assert.Null(panel.BlockedBecause);
        _ = await panel.PublishAsync(note, []);
        Assert.Equal(NoteType.Discussion, Assert.Single(sent).NoteType);
    }

    [Fact]
    public void AnInternalNoteIsNotHeldByTheMarks()
    {
        // Internal is the default and the customer does not read it. The marks still show.
        var note = Draft() with { FollowUps = ["Call 1-800-555-0100 and read them the code."] };
        var panel = Panel(note);
        panel.NoteIs(note);

        Assert.Equal(NoteType.Internal, panel.NoteType);
        Assert.False(panel.NeedsALookTick);
        Assert.Null(panel.BlockedBecause);
        Assert.Equal("1 sentence needs a look", panel.NeedsALookSummary);
    }

    [Fact]
    public void ADiscussionPublishWithNoTicketNoteDoesNotWaitForTheTick()
    {
        var note = Draft() with { FollowUps = ["Call 1-800-555-0100 and read them the code."] };
        var panel = Panel(note);
        panel.NoteIs(note);
        panel.NoteType = NoteType.Discussion;

        panel.TicketNote = false;

        Assert.False(panel.NeedsALookTick);
        Assert.Null(panel.BlockedBecause);
    }

    [Fact]
    public void ANoteWithNothingMarkedPublishesAsDiscussionWithoutATick()
    {
        var note = Draft();
        var panel = Panel(note);
        panel.NoteIs(note);
        panel.NoteType = NoteType.Discussion;

        Assert.Null(panel.NeedsALookSummary);
        Assert.False(panel.NeedsALookTick);
        Assert.Null(panel.BlockedBecause);
    }

    [Fact]
    public async Task ATickIsForTheSentencesItWasGivenFor()
    {
        // A sentence marked after the tick was never looked at, so the tick goes and the box asks again.
        var first = Draft() with { FollowUps = ["Call 1-800-555-0100 and read them the code."] };
        var sent = new List<PublishRequest>();
        var panel = Panel(first, sent);
        panel.NoteIs(first);
        panel.NoteType = NoteType.Discussion;
        panel.NeedsALookChecked = true;

        var edited = first with { Result = "Visit portal.example to finish." };
        panel.NoteIs(edited);

        Assert.False(panel.NeedsALookChecked);
        Assert.Equal(PublishPanel.NeedsALookFirst, panel.BlockedBecause);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => panel.PublishAsync(edited, []));
        Assert.Empty(sent);
    }

    [Fact]
    public async Task ANoteSentWithMoreMarksThanWereTickedIsRefused()
    {
        // The note is asked for at the moment of publishing; a shortcut that sends a newer one than the
        // panel was shown still meets the tick.
        var shown = Draft() with { FollowUps = ["Call 1-800-555-0100 and read them the code."] };
        var sent = new List<PublishRequest>();
        var panel = Panel(shown, sent);
        panel.NoteIs(shown);
        panel.NoteType = NoteType.Discussion;
        panel.NeedsALookChecked = true;

        var newer = shown with { Result = "Visit portal.example to finish." };

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => panel.PublishAsync(newer, []));
        Assert.Empty(sent);
    }

    [Fact]
    public void TheDraftAsStoredIsMarkedBeforeAnyEdit()
    {
        var note = Draft() with { FollowUps = ["Call 1-800-555-0100 and read them the code."] };
        var panel = Panel(note);
        panel.NoteType = NoteType.Discussion;

        Assert.Equal(PublishPanel.NeedsALookFirst, panel.BlockedBecause);
    }

    private static PublishPanel Panel(DraftNote note, List<PublishRequest>? sent = null)
    {
        var panel = new PublishPanel(
            Session(draft: note),
            ConnectWise,
            (_, _) => Task.FromResult<IReadOnlyList<TicketMatch>>([]),
            (r, _) =>
            {
                sent?.Add(r);
                return Task.FromResult<IReadOnlyList<DestinationResult>>([.. r.Destinations.Select(d => new DestinationResult(d, true))]);
            });
        panel.KbArticle = false;
        panel.Choose(new TicketMatch("48213", "Printer offline", "Acme Dental"));
        return panel;
    }
}
