using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using ScreenTrail.Core.Privacy;
using ScreenTrail.Service.Privacy;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Tests.Windows.Privacy;

/// <summary>
/// A-02 (a), measured on Windows (MC-a1): what masking the text-like areas the engine read no word of
/// adds through a scaled, lossy viewer, what it costs on pages with no secret, and how long the picture
/// checks take a frame. A measurement, not a floor: it prints its tables, and asserts only that every
/// page was drawn and read and that the checks never made a frame the worker would refuse.
/// </summary>
public sealed partial class OcrScaledSecretTests
{
    private const int CoveragePages = 10;

    [Fact]
    public async Task WhatMaskingUnreadTextAddsAndCostsIsMeasured()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        var recogniser = new WindowsOcrRecogniser();
        Assert.SkipUnless(recogniser.Available, "Windows has no OCR language pack installed.");
        var decoder = new WindowsFrameDecoder();
        var ct = TestContext.Current.CancellationToken;
        var times = new List<double>();

        var secrets = new List<string>
        {
            $"Secrets masked whole through the viewer (11pt at 100%, bilinear resize, JPEG 75; {CoveragePages} pages a font, 5 secrets a page), from the same readings: by the text's rules alone, and with the unread text-like areas masked too.",
            "| scale | font | secrets | masked by the rules | masked with unread areas | still part showing | missed |",
            "|---|---|---|---|---|---|---|",
        };
        var pages = 0;
        foreach (var scale in new[] { 0.5, 0.6, 0.7, 0.8, 1.0 })
        {
            foreach (var font in Fonts)
            {
                int count = 0, before = 0, after = 0, partial = 0, missed = 0;
                for (var page = 0; page < CoveragePages; page++)
                {
                    using var screen = Draw(font, 11f, 1.0, page);
                    using var seen = Resize(screen.Bitmap, scale);
                    var drawn = screen.Secrets.Select(s => s with { Pieces = [.. s.Pieces.Select(p => Scale(p, scale))] }).ToList();
                    var (rules, all) = await LookAsync(Encode(seen, 75), recogniser, decoder, times, ct);
                    pages++;
                    var dark = Dark(seen);
                    foreach (var secret in drawn)
                    {
                        count++;
                        before += Share(dark, seen.Width, seen.Height, secret.Pieces, rules) >= 0.97 ? 1 : 0;
                        var share = Share(dark, seen.Width, seen.Height, secret.Pieces, all);
                        (after, partial, missed) = share >= 0.97 ? (after + 1, partial, missed) : share > 0.03 ? (after, partial + 1, missed) : (after, partial, missed + 1);
                    }
                }

                secrets.Add(string.Create(CultureInfo.InvariantCulture, $"| {scale:0.0} | {font} | {count} | {before} | {after} | {partial} | {missed} |"));
            }
        }

        var plain = new List<string>
        {
            $"Masked as unread text on pages with no secret (9 and 11pt, 100% and through the viewer at 0.7 with JPEG 75; {CoveragePages} pages each): share of the ink and of the page under an unread-area mask, and pages with any.",
            "| page | font | view | ink masked | page masked | pages with any |",
            "|---|---|---|---|---|---|",
        };
        foreach (var kind in PlainKinds)
        {
            foreach (var font in Fonts)
            {
                foreach (var scale in new[] { 1.0, 0.7 })
                {
                    long ink = 0, inkMasked = 0, area = 0, areaMasked = 0;
                    var touched = 0;
                    for (var n = 0; n < CoveragePages; n++)
                    {
                        foreach (var points in new[] { 9f, 11f })
                        {
                            using var drawnPage = DrawPlain(kind, font, points, n);
                            using var seen = Resize(drawnPage, scale);
                            var (rules, all) = await LookAsync(Encode(seen, scale < 1 ? 75 : 82), recogniser, decoder, times, ct);
                            pages++;
                            var unread = all.Skip(rules.Count).ToList();
                            touched += unread.Count > 0 ? 1 : 0;
                            var dark = Dark(seen);
                            var under = Under(unread, seen.Width, seen.Height);
                            for (var i = 0; i < dark.Length; i++)
                            {
                                ink += dark[i] ? 1 : 0;
                                inkMasked += dark[i] && under[i] ? 1 : 0;
                                areaMasked += under[i] ? 1 : 0;
                            }

                            area += dark.Length;
                        }
                    }

                    plain.Add(string.Create(CultureInfo.InvariantCulture, $"| {kind} | {font} | {(scale < 1 ? "viewer 0.7" : "100%")} | {(double)inkMasked / Math.Max(1, ink):P1} | {(double)areaMasked / area:P1} | {touched} of {CoveragePages * 2} |"));
                }
            }
        }

        times.Sort();
        Measurements.Record(string.Join(Environment.NewLine, secrets));
        Measurements.Record(string.Join(Environment.NewLine, plain));
        Measurements.Record(string.Create(
            CultureInfo.InvariantCulture,
            $"The picture checks (QR search and unread-text areas) over {times.Count} frames: median {times[times.Count / 2]:0.0} ms, 95th percentile {times[(int)(times.Count * 0.95)]:0.0} ms, most {times[^1]:0.0} ms."));

        Assert.Equal(pages, times.Count);
    }

    /// <summary>
    /// What the worker masks on one frame: the rules' regions from the words, and those with the QR
    /// codes and unread text-like areas the picture checks add, in that order. Times the checks.
    /// </summary>
    private static async Task<(IReadOnlyList<MaskedRegion> Rules, IReadOnlyList<MaskedRegion> All)> LookAsync(
        byte[] image, WindowsOcrRecogniser recogniser, WindowsFrameDecoder decoder, List<double> times, CancellationToken ct)
    {
        var words = (await recogniser.ReadAsync(image, ct)).Words;
        var rules = new RedactionEngine().RedactFrame(words).Regions.ToList();
        using var decoded = await decoder.DecodeAsync(image, ct);
        var brightness = decoded.ReadBrightness();
        Assert.NotNull(brightness);
        var clock = Stopwatch.StartNew();
        var codes = QrCodeFinder.Find(brightness);
        Assert.True(codes.Complete, "a page with no code was refused by the QR search");
        var unread = TextCoverage.Unread(brightness, words, [.. rules, .. codes.Codes]);
        times.Add(clock.Elapsed.TotalMilliseconds);
        return (rules, [.. rules, .. codes.Codes, .. unread]);
    }

    private static bool[] Under(IEnumerable<MaskedRegion> regions, int width, int height)
    {
        var under = new bool[width * height];
        foreach (var region in regions)
        {
            var (left, right) = ((int)Math.Max(0, region.X), (int)Math.Min(width, region.X + region.Width));
            for (var y = (int)Math.Max(0, region.Y); y < Math.Min(height, region.Y + region.Height) && right > left; y++)
            {
                Array.Fill(under, true, (y * width) + left, right - left);
            }
        }

        return under;
    }

    private static double Share(bool[] dark, int width, int height, Rectangle[] pieces, IReadOnlyList<MaskedRegion> regions)
    {
        var under = Under(regions, width, height);
        int ink = 0, covered = 0;
        foreach (var piece in pieces)
        {
            for (var y = Math.Max(0, piece.Top); y < Math.Min(height, piece.Bottom); y++)
            {
                for (var x = Math.Max(0, piece.Left); x < Math.Min(width, piece.Right); x++)
                {
                    ink += dark[(y * width) + x] ? 1 : 0;
                    covered += dark[(y * width) + x] && under[(y * width) + x] ? 1 : 0;
                }
            }
        }

        return ink == 0 ? 0 : (double)covered / ink;
    }
}
