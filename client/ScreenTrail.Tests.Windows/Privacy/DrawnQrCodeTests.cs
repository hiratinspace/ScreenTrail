using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using ScreenTrail.Core.Capture;
using ScreenTrail.Core.Privacy;
using ScreenTrail.Core.Store;
using ScreenTrail.Service.Privacy;
using ScreenTrail.Service.Store;
using ScreenTrail.Shared.Schema;
using ScreenTrail.Tests.Privacy;

namespace ScreenTrail.Tests.Windows.Privacy;

/// <summary>
/// A-02 (c) on Windows: a QR code drawn on a frame comes out of the real worker masked, through the real
/// decoder, OCR engine, masker and encrypted store. The Mac suite proves the finding on drawn brightness;
/// this proves the brightness Windows gives is the picture's, and that the square is painted in the
/// stored JPEG.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class DrawnQrCodeTests : IAsyncDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "screentrail-tests", Guid.NewGuid().ToString("N"));
    private SqliteSessionStore? _store;

    private static bool OnWindows => OperatingSystem.IsWindows();

    [Fact]
    public async Task TheDecodedFrameGivesItsBrightness()
    {
        Assert.SkipUnless(OnWindows, "Windows only.");
        using var bitmap = new Bitmap(4, 2, PixelFormat.Format24bppRgb);
        (int X, int Y, Color Colour, byte Brightness)[] samples =
        [
            (0, 0, Color.FromArgb(200, 100, 50), 124),
            (1, 0, Color.White, 255),
            (2, 0, Color.Black, 0),
            (3, 1, Color.FromArgb(0, 0, 255), 29),
        ];
        foreach (var (x, y, colour, _) in samples)
        {
            bitmap.SetPixel(x, y, colour);
        }

        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        using var decoded = await new WindowsFrameDecoder().DecodeAsync(png.ToArray(), TestContext.Current.CancellationToken);

        var brightness = decoded.ReadBrightness();
        Assert.NotNull(brightness);
        Assert.Equal((4, 2), (brightness.Width, brightness.Height));
        foreach (var (x, y, _, expected) in samples)
        {
            Assert.Equal(expected, brightness.Pixels[(y * 4) + x]);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public async Task ADrawnQrCodeIsMasked(int module)
    {
        Assert.SkipUnless(OnWindows, "Windows only.");
        var recogniser = new WindowsOcrRecogniser();
        Assert.SkipUnless(recogniser.Available, "Windows has no OCR language pack installed.");
        var ct = TestContext.Current.CancellationToken;
        var (image, code) = Render(1280, 720, module);
        Measurements.Save($"qr-drawn-{module}.jpg", image);

        _store ??= await SqliteSessionStore.OpenAsync(Path.Combine(_dir, "store.db"), new DpapiKeyProvider(Path.Combine(_dir, "store.key")), ct: ct);
        var session = $"s{module}";
        await _store.CreateSessionAsync(new NewSession(session, DateTimeOffset.UtcNow, new RemoteTool { Kind = RemoteToolKind.Rdp }, false, null), ct);
        var pending = new PendingFrames(depth: 1);
        Assert.True(pending.TryEnqueue(session, new StagedFrame($"f{module}", 1_000, FrameTrigger.Click, 1280, 720, null, image)));
        var worker = new RedactionWorker(
            _store, recogniser, new WindowsFrameMasker(), new RedactionEngine(), pending,
            new RedactionOptions { Concurrency = 0 }, decoder: new WindowsFrameDecoder());

        Assert.True(await worker.ProcessOneAsync(ct));

        var frame = Assert.Single((await _store.LoadSessionAsync(session, ct))!.Frames);
        Assert.Contains(frame.MaskedRegions, r => r.Kind == MaskKind.SensitiveContext
            && r.X <= code.X && r.Y <= code.Y && r.X + r.Width >= code.Right && r.Y + r.Height >= code.Bottom);

        // Painted in the stored picture: inside the code's square, away from the JPEG's ringing at the edge,
        // there is no light module left.
        var stored = (await _store.GetRedactedFrameImageAsync(frame.Id, ct))!;
        Measurements.Save($"qr-masked-{module}.jpg", stored);
        using var picture = new Bitmap(new MemoryStream(stored));
        Assert.Equal((1280, 720), (picture.Width, picture.Height));
        var light = 0;
        for (var y = code.Top + 2; y < code.Bottom - 2; y += 2)
        {
            for (var x = code.Left + 2; x < code.Right - 2; x += 2)
            {
                var pixel = picture.GetPixel(x, y);
                light += pixel.R > 60 || pixel.G > 60 || pixel.B > 60 ? 1 : 0;
            }
        }

        Assert.True(light == 0, $"{light} sampled pixels of the QR code's square are not masked in the stored picture (module {module} px)");
    }

    /// <summary>
    /// An authenticator's enrolment page, drawn by GDI+: a heading and an instruction the engine reads,
    /// and the code (<see cref="PictureCanvas.QrModules"/>) on its white quiet zone.
    /// </summary>
    private static (byte[] Image, Rectangle Code) Render(int width, int height, int module)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        bitmap.SetResolution(96, 96);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(243, 243, 243));
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var heading = new Font("Segoe UI", 16, FontStyle.Bold);
        using var body = new Font("Segoe UI", 11);
        using var ink = new SolidBrush(Color.FromArgb(28, 28, 28));
        graphics.DrawString("Set up your authenticator app", heading, ink, 60, 40);
        graphics.DrawString("Scan this code with the app on your phone, then enter the six digits it shows.", body, ink, 60, 100);

        var size = PictureCanvas.QrModules.Length * module;
        var code = new Rectangle(560, 220, size, size);
        graphics.FillRectangle(Brushes.White, code.X - (4 * module), code.Y - (4 * module), size + (8 * module), size + (8 * module));
        for (var row = 0; row < PictureCanvas.QrModules.Length; row++)
        {
            for (var column = 0; column < PictureCanvas.QrModules[row].Length; column++)
            {
                if (PictureCanvas.QrModules[row][column] == '#')
                {
                    graphics.FillRectangle(Brushes.Black, code.X + (column * module), code.Y + (row * module), module, module);
                }
            }
        }

        using var buffer = new MemoryStream();
        bitmap.Save(buffer, ImageFormat.Jpeg);
        return (buffer.ToArray(), code);
    }

    public async ValueTask DisposeAsync()
    {
        if (_store is not null)
        {
            await _store.DisposeAsync();
        }

        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
