using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using ScreenTrail.Core.Privacy;
using ScreenTrail.Service.Privacy;

namespace ScreenTrail.Tests.Windows.Privacy;

/// <summary>
/// The measurement DEC-08 asks for before the label rules (<see cref="RedactionPolicy.LabelledIds"/>) go
/// on by default: how much of a page with no id on it each one masks, read by Windows OCR as the product
/// reads (twice the captured size). Measured on the pages with no secret, and on a page of the words the
/// labels are made of in their ordinary use on an MSP's screens ("Account #", "DL380", "routing table"),
/// which is where they would cost; and, for scale, how many of a page of labelled ids they mask. A
/// measurement, not a floor: the decision is the owner's, from these numbers.
/// </summary>
public sealed partial class OcrScaledSecretTests
{
    private static readonly string[] LabelWordsInUse =
    [
        "Account # 10023   Acme Dental   Contract 4471   Status Active",
        "Customer account number 000123456789 renewed on 2026-11-02",
        "Passport photos for the travel team are on the shared drive",
        "HP ProLiant DL380 Gen10 in rack 4, DL 360 spare in the store room",
        "Medicare enrolment closes in December, reminder sent to HR",
        "Routing table updated, 214 routes, gateway 10.0.0.1",
        "Bank reconciliation report exported for accounting on Friday",
        "Driver's license scan uploaded to HR ticket 48213",
        "ABA training on Tuesday, RTN team meeting at 10:00",
        "Checking account sync failed in QuickBooks, retried at 14:02",
        "Savings plan enrolment form 2026, page 3 of 4",
        "Bank account 4 of 6 reconciled, 2 waiting",
    ];

    private static readonly string[] LabelledIdsPage =
    [
        "Routing number: 021000021",
        "Bank account number: 000123456789",
        "Passport No. C03005988",
        "Driver's License #: D123-4567-8901",
        "Medicare number: 1EG4-TE5-MK73",
    ];

    [Fact]
    public async Task MaskedAreaOfTheLabelRulesIsMeasured()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        var recogniser = new WindowsOcrRecogniser(scale: 2.0);
        Assert.SkipUnless(recogniser.Available, "Windows has no OCR language pack installed.");
        var engines = new[] { ("default", new RedactionEngine()), ("label rules on", new RedactionEngine(RedactionPolicy.Default with { LabelledIds = true })) };
        var plain = OnTheLaptop ? 1 : 3;

        var table = new List<string>
        {
            "Label rules (DEC-08), read at 2x, Segoe UI and Consolas, 9 and 11pt: share of the ink masked, pages with any mask, finds by kind.",
            "| page | " + string.Join(" | ", engines.Select(e => e.Item1)) + " |",
            "|---|" + string.Concat(Enumerable.Repeat("---|", engines.Length)),
        };

        foreach (var kind in PlainKinds.Append("label words in use"))
        {
            var (ink, masked, touched) = (new long[engines.Length], new long[engines.Length], new int[engines.Length]);
            var kinds = engines.Select(_ => new Dictionary<ScreenTrail.Shared.Schema.MaskKind, int>()).ToArray();
            var pages = 0;
            foreach (var font in Fonts)
            {
                foreach (var points in new[] { 9f, 11f })
                {
                    for (var n = 0; n < (kind == "label words in use" ? 1 : plain); n++)
                    {
                        using var bitmap = kind == "label words in use" ? DrawLines(LabelWordsInUse, font, points) : DrawPlain(kind, font, points, n);
                        var words = (await recogniser.ReadAsync(Encode(bitmap, 82), TestContext.Current.CancellationToken)).Words;
                        var dark = Dark(bitmap);
                        pages++;
                        for (var e = 0; e < engines.Length; e++)
                        {
                            var redaction = engines[e].Item2.RedactFrame(words);
                            var under = Masked(redaction.Regions, bitmap.Width, bitmap.Height);
                            var onInk = 0;
                            for (var i = 0; i < under.Length; i++)
                            {
                                ink[e] += dark[i] ? 1 : 0;
                                onInk += under[i] && dark[i] ? 1 : 0;
                            }

                            masked[e] += onInk;
                            touched[e] += onInk > 0 ? 1 : 0;
                            foreach (var (found, count) in redaction.Counts)
                            {
                                kinds[e][found] = kinds[e].GetValueOrDefault(found) + count;
                            }
                        }
                    }
                }
            }

            table.Add($"| {kind} | " + string.Join(" | ", Enumerable.Range(0, engines.Length).Select(e => string.Create(
                CultureInfo.InvariantCulture,
                $"{(double)masked[e] / Math.Max(1, ink[e]):0.0%} ink, {touched[e]} of {pages} ({string.Join(" ", kinds[e].OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))})"))) + " |");
        }

        // For scale: how many of a page of labelled ids the rules mask, one id a line.
        var ids = new int[engines.Length];
        var lines = 0;
        foreach (var font in Fonts)
        {
            using var bitmap = DrawLines(LabelledIdsPage, font, 11f);
            var words = (await recogniser.ReadAsync(Encode(bitmap, 82), TestContext.Current.CancellationToken)).Words;
            lines += LabelledIdsPage.Length;
            for (var e = 0; e < engines.Length; e++)
            {
                ids[e] += engines[e].Item2.RedactFrame(words).Counts.Values.Sum();
            }
        }

        table.Add($"Finds on a page of {lines} labelled ids (11pt, both fonts): " + string.Join(", ", Enumerable.Range(0, engines.Length).Select(e => $"{engines[e].Item1} {ids[e]}")));
        Measurements.Record(string.Join(Environment.NewLine, table));
    }

    /// <summary>The lines given, drawn as <see cref="DrawPlain"/> draws its pages.</summary>
    private static Bitmap DrawLines(string[] lines, string family, float points)
    {
        var pixels = (float)(points * 96.0 / 72.0);
        using var font = new Font(family, pixels, FontStyle.Regular, GraphicsUnit.Pixel);
        var pitch = (int)Math.Ceiling(font.GetHeight(96f) * 1.15);
        var bitmap = new Bitmap(BaseWidth, (2 * Margin) + (lines.Length * pitch), PixelFormat.Format24bppRgb);
        bitmap.SetResolution(96, 96);
        const System.Windows.Forms.TextFormatFlags Flags =
            System.Windows.Forms.TextFormatFlags.NoPadding | System.Windows.Forms.TextFormatFlags.NoPrefix
            | System.Windows.Forms.TextFormatFlags.SingleLine | System.Windows.Forms.TextFormatFlags.Left | System.Windows.Forms.TextFormatFlags.Top;
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        for (var i = 0; i < lines.Length; i++)
        {
            System.Windows.Forms.TextRenderer.DrawText(graphics, lines[i], font, new System.Drawing.Point(Margin, Margin + (i * pitch)), Color.Black, Color.White, Flags);
        }

        return bitmap;
    }
}
