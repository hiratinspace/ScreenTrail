using System.Security.Cryptography;
using ScreenTrail.Core.Capture;
using ScreenTrail.Core.Privacy;
using ScreenTrail.Core.Store;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Tests.Privacy;

/// <summary>
/// A-02 (a): text the recogniser read no word of is masked in the stored picture, not kept readable
/// because the engine read something else on the frame. The frame is kept, and the areas that look like
/// text where no word was read are covered.
///
/// The frames are drawn: a heading the fake recogniser returns, in the top tenth, and below it rows of
/// small text it returns no word of, as a remote viewer shrunk to fit gives the engine. Every drawn ink
/// pixel is known, so what is asked is about the picture: of the ink below the read band, how much is
/// left uncovered.
/// </summary>
public sealed class TextCoverageTests : IAsyncDisposable
{
    private const int Width = 1280;
    private const int Height = 720;

    /// <summary>The top tenth of the frame, the only part the recogniser reads.</summary>
    private const int ReadBand = Height / 10;

    private static readonly DateTimeOffset At = new(2026, 11, 28, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] Unread =
    [
        "ghp_4f1a9c0e7b2d55e1 password: winter2026",
        "pass: tangerine4417wobble",
        "api_key = 8b8b1f0d4e2a96c3fa0e",
        "user: svc_backup pwd: mnr0ckl1ght",
        "token: 0e9d7c1b2a3f4e5d6c7b8a90",
        "wifi key: lavender8812tumble",
    ];

    private readonly string _path = Path.Combine(Path.GetTempPath(), "screentrail-tests", $"{Guid.NewGuid():N}.db");
    private readonly PendingFrames _pending = new(depth: 10);
    private SqliteSessionStore? _store;

    /// <summary>
    /// The ways the engine misses text: small, softened by a viewer's resize, low contrast on a dark
    /// terminal, and the same softened.
    /// </summary>
    public static TheoryData<string> Looks => ["small", "softened", "terminal", "terminal softened", "larger"];

    [Theory]
    [MemberData(nameof(Looks))]
    public async Task TextReadOnlyInTheTopTenthLeavesNoUnmaskedTextBelow(string look)
    {
        var terminal = look.StartsWith("terminal", StringComparison.Ordinal);
        var canvas = new PictureCanvas(Width, Height, paper: terminal ? (byte)40 : (byte)250);
        var ink = terminal ? (byte)100 : (byte)20;
        var heading = canvas.DrawText(24, 20, "remote session", scale: 3, ink: ink);
        var scale = look == "larger" ? 2 : 1;
        for (var row = 0; row * 14 * scale < Height - ReadBand - 30; row++)
        {
            canvas.DrawText(40 + (row % 3 * 150), ReadBand + 12 + (row * 14 * scale), Unread[row % Unread.Length], scale, ink);
        }

        if (look.EndsWith("softened", StringComparison.Ordinal))
        {
            canvas.Soften(0, ReadBand, Width, Height - ReadBand);
        }

        // The recogniser read the heading and nothing else.
        var regions = await RedactAsync(canvas, [new OcrWord("remote", heading.X, heading.Y, heading.Width / 2, heading.Height), new OcrWord("session", heading.X + (heading.Width / 2), heading.Y, heading.Width / 2, heading.Height)]);

        var below = canvas.Ink(0, ReadBand, Width, Height - ReadBand);
        var left = canvas.InkLeft(regions, 0, ReadBand, Width, Height - ReadBand);
        Assert.True(below > 0, "no text was drawn below the read band, so the frame proves nothing");
        Assert.True(left == 0, $"{left} of {below} ink pixels below the read band are not masked ({look}), within {canvas.WhereInkIsLeft(regions, 0, ReadBand, Width, Height - ReadBand)}; the stored regions are [{string.Join("; ", regions.Select(r => $"{r.Kind} {r.X},{r.Y} {r.Width}x{r.Height}"))}]");

        // What the engine read stays readable: it is in the stored text, and nothing covers it.
        Assert.Equal(canvas.Ink(heading.X, heading.Y, heading.Width, heading.Height), canvas.InkLeft(regions, heading.X, heading.Y, heading.Width, heading.Height));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task APageReadWholeHasNothingMoreMasked(int scale)
    {
        // The frame a technician screenshots most: every line read. Nothing is covered beyond the text's
        // own matches, so a frame read well looks as it did before.
        var canvas = new PictureCanvas(Width, Height);
        var words = new List<OcrWord>();
        for (var row = 0; row * 14 * scale < Height - 40; row++)
        {
            var line = canvas.DrawText(40 + (row % 3 * 150), 20 + (row * 14 * scale), "printer cleared on the third floor", scale);
            words.Add(new OcrWord("printer cleared on the third floor", line.X, line.Y, line.Width, line.Height));
        }

        var regions = await RedactAsync(canvas, words);

        Assert.DoesNotContain(regions, r => r.Kind == MaskKind.SensitiveContext);
    }

    [Fact]
    public async Task ALineReadInPartIsMaskedWhereItWasNotRead()
    {
        var canvas = new PictureCanvas(Width, Height);
        var read = canvas.DrawText(40, 200, "user: svc_backup", scale: 1);
        var unread = canvas.DrawText(read.X + read.Width + 6, 200, "pwd: mnr0ckl1ght winter2026", scale: 1);
        var heading = canvas.DrawText(40, 20, "session", scale: 3);

        var regions = await RedactAsync(canvas, [new OcrWord("session", heading.X, heading.Y, heading.Width, heading.Height), new OcrWord("user: svc_backup", read.X, read.Y, read.Width, read.Height)]);

        Assert.Equal(0, canvas.InkLeft(regions, unread.X, unread.Y, unread.Width, unread.Height));
        Assert.Equal(canvas.Ink(heading.X, heading.Y, heading.Width, heading.Height), canvas.InkLeft(regions, heading.X, heading.Y, heading.Width, heading.Height));
    }

    [Fact]
    public async Task PicturesPanelsRulesAndIconsAreNotTakenForText()
    {
        // What is on a screen beside text: a photograph (noise, and the same softened), a gradient, panels
        // and their borders, a table's rules, and icons alone on a toolbar. None of it is text.
        var canvas = new PictureCanvas(Width, Height, paper: 243);
        var heading = canvas.DrawText(40, 20, "remote desktop", scale: 3);
        var random = new Random(4417);
        for (var y = 100; y < 400; y++)
        {
            for (var x = 40; x < 440; x++)
            {
                canvas.Pixels[(y * Width) + x] = (byte)random.Next(256);
            }
        }

        for (var y = 100; y < 400; y++)
        {
            for (var x = 480; x < 880; x++)
            {
                canvas.Pixels[(y * Width) + x] = (byte)(40 + ((x - 480) * 180 / 400) + random.Next(-6, 7));
            }
        }

        canvas.Soften(40, 250, 400, 150);
        canvas.Fill(920, 100, 320, 300, 255);
        canvas.Fill(920, 100, 320, 1, 120);
        canvas.Fill(920, 399, 320, 1, 120);
        canvas.Fill(920, 100, 1, 300, 120);
        canvas.Fill(1239, 100, 1, 300, 120);
        for (var y = 130; y < 400; y += 24)
        {
            canvas.Fill(920, y, 320, 1, 200);
        }

        for (var x = 1000; x < 1240; x += 80)
        {
            canvas.Fill(x, 100, 1, 300, 200);
        }

        for (var x = 60; x < 1200; x += 120)
        {
            canvas.Fill(x, 460, 12, 12, 60);
            canvas.Fill(x + 3, 463, 6, 6, 243);
        }

        var regions = await RedactAsync(canvas, [new OcrWord("remote desktop", heading.X, heading.Y, heading.Width, heading.Height)]);

        Assert.True(
            !regions.Any(r => r.Kind == MaskKind.SensitiveContext),
            $"masked as text: [{string.Join("; ", regions.Select(r => $"{r.X},{r.Y} {r.Width}x{r.Height}"))}]");
    }

    [Fact]
    public void LookingAtAFullHdFrameStaysWithinItsBudget()
    {
        // The cost every frame now pays on top of OCR (ST-041 budgets 700 ms for the whole frame): the QR
        // search and the coverage check over a full HD frame dense with text the engine read none of.
        var canvas = new PictureCanvas(1920, 1080);
        for (var row = 0; row * 12 < 1060; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                canvas.DrawText(10 + (column * 480), 10 + (row * 12), Unread[(row + column) % Unread.Length][..Math.Min(36, Unread[(row + column) % Unread.Length].Length)]);
            }
        }

        var brightness = canvas.Brightness();
        var times = new List<double>();
        for (var run = 0; run < 5; run++)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var codes = QrCodeFinder.Find(brightness);
            var unread = TextCoverage.Unread(brightness, [], codes.Codes);
            times.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            Assert.True(codes.Complete);
            Assert.NotEmpty(unread);
        }

        times.Sort();
        Assert.True(times[2] < 150, $"looking at a full HD frame took a median of {times[2]:F0} ms");
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
        var session = $"s{Guid.NewGuid():N}";
        await _store.CreateSessionAsync(new NewSession(session, At, new RemoteTool { Kind = RemoteToolKind.Rdp }, false, null), ct);
        Assert.True(_pending.TryEnqueue(session, new StagedFrame("f1", 1_000, FrameTrigger.Click, canvas.Width, canvas.Height, null, new byte[] { 1, 2, 3 })));

        var worker = new RedactionWorker(
            _store, new Words(words), new Masker(), new RedactionEngine(), _pending, decoder: new Decoder(canvas));
        Assert.True(await worker.ProcessOneAsync(ct));

        var frame = Assert.Single((await _store.LoadSessionAsync(session, ct))!.Frames);
        return frame.MaskedRegions;
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
