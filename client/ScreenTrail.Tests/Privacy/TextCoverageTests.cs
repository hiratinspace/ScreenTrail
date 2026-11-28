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
        Assert.True(left == 0, $"{left} of {below} ink pixels below the read band are not masked ({look}); the stored regions are [{string.Join("; ", regions.Select(r => $"{r.Kind} {r.X},{r.Y} {r.Width}x{r.Height}"))}]");

        // What the engine read stays readable: it is in the stored text, and nothing covers it.
        Assert.Equal(canvas.Ink(heading.X, heading.Y, heading.Width, heading.Height), canvas.InkLeft(regions, heading.X, heading.Y, heading.Width, heading.Height));
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
