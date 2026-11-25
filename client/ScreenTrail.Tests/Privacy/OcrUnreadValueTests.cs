using ScreenTrail.Core.Privacy;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Tests.Privacy;

/// <summary>
/// A credential cue whose value the OCR engine returned no word for (ST-042, after the viewer probe of
/// 2026-11-24). Through a scaled, lossy viewer Windows OCR read "token:" and nothing after it, "--key"
/// and nothing over the key's first row, or only a scrap of the key's end. These lay out what the engine
/// returned in those cases and ask that the place after the cue is masked, and that nothing else changes.
/// </summary>
public sealed class OcrUnreadValueTests
{
    private const int Height = 20;
    private const int Glyph = 10;
    private const int Space = 10;
    private const int Pitch = 30;
    private const int Left = 40;

    private static readonly RedactionEngine Engine = new();
    private static readonly RedactionEngine Without = new() { UnreadValues = false };

    [Theory]
    [InlineData("token:")]
    [InlineData("pass:")]
    [InlineData("Password")]
    [InlineData("--key")]
    [InlineData("api_key=")]
    [InlineData("client-secret:")]
    public void ACueWithNothingReadAfterItHasThePlaceAfterItMasked(string cue)
    {
        var words = Page(Row(0, Left, "Front", "desk", "checklist"), Row(1, Left, cue), Row(2, Left, "Queue", "cleared"));

        var (redaction, before) = (Engine.RedactFrame(words), Without.RedactFrame(words));

        // Where a value would be: right after the cue, a token's length at least, as tall as its row.
        var value = new OcrWord("x", words[3].X + words[3].Width + Space, words[3].Y, 30 * Glyph, Height);
        Assert.True(Covered(redaction, value), "the place after the cue is left unpainted");

        // What it adds is only that place: every box there was before is still there, and the new ones
        // touch no word, the cue included. ("pass:" reads on into the next row under the password rule,
        // which masks it as before.)
        Assert.All(before.Regions, r => Assert.Contains(r, redaction.Regions));
        var added = redaction.Regions.Where(r => !before.Regions.Contains(r)).ToList();
        Assert.NotEmpty(added);
        Assert.False(
            words.Any(w => added.Any(r => r.X < w.X + w.Width && w.X < r.X + r.Width && r.Y < w.Y + w.Height && w.Y < r.Y + r.Height)),
            "the slot paints over a word that was read");

        // Nothing was read, so the text and the counts are as they were.
        Assert.Equal(before.Text, redaction.Text);
        Assert.Equal(before.Counts, redaction.Counts);
    }

    [Fact]
    public void AScrapOfTheValueReadFarAfterTheCueIsMaskedWithTheGap()
    {
        // "--key BeF2" for "--key sk_live_Jtb1o1BeF2": the engine read the key's last four characters.
        var words = Page(Row(0, Left, "deploy", "--target", "production", "--key"), [new OcrWord("BeF2", 0, 0, 4 * Glyph, Height)]);
        var key = words[3];
        words[4] = words[4] with { X = key.X + key.Width + Space + (14 * Glyph), Y = key.Y };

        var redaction = Engine.RedactFrame(words);

        var gap = new OcrWord("x", key.X + key.Width, key.Y, words[4].X - (key.X + key.Width), Height);
        Assert.True(Covered(redaction, gap), "the unread stretch between the cue and the scrap is left unpainted");
        Assert.True(Covered(redaction, words[4]), "the scrap of the key is left unpainted");
        Assert.Equal("deploy --target production --key [SECRET]", redaction.Text);
        Assert.False(Touched(redaction, key), "the cue is painted over");
    }

    [Fact]
    public void AWordOfProseFarAfterTheCueIsKept()
    {
        // A form: "Password" and, across an empty field, "Remember me".
        var words = Page(Row(0, Left, "Password"), Row(0, Left + (8 * Glyph) + (12 * Height), "Remember", "me"));

        var redaction = Engine.RedactFrame(words);

        var field = new OcrWord("x", words[0].X + words[0].Width, words[0].Y, words[1].X - (words[0].X + words[0].Width), Height);
        Assert.True(Covered(redaction, field), "the empty field after the cue is left unpainted");
        Assert.False(Touched(redaction, words[1]), "a plain word after the field is painted");
        Assert.Equal("Password Remember me", redaction.Text);
    }

    [Theory]
    [InlineData("token:", "abc")]
    [InlineData("the", "password", "for", "the", "printer")]
    [InlineData("press", "any", "key", "to", "continue")]
    public void ACueWithAWordRightAfterItAddsNothing(params string[] texts)
    {
        var words = Page(Row(0, Left, texts));

        Assert.Equal(
            Without.RedactFrame(words).Regions,
            Engine.RedactFrame(words).Regions);
    }

    [Fact]
    public void AValueThatWasReadIsMaskedAsBeforeAndNoWider()
    {
        var words = Page(Row(0, Left, "pass:", "Tangerine3327Wobble"), Row(1, Left, "Queue", "cleared"));

        var (with, without) = (Engine.RedactFrame(words), Without.RedactFrame(words));

        Assert.Equal(without.Text, with.Text);
        Assert.Equal(without.Regions, with.Regions);
    }

    [Theory]
    [InlineData("keyboard")]
    [InlineData("Monkey")]
    [InlineData("key2")]
    [InlineData("token123")]
    [InlineData("passing")]
    public void AWordThatOnlyContainsACueIsNotOne(string text) => Assert.Null(RedactionEngine.CredentialCue(text));

    [Theory]
    [InlineData("token:", MaskKind.ApiKey)]
    [InlineData("--token", MaskKind.ApiKey)]
    [InlineData("x-api-key:", MaskKind.ApiKey)]
    [InlineData("APIKEY", MaskKind.ApiKey)]
    [InlineData("•-key", MaskKind.ApiKey)]
    [InlineData("pwd=", MaskKind.Password)]
    [InlineData("passi", MaskKind.Password)]
    [InlineData("Passphrase:", MaskKind.Password)]
    public void CuesAreReadWithTheirMarksTrimmed(string text, MaskKind kind) => Assert.Equal(kind, RedactionEngine.CredentialCue(text));

    [Fact]
    public void ARowThatMayReadRightToLeftIsLeftAlone()
    {
        var words = Page(Row(0, Left, "סיסמה", "token:"));

        Assert.Empty(Engine.RedactFrame(words).Regions);
    }

    [Fact]
    public void TheSlotReachesNoFurtherThanItsBound()
    {
        var words = Page(Row(3, Left, "token:"));

        var redaction = Engine.RedactFrame(words);

        var slot = Assert.Single(redaction.Regions);
        Assert.Equal(words[0].X + words[0].Width, slot.X);
        Assert.Equal((long)Math.Ceiling(RedactionEngine.UnreadValueReach * Height), slot.Width);
        Assert.True(slot.Height >= 1.5 * Height, "the slot is shorter than a value's capitals and descenders");
    }

    [Fact]
    public void TheDistancesAreTheOnesTheDocsState()
    {
        // docs/security/data-flow.md states these. Change one and change it there.
        Assert.Equal(1.2, RedactionEngine.UnreadValueGap);
        Assert.Equal(40, RedactionEngine.UnreadValueReach);
        Assert.Equal(0.75, RedactionEngine.UnreadValueHalfHeight);
    }

    private static OcrWord[] Row(int line, int x, params string[] texts)
    {
        var row = new OcrWord[texts.Length];
        for (var i = 0; i < texts.Length; i++)
        {
            row[i] = new OcrWord(texts[i], x, line * Pitch, texts[i].Length * Glyph, Height);
            x += row[i].Width + Space;
        }

        return row;
    }

    private static OcrWord[] Page(params OcrWord[][] rows) => [.. rows.SelectMany(r => r)];

    private static bool Covered(FrameRedaction redaction, OcrWord word) =>
        redaction.Regions.Any(r =>
            r.X <= word.X && r.Y <= word.Y
            && r.X + r.Width >= word.X + word.Width
            && r.Y + r.Height >= word.Y + word.Height);

    private static bool Touched(FrameRedaction redaction, OcrWord word) =>
        redaction.Regions.Any(r =>
            r.X < word.X + word.Width && word.X < r.X + r.Width
            && r.Y < word.Y + word.Height && word.Y < r.Y + r.Height);
}
