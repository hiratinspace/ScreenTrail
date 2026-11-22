using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using ScreenTrail.Core.Capture;
using ScreenTrail.Core.Privacy;
using ScreenTrail.Service.Privacy;
using ScreenTrail.Shared.Schema;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ScreenTrail.Tests.Windows.Privacy;

/// <summary>
/// ST-031: a frame is decoded once and shared between the OCR read and the mask. Only Windows can prove
/// the part that matters for redaction strength: that the engine is given the same pixels as when it
/// decoded the JPEG itself, at every size it reads (as captured, and scaled up by the same cubic scaler),
/// and that the masker paints the decoded picture in place. A frame read differently could read fewer
/// secrets, and the Mac suite has no decoder to say otherwise.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class SharedDecodeTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static bool OnWindows => OperatingSystem.IsWindows();

    /// <summary>Full HD and 720p are read at up to twice their size; 4K as captured (OcrScaling.PixelBudget).</summary>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 720)]
    [InlineData(3840, 2160)]
    public async Task TheEngineIsGivenTheSamePixelsAsWhenItDecodedTheJpegItself(int width, int height)
    {
        Assert.SkipUnless(OnWindows, "Windows only.");
        var ct = TestContext.Current.CancellationToken;
        var image = RenderText(width, height);
        var read = OcrScaling.ReadSize((uint)width, (uint)height, WindowsOcrRecogniser.DefaultScale, OcrEngine.MaxImageDimension);

        using var before = await AsTheJpegGaveItAsync(image, read, ct);
        using var decoded = (WindowsDecodedFrame)await new WindowsFrameDecoder().DecodeAsync(image, ct);
        using var after = await WindowsOcrRecogniser.PictureToReadAsync(decoded, read, ct);

        Assert.Equal((before.PixelWidth, before.PixelHeight), (after.PixelWidth, after.PixelHeight));
        var (a, b) = (Pixels(before), Pixels(after));
        var differing = 0;
        var largest = 0;
        for (var i = 0; i < a.Length; i += 4)
        {
            // Blue, green and red: the engine reads no alpha, and a JPEG has none.
            for (var c = 0; c < 3; c++)
            {
                var difference = Math.Abs(a[i + c] - b[i + c]);
                if (difference > 0)
                {
                    differing++;
                    largest = Math.Max(largest, difference);
                }
            }
        }

        Measurements.Record(string.Create(
            Invariant,
            $"Shared decode, {width}x{height} read at {read.Width}x{read.Height}: **{differing}** of {a.Length / 4 * 3} channel values differ from the JPEG decoded for OCR (largest difference {largest})"));
        Assert.True(differing == 0, $"the engine would read different pixels: {differing} channel values differ, by up to {largest}");
    }

    [Fact]
    public async Task TheEngineReadsTheSameWordsInTheSamePlacesFromTheSharedDecode()
    {
        Assert.SkipUnless(OnWindows, "Windows only.");
        var recogniser = new WindowsOcrRecogniser();
        Assert.SkipUnless(recogniser.Available, "Windows has no OCR language pack installed.");
        var ct = TestContext.Current.CancellationToken;
        var image = RenderText(1920, 1080);

        var fromJpeg = await recogniser.ReadAsync(image, ct);
        using var decoded = await new WindowsFrameDecoder().DecodeAsync(image, ct);
        var fromShared = await ((IFrameTextRecogniser)recogniser).ReadAsync(decoded, ct);

        Assert.NotEmpty(fromJpeg.Words);
        Assert.Equal(fromJpeg.Words, fromShared.Words);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 720)]
    public async Task TheMaskerPaintsTheDecodedPictureInPlaceAndStoresIt(int width, int height)
    {
        Assert.SkipUnless(OnWindows, "Windows only.");
        var ct = TestContext.Current.CancellationToken;
        var image = RenderText(width, height);
        using var decoded = (WindowsDecodedFrame)await new WindowsFrameDecoder().DecodeAsync(image, ct);

        // Aligned to the JPEG encoder's 16 px blocks, so the samples inside sit in wholly painted blocks.
        var region = new MaskedRegion { X = 96, Y = 96, Width = 400, Height = 160, Kind = MaskKind.Card };
        var masked = ((IFrameMasker)new WindowsFrameMasker()).Mask(decoded, [region], Downscale.MaxEdge);

        // In place: the decoded picture itself is painted, so nothing could read the secret from it after.
        var painted = Pixels(decoded.Bitmap);
        foreach (var (x, y) in new[] { (96, 96), (495, 255), (300, 180) })
        {
            var at = ((y * width) + x) * 4;
            Assert.Equal((0, 0, 0), (painted[at], painted[at + 1], painted[at + 2]));
        }

        var plan = Downscale.For(width, height, Downscale.MaxEdge);
        Assert.Equal((plan.Width, plan.Height), (masked.Width, masked.Height));
        using var stored = new Bitmap(new MemoryStream(masked.Image));
        Assert.Equal((plan.Width, plan.Height), (stored.Width, stored.Height));
        var scale = (double)plan.Width / width;
        foreach (var (x, y) in new[] { (300, 176), (112, 112), (480, 240) })
        {
            var sample = stored.GetPixel((int)(x * scale), (int)(y * scale));
            Assert.True(sample.R < 40 && sample.G < 40 && sample.B < 40, $"expected the masked area to be black at ({x}, {y}), found {sample}");
        }

        var outside = stored.GetPixel((int)((width - 40) * scale), (int)((height - 40) * scale));
        Assert.True(outside.R > 200 && outside.G > 200 && outside.B > 200, $"expected the background outside the box, found {outside}");
    }

    [Fact]
    public async Task ADecodedFrameLetGoOfCannotBeReadOrPainted()
    {
        Assert.SkipUnless(OnWindows, "Windows only.");
        var ct = TestContext.Current.CancellationToken;
        var decoded = (WindowsDecodedFrame)await new WindowsFrameDecoder().DecodeAsync(RenderText(640, 360), ct);

        decoded.Dispose();
        decoded.Dispose();

        Assert.Throws<ObjectDisposedException>(() => decoded.Lock(BitmapBufferAccessMode.Read));
    }

    /// <summary>What a decode costs, against the masker's own: the second decode this change removes.</summary>
    [Fact]
    public async Task TheDecodeItSavesIsMeasured()
    {
        Assert.SkipUnless(OnWindows, "Windows only.");
        var ct = TestContext.Current.CancellationToken;
        var image = RenderText(1920, 1080);
        var decoder = new WindowsFrameDecoder();

        // GDI+ decoding the JPEG is what the masker did for every frame before; the WIC decode is the one
        // decode that is left. Wall time, median of seven after a warm-up, on whichever machine runs it.
        var gdi = await Median(7, () =>
        {
            using var source = new MemoryStream(image, writable: false);
            using var bitmap = new Bitmap(source);
            _ = bitmap.Width;
            return Task.CompletedTask;
        });
        var wic = await Median(7, async () =>
        {
            using var decoded = await decoder.DecodeAsync(image, ct);
        });

        Measurements.Record(string.Create(
            Invariant,
            $"Decode of a 1920x1080 frame ({image.Length / 1024} KB), median wall ms: GDI+ (the masker's decode, now gone) **{gdi:F1}**, WIC (the one left) {wic:F1}"));
    }

    private static async Task<double> Median(int runs, Func<Task> work)
    {
        await work();
        var times = new List<double>(runs);
        for (var i = 0; i < runs; i++)
        {
            var started = Stopwatch.GetTimestamp();
            await work();
            times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        times.Sort();
        return times[runs / 2];
    }

    /// <summary>The engine's picture exactly as the recogniser made it before the decode was shared.</summary>
    private static async Task<SoftwareBitmap> AsTheJpegGaveItAsync(byte[] image, OcrReadSize read, CancellationToken ct)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(image.AsBuffer()).AsTask(ct);
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct);
        return read.Width == decoder.PixelWidth && read.Height == decoder.PixelHeight
            ? await decoder.GetSoftwareBitmapAsync().AsTask(ct)
            : await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform
                {
                    ScaledWidth = read.Width,
                    ScaledHeight = read.Height,
                    InterpolationMode = BitmapInterpolationMode.Cubic,
                },
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage).AsTask(ct);
    }

    private static byte[] Pixels(SoftwareBitmap bitmap)
    {
        using var bgra = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8
            ? SoftwareBitmap.Copy(bitmap)
            : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8);
        var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyToBuffer(pixels.AsBuffer());
        return pixels;
    }

    /// <summary>Dense small text on a light card, drawn rather than captured, so every runner reads the same frame.</summary>
    private static byte[] RenderText(int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        bitmap.SetResolution(96, 96);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(243, 243, 243));
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var font = new Font("Segoe UI", 9);
        using var ink = new SolidBrush(Color.FromArgb(28, 28, 28));
        for (var (y, row) = (20, 0); y < height - 60; y += 18, row++)
        {
            for (var x = 20; x < width - 240; x += 300)
            {
                graphics.DrawString(string.Create(Invariant, $"Service {row:D3} Startup type Automatic {x}"), font, ink, x, y);
            }
        }

        using var buffer = new MemoryStream();
        bitmap.Save(buffer, ImageFormat.Jpeg);
        return buffer.ToArray();
    }
}
