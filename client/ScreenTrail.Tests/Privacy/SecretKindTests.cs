using System.Diagnostics;
using ScreenTrail.Core.Privacy;

namespace ScreenTrail.Tests.Privacy;

/// <summary>
/// K2 (with A-01), DEC-08: each kind in <see cref="SecretKinds"/> is masked or left as its row promises, in
/// the text a transcript or a frame's OCR keeps and on the screenshot, under the default policy and with
/// the label rules on. A documented miss is held to being a miss too, so the documents that list them
/// stay true: a rule that starts to mask one moves its row, and the documents, with it.
/// </summary>
public sealed class SecretKindTests
{
    private static readonly RedactionEngine Default = new();

    private static readonly RedactionEngine WithLabels = new(RedactionPolicy.Default with { LabelledIds = true });

    public static TheoryData<string> Rows() => [.. SecretKinds.All.Select(r => r.Kind)];

    [Fact]
    public void EveryKindHasOneRow() =>
        Assert.Equal(SecretKinds.All.Count, SecretKinds.All.Select(r => r.Kind).Distinct(StringComparer.Ordinal).Count());

    [Theory]
    [MemberData(nameof(Rows))]
    public void TheStoredTextIsMaskedAsTheRowPromises(string kind)
    {
        var row = SecretKinds.All.Single(r => r.Kind == kind);
        Assert.Contains(row.Secret, row.Text, StringComparison.Ordinal);

        var byDefault = Default.ScrubText(row.Text);
        var labelled = WithLabels.ScrubText(row.Text);

        Assert.True(byDefault.Complete, row.Kind);
        Assert.True(labelled.Complete, row.Kind);
        Assert.True(
            Shows(byDefault.Text, row.Secret) == (row.Promise != SecretPromise.MustMask),
            $"{row.Kind} ({row.Promise}), default policy: \"{byDefault.Text}\"");
        Assert.True(
            Shows(labelled.Text, row.Secret) == (row.Promise == SecretPromise.DocumentedMiss),
            $"{row.Kind} ({row.Promise}), label rules on: \"{labelled.Text}\"");
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void TheScreenshotIsMaskedAsTheRowPromises(string kind)
    {
        var row = SecretKinds.All.Single(r => r.Kind == kind);
        var words = Lay(row.Text);

        var byDefault = Default.RedactFrame(words);
        var labelled = WithLabels.RedactFrame(words);

        Assert.True(byDefault.Complete, row.Kind);
        Assert.True(labelled.Complete, row.Kind);
        var maskedByDefault = row.Promise == SecretPromise.MustMask;
        var maskedWithLabels = row.Promise != SecretPromise.DocumentedMiss;
        Assert.True(Shows(byDefault.Text, row.Secret) != maskedByDefault, $"{row.Kind} ({row.Promise}), default policy: \"{byDefault.Text}\"");
        Assert.True(Shows(labelled.Text, row.Secret) != maskedWithLabels, $"{row.Kind} ({row.Promise}), label rules on: \"{labelled.Text}\"");

        // The picture loses what the text loses: every word of the secret is under a box.
        if (maskedByDefault)
        {
            Assert.All(WordsOf(words, row), w => Assert.True(Covered(byDefault, w), $"{row.Kind}: \"{w.Text}\" is not painted over"));
        }

        if (maskedWithLabels)
        {
            Assert.All(WordsOf(words, row), w => Assert.True(Covered(labelled, w), $"{row.Kind}: \"{w.Text}\" is not painted over with the label rules on"));
        }
    }

    /// <summary>
    /// A password list in a documentation tool or a spreadsheet: the "Password" header is the only cue,
    /// and every row under it is masked, not only the first.
    /// </summary>
    [Fact]
    public void EveryRowUnderAPasswordHeaderIsMasked()
    {
        var words = Table(
            ["Username", "Password", "URL"],
            ["alice", "Winter2026!", "intranet"],
            ["bob", "Harbour7Lamp", "portal"],
            ["carol", "Tulip#4471", "vpn"]);

        var redaction = Default.RedactFrame(words);

        Assert.True(redaction.Complete);
        foreach (var secret in new[] { "Winter2026!", "Harbour7Lamp", "Tulip#4471" })
        {
            Assert.DoesNotContain(secret, redaction.Text, StringComparison.Ordinal);
            Assert.True(Covered(redaction, words.Single(w => w.Text == secret)), $"{secret} is not painted over");
        }

        // The other columns stay readable: the names and the addresses are the note's material.
        foreach (var kept in new[] { "Username", "alice", "bob", "carol", "intranet", "portal", "vpn" })
        {
            Assert.Contains(kept, redaction.Text, StringComparison.Ordinal);
        }
    }

    /// <summary>The column ends where the table does: the paragraph under it is not part of it.</summary>
    [Fact]
    public void AColumnEndsWhereTheTableEnds()
    {
        var words = Table(
            ["Account", "Password", "Notes"],
            ["svc_backup", "Harbour7Lamp", "nightly"]);
        words.Add(new OcrWord("Rotate", 40, 40 + (6 * 30), 60, 20));
        words.Add(new OcrWord("quarterly", 300, 40 + (6 * 30), 90, 20));

        var redaction = Default.RedactFrame(words);

        Assert.DoesNotContain("Harbour7Lamp", redaction.Text, StringComparison.Ordinal);
        Assert.Contains("quarterly", redaction.Text, StringComparison.Ordinal);
        Assert.Contains("Rotate", redaction.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A column longer than the rule follows down (<see cref="RedactionEngine.PasswordColumnRows"/>) is
    /// not stored with its last rows readable: either every cell is masked or the frame is reported
    /// incomplete, and the worker discards it. No screen holds a table that long; a frame built to is
    /// still not kept half masked.
    /// </summary>
    [Fact]
    public void AColumnLongerThanTheRuleFollowsIsMaskedToItsEndOrTheFrameIsDiscarded()
    {
        var rows = RedactionEngine.PasswordColumnRows + 100;
        var words = Table([["User", "Password"], .. Enumerable.Range(1, rows).Select(r => new[] { $"user{r}", $"Harbour{r}Lamp" })]);

        var redaction = Default.RedactFrame(words);

        var shown = redaction.Text.Split(' ').ToHashSet(StringComparer.Ordinal);
        var readable = Enumerable.Range(1, rows).Where(r => shown.Contains($"Harbour{r}Lamp")).ToList();
        Assert.True(
            !redaction.Complete || readable.Count == 0,
            $"{readable.Count} of {rows} passwords are readable (the first in row {readable.FirstOrDefault()}), and the frame was reported complete");
    }

    /// <summary>
    /// A "Password" heading on its own with a sentence under it is not a table, and the sentence is the
    /// technician's material, so the column rule leaves it alone.
    /// </summary>
    [Fact]
    public void APasswordHeadingOverASentenceIsNotAColumn()
    {
        var words = new List<OcrWord>
        {
            new("Password", 40, 40, 80, 20),
            new("Change", 40, 70, 60, 20),
            new("it", 110, 70, 20, 20),
            new("every", 140, 70, 50, 20),
            new("90", 200, 70, 20, 20),
            new("days", 230, 70, 40, 20),
        };

        var redaction = Default.RedactFrame(words);

        Assert.Equal("Password Change it every 90 days", redaction.Text);
        Assert.DoesNotContain(redaction.Regions, r => r.Y >= 70);
    }

    /// <summary>
    /// One cue list for the sign-in heuristic and for masking: every password word the heuristic knows,
    /// in any of its languages, masks the value beside it.
    /// </summary>
    [Fact]
    public void EveryPasswordWordTheSignInHeuristicKnowsMasksItsValue()
    {
        foreach (var word in LoginScreenHeuristic.PasswordWords)
        {
            var text = word + ": Harbour7Lamp";
            Assert.DoesNotContain("Harbour7Lamp", Default.ScrubText(text).Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Harbour7Lamp", Default.ScrubText(char.ToUpperInvariant(word[0]) + word[1..] + " Harbour7Lamp").Text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Honest text beside the new rules: words and numbers an MSP's screens show that look like their
    /// shapes and are not secrets (ST-042's false-positive budget). Under the default policy and with the
    /// label rules on.
    /// </summary>
    [Theory]
    [InlineData(@"net use Z: \\fs01\share /persistent:yes")]
    [InlineData(@"net use \\fs01\share /delete")]
    [InlineData(@"net use * /delete /y")]
    [InlineData(@"net user jdoe /domain")]
    [InlineData(@"net user jdoe * /add")]
    [InlineData(@"psexec \\srv01 -s -i cmd")]
    [InlineData("ssh -p 2222 admin@10.0.0.5")]
    [InlineData("https://intranet.example/login?user=jo")]
    [InlineData("Open https://portal.example:8443/status for the status page")]
    [InlineData("Ticket 2026-11-28 12:30, from jo@example.com")]
    [InlineData("Build 110803 shipped with 271612 and 564531")]
    [InlineData("Order 123456-654321 and invoice 222222-333333 were paid")]
    [InlineData("Phone 900-555-1234 or 912-555-0100")]
    [InlineData("Version 912.70.1 build 2026")]
    [InlineData("Enter this code when the app asks for it")]
    [InlineData("Scan the QR code with the authenticator app, then press Next")]
    [InlineData("Account # 10023 for Acme Dental, contract 4471")]
    [InlineData("Customer account number 000123456789 renewed")]
    [InlineData("Passport photos are on the shared drive")]
    [InlineData("The routing table has 021000021 entries")]
    [InlineData("Routing number field was left empty")]
    [InlineData("HP ProLiant DL380 Gen10, DL 360 in rack 4")]
    [InlineData("Medicare enrolment closes in December")]
    [InlineData("Key Content is shown only to administrators")]
    public void OrdinaryTextBesideTheNewRulesIsLeftAlone(string text)
    {
        foreach (var engine in new[] { Default, WithLabels })
        {
            var scrubbed = engine.ScrubText(text);
            Assert.True(scrubbed.Complete);
            Assert.Equal(text, scrubbed.Text);
        }
    }

    /// <summary>A routing number is masked only when its checksum holds: nine digits under the label that fail it are left alone.</summary>
    [Fact]
    public void ARoutingNumberThatFailsItsChecksumIsLeftAlone()
    {
        Assert.Equal("Routing number: 021000022", WithLabels.ScrubText("Routing number: 021000022").Text);
        Assert.DoesNotContain("021000021", WithLabels.ScrubText("Routing number: 021000021").Text, StringComparison.Ordinal);
    }

    private static bool Shows(string text, string secret) => text.Contains(secret, StringComparison.Ordinal);

    /// <summary>The text laid out as a screen shows it: a row per line, a word per run of non-spaces, 10 px a character.</summary>
    internal static List<OcrWord> Lay(string text)
    {
        var words = new List<OcrWord>();
        var lines = text.Split('\n');
        for (var line = 0; line < lines.Length; line++)
        {
            var x = 40;
            foreach (var part in lines[line].Split(' '))
            {
                if (part.Length > 0)
                {
                    words.Add(new OcrWord(part, x, 40 + (line * 30), part.Length * 10, 20));
                    x += part.Length * 10;
                }

                x += 10;
            }
        }

        return words;
    }

    /// <summary>Cells in columns 260 px apart, a row every 30 px.</summary>
    private static List<OcrWord> Table(params string[][] rows)
    {
        var words = new List<OcrWord>();
        for (var row = 0; row < rows.Length; row++)
        {
            for (var cell = 0; cell < rows[row].Length; cell++)
            {
                words.Add(new OcrWord(rows[row][cell], 40 + (cell * 260), 40 + (row * 30), rows[row][cell].Length * 10, 20));
            }
        }

        return words;
    }

    /// <summary>The laid-out words that hold a piece of the secret.</summary>
    private static IEnumerable<OcrWord> WordsOf(List<OcrWord> words, SecretKinds.Row row)
    {
        var pieces = row.Secret.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return words.Where(w => pieces.Any(p => w.Text.Contains(p, StringComparison.Ordinal) || p.Contains(w.Text, StringComparison.Ordinal) && w.Text.Length >= 3));
    }

    private static bool Covered(FrameRedaction redaction, OcrWord word) =>
        redaction.Regions.Any(r =>
            r.X <= word.X && r.Y <= word.Y
            && r.X + r.Width >= word.X + word.Width
            && r.Y + r.Height >= word.Y + word.Height);
}

/// <summary>
/// Each new rule against input built to make a regular expression backtrack (K2's "must keep working":
/// new patterns must not be able to hang the scan). Timed with nothing beside it, best of three, against
/// the 700 ms a frame may take; and the scan must finish, since a pattern that ran out of its time leaves
/// the frame unsearched and it is discarded.
/// </summary>
[Collection("Timed with nothing running beside it")]
public sealed class SecretKindCostTests
{
    private static readonly RedactionEngine WithLabels = new(RedactionPolicy.Default with { LabelledIds = true });

    public static TheoryData<string> Inputs() =>
    [
        "url userinfo",
        "net use",
        "net user",
        "psexec",
        "convertto-securestring",
        "key content",
        "recovery key",
        "mfa setup key",
        "otpauth",
        "itin",
        "medicare",
        "passport",
        "drivers licence",
        "routing",
        "bank account",
        "shared password cue",
        "wifi key",
        "password column",
        "password column, every word a header",
        "password column, tables of headers under the rule's bound",
        "password column, every word a header on a wide page",
    ];

    [Theory]
    [MemberData(nameof(Inputs))]
    public void PathologicalInputDoesNotHangTheScan(string rule)
    {
        string Repeat(string piece, int length) => string.Concat(Enumerable.Repeat(piece, (length / piece.Length) + 1))[..length];
        const int Length = 60_000;
        var text = rule switch
        {
            "url userinfo" => "x://" + Repeat("a:b", Length) + " " + Repeat("ab://", Length / 2) + "a:",
            "net use" => Repeat("net use ", Length / 2) + Repeat(@"\\", Length / 4) + " " + Repeat("/user:a ", Length / 4),
            "net user" => Repeat("net user a ", Length),
            "psexec" => "psexec " + Repeat(" -p", Length),
            "convertto-securestring" => Repeat("ConvertTo-SecureString -String \"", Length / 2) + Repeat("'", Length / 2),
            "key content" => Repeat("Key Content : ", Length),
            "recovery key" => "recovery key " + Repeat("111111-", Length / 2) + Repeat("111111 ", Length / 2),
            "mfa setup key" => "Secret key: " + Repeat("ABCD ", Length / 2) + Repeat("abc2 ", Length / 2) + Repeat("can't scan " + new string('a', 70), Length / 2),
            "otpauth" => Repeat("otpauth://totp/a?secret=", Length / 2) + Repeat("A", Length / 2),
            "itin" => Repeat("ITIN 912-70-", Length / 2) + Repeat("9127", Length / 2),
            "medicare" => Repeat("Medicare number: 1EG4-", Length),
            "passport" => Repeat("Passport No. C0", Length),
            "drivers licence" => Repeat("Driver's License #: D1-", Length),
            "routing" => Repeat("Routing number: 02100002", Length),
            "bank account" => Repeat("Bank account number: 0001 ", Length),
            "shared password cue" => Repeat("Kennwort mot de passe contraseña ", Length / 2) + Repeat("ist ", Length / 2),
            "wifi key" => Repeat("the wifi key is the network key ", Length),
            _ => string.Empty,
        };

        var best = double.MaxValue;
        if (rule.StartsWith("password column", StringComparison.Ordinal))
        {
            // 8,000 words. Each column stands far enough from the next (21 px, 0.875 of a row's height) that
            // every header heads a column of its own, and near enough that the wrap pass reads each row as
            // one run, so what is timed is the column rule. A page of 8,000 cells each a run of its own is
            // the wrap pass's most costly shape, timed by OcrWrapCostTests; the column rule adds little to it.
            var (words, mustMask, mayEnd) = rule switch
            {
                // Every other header a password column 199 rows deep.
                "password column" => (Grid(40, 200, (row, col) => row == 0 ? (col % 2 == 0 ? "Password" : "User") : $"Ab{row}x{col}"), (Func<int, int, bool>)((row, col) => row > 0 && col % 2 == 0), false),

                // Every word a header with a column under it, deeper than the rule follows one down.
                "password column, every word a header" => (Grid(2, 4000, (_, _) => "Password"), (row, _) => row > 0, true),

                // The same in tables of 400 rows, each a column the rule follows to its end.
                "password column, tables of headers under the rule's bound" => (Grid(4, 2000, (_, _) => "Password", tableRows: 400), (row, _) => row % 400 > 0, false),

                // Eighty headers a row, so each one's row below holds eighty cells to look through.
                _ => (Grid(80, 100, (_, _) => "Password"), (row, _) => row > 0, false),
            };

            for (var i = 0; i < 3; i++)
            {
                var clock = Stopwatch.StartNew();
                var frame = WithLabels.RedactFrame(words);
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);

                // Every cell masked, or (only where the column goes on past the rule's bound) the frame
                // reported incomplete, which the worker discards: never stored with a cell readable.
                Assert.True(frame.Complete || mayEnd, $"{rule}: a pattern ran out of its time");
                if (frame.Complete)
                {
                    Assert.Empty(Readable(frame, words, mustMask));
                }
            }
        }
        else
        {
            for (var i = 0; i < 3; i++)
            {
                var clock = Stopwatch.StartNew();
                var scrubbed = WithLabels.ScrubText(text);
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
                Assert.True(scrubbed.Complete, $"{rule}: a pattern ran out of its time");
            }
        }

        Assert.True(best < 700, $"{rule}: {best:0} ms, over the 700 ms a frame may take");
    }

    /// <summary>Cells 66 px apart (45 wide), a row every 30 px; a gap of 200 px between tables.</summary>
    private static List<OcrWord> Grid(int columns, int rows, Func<int, int, string> text, int tableRows = int.MaxValue) =>
        [.. Enumerable.Range(0, columns * rows).Select(i => (Row: i / columns, Col: i % columns))
            .Select(c => new OcrWord(text(c.Row, c.Col), c.Col * 66, (c.Row * 30) + (c.Row / tableRows * 200), 45, OcrTokenRuleTests.Height))];

    /// <summary>
    /// The texts of cells that must be masked and are still in the stored text: a text is counted where
    /// it shows more often than the words that may show (headers, other columns) hold it.
    /// </summary>
    private static List<string> Readable(FrameRedaction frame, List<OcrWord> words, Func<int, int, bool> mustMask)
    {
        var shown = frame.Text.Split(' ').CountBy(t => t, StringComparer.Ordinal).ToDictionary(StringComparer.Ordinal);
        var columns = words.Count(w => w.Y == words[0].Y);
        var mayShow = words.Where((_, i) => !mustMask(i / columns, i % columns)).CountBy(w => w.Text, StringComparer.Ordinal).ToDictionary(StringComparer.Ordinal);
        return [.. words.Where((_, i) => mustMask(i / columns, i % columns)).Select(w => w.Text).Distinct(StringComparer.Ordinal)
            .Where(t => shown.GetValueOrDefault(t) > mayShow.GetValueOrDefault(t))];
    }
}
