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
        _store ??= await SqliteSessionStore.OpenAsync(_path, new FixedKey(RandomNumberGenerator.GetBytes(32)), ct: ct);        await _store.CreateSessionAsync(new NewSession("s1", At, new RemoteTool { Kind = RemoteToolKind.Rdp }, false, null), ct);
        Assert.True(_pending.TryEnqueue("s1", new StagedFrame("f1", 1_000, FrameTrigger.Click, canvas.Width, canvas.Height, null, new byte[] { 1, 2, 3 })));

        var worker = new RedactionWorker(
            _store, new Words(words), new Masker(), new RedactionEngine(), _pending, decoder: new Decoder(canvas));
        Assert.True(await worker.ProcessOneAsync(ct));

        var frame = Assert.Single((await _store.LoadSessionAsync("s1", ct))!.Frames);
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
