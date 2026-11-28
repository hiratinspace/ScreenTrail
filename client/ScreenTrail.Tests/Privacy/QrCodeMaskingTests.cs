using System.Security.Cryptography;
using ScreenTrail.Core.Capture;
using ScreenTrail.Core.Privacy;
using ScreenTrail.Core.Store;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Tests.Privacy;

/// <summary>
/// A-02 (c): a QR code on a frame is masked in the stored picture. It is never text, so no pattern can
/// find it, and an MFA enrolment code or a Wi-Fi code read from a screenshot is the secret itself. The
/// worker finds the code by its three finder patterns and covers the code's square.
/// </summary>
public sealed class QrCodeMaskingTests : IAsyncDisposable
{
    private const int Width = 1280;
    private const int Height = 720;
    private static readonly DateTimeOffset At = new(2026, 11, 28, 9, 0, 0, TimeSpan.Zero);
    private readonly string _path = Path.Combine(Path.GetTempPath(), "screentrail-tests", $"{Guid.NewGuid():N}.db");
    private readonly PendingFrames _pending = new(depth: 10);
    private SqliteSessionStore? _store;

    public static TheoryData<int, bool> Codes => new()
    {
        { 2, false },
        { 3, false },
        { 4, false },
        { 6, false },
        { 10, false },
        { 4, true },
    };

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task AQrCodeOnAFrameIsMaskedInTheStoredPicture(int module, bool inverted)
    {
        // A sign-in page that does not look like one: a heading the engine reads, and the enrolment code.
        var canvas = new PictureCanvas(Width, Height, paper: inverted ? (byte)30 : (byte)250);
        var heading = canvas.DrawText(40, 24, "setup", scale: 3, ink: inverted ? (byte)230 : (byte)20);
        var (x, y, size) = inverted
            ? canvas.DrawQr(560, 260, module, dark: 230, light: 30)
            : canvas.DrawQr(560, 260, module);
        var words = new[] { new OcrWord("Set up your authenticator app", heading.X, heading.Y, heading.Width, heading.Height) };

        var regions = await RedactAsync(canvas, words);

        var ink = canvas.Ink(x, y, size, size);
        var left = canvas.InkLeft(regions, x, y, size, size);
        Assert.True(ink > 0, "the code was drawn with no dark modules, so the frame proves nothing");
        Assert.True(left == 0, $"{left} of the QR code's {ink} dark pixels are not masked (module {module} px{(inverted ? ", light on dark" : string.Empty)}); the stored regions are [{string.Join("; ", regions.Select(r => $"{r.Kind} {r.X},{r.Y} {r.Width}x{r.Height}"))}]");

        // The heading the engine read is not a QR code and stays readable.
        var picture = regions.Where(r => r.Kind == MaskKind.SensitiveContext).ToList();
        Assert.Equal(canvas.Ink(heading.X, heading.Y, heading.Width, heading.Height), canvas.InkLeft(picture, heading.X, heading.Y, heading.Width, heading.Height));
    }

    [Fact]
    public async Task EachOfTwoCodesIsMaskedAndTheMaskIsTheCodeAndItsMargin()
    {
        var canvas = new PictureCanvas(Width, Height);
        var heading = canvas.DrawText(40, 24, "setup", scale: 3);
        var codes = new[] { canvas.DrawQr(120, 200, 5), canvas.DrawQr(800, 300, 3) };
        var regions = await RedactAsync(canvas, [new OcrWord("Setup", heading.X, heading.Y, heading.Width, heading.Height)]);

        var picture = regions.Where(r => r.Kind == MaskKind.SensitiveContext).ToList();
        Assert.Equal(2, picture.Count);
        foreach (var ((x, y, size), module) in codes.Zip([5, 3]))
        {
            Assert.Equal(0, canvas.InkLeft(picture, x, y, size, size));
            var mask = Assert.Single(picture, r => r.X <= x && r.Y <= y && r.X + r.Width >= x + size && r.Y + r.Height >= y + size);
            Assert.True(
                mask.X >= x - (6 * module) && mask.Y >= y - (6 * module) && mask.X + mask.Width <= x + size + (6 * module) && mask.Y + mask.Height <= y + size + (6 * module),
                $"the mask {mask.X},{mask.Y} {mask.Width}x{mask.Height} reaches well past the code at {x},{y} ({size} px)");
        }
    }

    [Fact]
    public void SelectedRadioButtonsAtTheCornersOfASquareAreNotACode()
    {
        // A ring, a gap and a dot are one, one, three, one, one across too; a ring's corners are not dark.
        var canvas = new PictureCanvas(Width, Height);
        foreach (var (cx, cy) in new[] { (200, 200), (200, 320), (320, 200), (600, 400), (600, 460), (660, 400) })
        {
            foreach (var radius in new[] { 7, 12 })
            {
                Ring(canvas, cx + (radius * 30), cy, radius);
            }
        }

        Assert.Empty(QrCodeFinder.Find(canvas.Brightness()));
    }

    [Fact]
    public void TextTablesAndRulesAreNotACode()
    {
        var canvas = new PictureCanvas(Width, Height);
        for (var line = 0; line < 30; line++)
        {
            canvas.DrawText(20, 10 + (line * 22), "ghp_4f1a 9c0e mnmnmn lililil 0o0o0o 8b8b8b", scale: 1 + (line % 3));
        }

        for (var x = 700; x < 1260; x += 40)
        {
            canvas.Fill(x, 0, 1, Height, 0);
        }

        for (var y = 0; y < Height; y += 24)
        {
            canvas.Fill(700, y, 560, 1, 0);
        }

        Assert.Empty(QrCodeFinder.Find(canvas.Brightness()));
    }

    [Fact]
    public async Task AFrameWhosePictureCannotBeCheckedIsNotStored()
    {
        var ct = TestContext.Current.CancellationToken;
        _store ??= await SqliteSessionStore.OpenAsync(_path, new FixedKey(RandomNumberGenerator.GetBytes(32)), ct: ct);
        await _store.CreateSessionAsync(new NewSession("s1", At, new RemoteTool { Kind = RemoteToolKind.Rdp }, false, null), ct);
        Assert.True(_pending.TryEnqueue("s1", new StagedFrame("f1", 1_000, FrameTrigger.Click, Width, Height, null, new byte[] { 1, 2, 3 })));
        var worker = new RedactionWorker(
            _store, new Words([new OcrWord("Services", 0, 0, 40, 10)]), new Masker(), new RedactionEngine(), _pending, decoder: new Unreadable());

        Assert.True(await worker.ProcessOneAsync(ct));

        var session = (await _store.LoadSessionAsync("s1", ct))!;
        Assert.Empty(session.Frames);
        Assert.Equal(1, session.FramesPurgedUnredacted);
        Assert.Equal(1, worker.Progress.Unreadable);
    }

    public async ValueTask DisposeAsync()
    {
        if (_store is not null)
        {
            await _store.DisposeAsync();
        }

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            File.Delete(_path + suffix);
        }
    }

    /// <summary>The frame through the real worker and store; the regions the store kept for it.</summary>
    private async Task<IReadOnlyList<MaskedRegion>> RedactAsync(PictureCanvas canvas, IReadOnlyList<OcrWord> words)
    {
        var ct = TestContext.Current.CancellationToken;
        _store ??= await SqliteSessionStore.OpenAsync(_path, new FixedKey(RandomNumberGenerator.GetBytes(32)), ct: ct);
        await _store.CreateSessionAsync(new NewSession("s1", At, new RemoteTool { Kind = RemoteToolKind.Rdp }, false, null), ct);
        Assert.True(_pending.TryEnqueue("s1", new StagedFrame("f1", 1_000, FrameTrigger.Click, canvas.Width, canvas.Height, null, new byte[] { 1, 2, 3 })));

        var worker = new RedactionWorker(
            _store, new Words(words), new Masker(), new RedactionEngine(), _pending, decoder: new Decoder(canvas));
        Assert.True(await worker.ProcessOneAsync(ct));

        var frame = Assert.Single((await _store.LoadSessionAsync("s1", ct))!.Frames);
        return frame.MaskedRegions;
    }

    /// <summary>A selected radio button: a ring one sixth of its radius thick, a gap, a dot.</summary>
    private static void Ring(PictureCanvas canvas, int cx, int cy, int radius)
    {
        for (var y = -radius; y <= radius; y++)
        {
            for (var x = -radius; x <= radius; x++)
            {
                var distance = Math.Sqrt((x * x) + (y * y));
                if ((distance <= radius && distance > radius * 5 / 7.0) || distance <= radius * 3 / 7.0)
                {
                    canvas.Fill(cx + x, cy + y, 1, 1, 0);
                }
            }
        }
    }

    /// <summary>A decoder whose frames cannot give their brightness.</summary>
    private sealed class Unreadable : IFrameDecoder
    {
        public Task<DecodedFrame> DecodeAsync(ReadOnlyMemory<byte> image, CancellationToken ct = default) =>
            Task.FromResult<DecodedFrame>(new Broken(image));

        private sealed class Broken(ReadOnlyMemory<byte> encoded) : DecodedFrame(encoded, QrCodeMaskingTests.Width, QrCodeMaskingTests.Height)
        {
            public override FrameBrightness? ReadBrightness() => throw new InvalidOperationException("the pixels could not be read");
        }
    }

    /// <summary>The drawn canvas as a decoded frame whose brightness the worker can read.</summary>
    private sealed class Decoder(PictureCanvas canvas) : IFrameDecoder
    {
        public Task<DecodedFrame> DecodeAsync(ReadOnlyMemory<byte> image, CancellationToken ct = default) =>
            Task.FromResult<DecodedFrame>(new Drawn(image, canvas));
    }

    private sealed class Drawn(ReadOnlyMemory<byte> encoded, PictureCanvas canvas) : DecodedFrame(encoded, canvas.Width, canvas.Height)
    {
        public override FrameBrightness? ReadBrightness() => canvas.Brightness();
    }

    private sealed class Words(IReadOnlyList<OcrWord> words) : IFrameTextRecogniser
    {
        public Task<RecognisedText> ReadAsync(ReadOnlyMemory<byte> image, CancellationToken ct = default) =>
            Task.FromResult(new RecognisedText(words, 1.0));
    }

    private sealed class Masker : IFrameMasker
    {
        public MaskedImage Mask(ReadOnlyMemory<byte> image, IReadOnlyList<MaskedRegion> regions, int maxEdge) =>
            new([0xEE], Width, Height);
    }

    private sealed class FixedKey(byte[] key) : IStoreKeyProvider
    {
        public byte[] GetKey() => (byte[])key.Clone();
    }
}
