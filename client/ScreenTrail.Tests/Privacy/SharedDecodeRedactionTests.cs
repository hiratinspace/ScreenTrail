using System.Buffers.Binary;
using System.Security.Cryptography;
using ScreenTrail.Core.Capture;
using ScreenTrail.Core.Privacy;
using ScreenTrail.Core.Store;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Tests.Privacy;

/// <summary>
/// ST-031: each frame is decoded once, the recogniser reads it and the masker paints those same pixels.
/// What has to hold is INV-1 as before (nothing reaches the store unread or unpainted) and the memory rule
/// of ADR-0006: the decoded frame lives only while its own frame is redacted, and is let go of on every
/// path, including the ones that throw.
/// </summary>
public sealed class SharedDecodeRedactionTests : IAsyncDisposable
{
    private static readonly DateTimeOffset At = new(2026, 11, 22, 11, 0, 0, TimeSpan.Zero);
    private readonly string _path = Path.Combine(Path.GetTempPath(), "screentrail-tests", $"{Guid.NewGuid():N}.db");
    private readonly FixedKey _key = new(RandomNumberGenerator.GetBytes(32));
    private readonly PendingFrames _pending = new(depth: 100);
    private SqliteSessionStore? _store;

    [Fact]
    public async Task EachFrameIsDecodedOnceAndTheMaskerPaintsWhatTheRecogniserRead()
    {
        var store = await OpenAsync();
        await StageAsync(store, "s1", "f1");
        var decoder = new PixelDecoder();
        var recogniser = new PixelRecogniser { Words = [new OcrWord("Card", 1, 1, 4, 1), new OcrWord(Card(), 6, 1, 8, 1)] };
        var masker = new PaintingMasker();

        Assert.True(await Worker(store, recogniser, masker, decoder).ProcessOneAsync(TestContext.Current.CancellationToken));

        var decoded = Assert.Single(decoder.Decoded);
        Assert.Same(decoded, Assert.Single(recogniser.Read));
        Assert.Same(decoded, Assert.Single(masker.Painted));
        Assert.Equal(0, recogniser.EncodedReads);
        Assert.Equal(0, masker.EncodedMasks);

        // The region the engine found is painted in the stored pixels, and nothing else changed.
        var stored = (await store.GetRedactedFrameImageAsync("f1"))!;
        var region = Assert.Single(Assert.Single((await store.LoadSessionAsync("s1"))!.Frames).MaskedRegions);
        for (var y = 0; y < PixelDecoder.Height; y++)
        {
            for (var x = 0; x < PixelDecoder.Width; x++)
            {
                var inside = x >= region.X && x < region.X + region.Width && y >= region.Y && y < region.Y + region.Height;
                Assert.Equal(inside ? (byte)0 : PixelDecoder.Ink, stored[(y * PixelDecoder.Width) + x]);
            }
        }
    }

    [Fact]
    public async Task TheDecodedFrameIsLetGoOfBeforeTheStoreIsWritten()
    {
        var store = await OpenAsync();
        await StageAsync(store, "s1", "f1");
        var decoder = new PixelDecoder();
        var masker = new PaintingMasker();

        Assert.True(await Worker(store, Reads("Services"), masker, decoder).ProcessOneAsync(TestContext.Current.CancellationToken));

        var decoded = Assert.Single(decoder.Decoded);
        Assert.False(masker.SawDisposed, "the masker was given a frame already let go of");
        Assert.True(decoded.Disposed);
        Assert.Single((await store.LoadSessionAsync("s1"))!.Frames);
    }

    public static TheoryData<string> Endings => ["unread", "unreadable", "recogniser throws", "masker throws", "cancelled"];

    [Theory]
    [MemberData(nameof(Endings))]
    public async Task TheDecodedFrameIsLetGoOfHoweverTheFrameEnds(string ending)
    {
        var store = await OpenAsync();
        await StageAsync(store, "s1", "f1");
        var decoder = new PixelDecoder();
        using var cancel = new CancellationTokenSource();
        var recogniser = ending switch
        {
            "unread" => new PixelRecogniser(),
            "unreadable" => new PixelRecogniser { Words = [new OcrWord("blurry", 0, 0, 4, 1)], Confidence = 0.05 },
            "recogniser throws" => new PixelRecogniser { Throw = true },
            "cancelled" => new PixelRecogniser { Words = [new OcrWord("Services", 0, 0, 4, 1)], OnRead = cancel.Cancel },
            _ => Reads("Services"),
        };
        var masker = new PaintingMasker { Throw = ending == "masker throws" };

        var worker = Worker(store, recogniser, masker, decoder);
        if (ending == "cancelled")
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.ProcessOneAsync(cancel.Token));
        }
        else
        {
            Assert.True(await worker.ProcessOneAsync(TestContext.Current.CancellationToken));
        }

        Assert.True(Assert.Single(decoder.Decoded).Disposed, $"the decoded frame was kept after the frame ended ({ending})");
        Assert.Empty((await store.LoadSessionAsync("s1"))!.Frames);
    }

    [Fact]
    public async Task AFrameThatCannotBeDecodedIsNotReadAndNotStored()
    {
        var store = await OpenAsync();
        await StageAsync(store, "s1", "f1");
        var recogniser = Reads("Services");
        var masker = new PaintingMasker();
        var worker = Worker(store, recogniser, masker, new PixelDecoder { Throw = true });

        Assert.True(await worker.ProcessOneAsync(TestContext.Current.CancellationToken));

        var session = (await store.LoadSessionAsync("s1"))!;
        Assert.Empty(session.Frames);
        Assert.Equal(1, session.FramesPurgedUnredacted);
        Assert.Equal(1, worker.Progress.Unreadable);
        Assert.Empty(recogniser.Read);
        Assert.Empty(masker.Painted);
    }

    [Fact]
    public async Task AReaderAndAMaskerThatOnlyKnowTheCapturedBytesAreGivenThem()
    {
        // The interfaces' fallbacks: an implementation that cannot use decoded pixels reads and paints
        // the captured frame, which is the same frame. Every fake elsewhere in the suite relies on this.
        var store = await OpenAsync();
        await StageAsync(store, "s1", "f1");
        var recogniser = new BytesRecogniser();
        var masker = new BytesMasker();

        Assert.True(await Worker(store, recogniser, masker, new PixelDecoder()).ProcessOneAsync(TestContext.Current.CancellationToken));

        Assert.Equal(Captured, Assert.Single(recogniser.Images));
        Assert.Equal(Captured, Assert.Single(masker.Images));
        Assert.Single((await store.LoadSessionAsync("s1"))!.Frames);
    }

    [Fact]
    public async Task WithoutADecoderTheWorkerReadsAndPaintsTheCapturedBytesAsBefore()
    {
        var store = await OpenAsync();
        await StageAsync(store, "s1", "f1");
        var recogniser = new PixelRecogniser { Words = [new OcrWord("Services", 0, 0, 4, 1)] };
        var masker = new PaintingMasker();

        Assert.True(await Worker(store, recogniser, masker, decoder: null).ProcessOneAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, recogniser.EncodedReads);
        Assert.Equal(1, masker.EncodedMasks);
        Assert.Empty(recogniser.Read);
    }

    [Fact]
    public void TheBitmapFileHeaderDescribesATopDownThirtyTwoBitPicture()
    {
        var header = BitmapFileStream.Header(3, 2);

        Assert.Equal((byte)'B', header[0]);
        Assert.Equal((byte)'M', header[1]);
        Assert.Equal(54u + 24, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(2)));
        Assert.Equal(54, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(10)));
        Assert.Equal(40, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(14)));
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(18)));
        Assert.Equal(-2, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(22)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(26)));
        Assert.Equal(32, BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(28)));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(30)));
        Assert.Equal(24u, BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(34)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(4096)]
    public void TheBitmapFileIsTheHeaderAndThenEachRowWithoutItsPadding(int chunk)
    {
        // A source with padded rows, as a platform buffer can have: the file's rows are packed.
        const int width = 3, height = 4, stride = 16;
        var source = new byte[stride * height];
        for (var i = 0; i < source.Length; i++)
        {
            source[i] = (byte)(i % stride < width * 4 ? i : 0xAA);
        }

        using var file = new BitmapFileStream(width, height, stride, (from, into, at, count) => Array.Copy(source, from, into, at, count));
        var read = new MemoryStream();
        var buffer = new byte[chunk];
        int n;
        while ((n = file.Read(buffer, 0, chunk)) > 0)
        {
            read.Write(buffer, 0, n);
        }

        var expected = BitmapFileStream.Header(width, height)
            .Concat(Enumerable.Range(0, height).SelectMany(row => source.Skip(row * stride).Take(width * 4)))
            .ToArray();
        Assert.Equal(file.Length, read.Length);
        Assert.Equal(expected, read.ToArray());
        Assert.DoesNotContain((byte)0xAA, read.ToArray().Skip(BitmapFileStream.HeaderSize));
    }

    [Fact]
    public void TheBitmapFileSeeksAndStopsAtItsEnd()
    {
        var source = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        using var file = new BitmapFileStream(2, 4, 8, (from, into, at, count) => Array.Copy(source, from, into, at, count));

        Assert.True(file.CanSeek);
        Assert.False(file.CanWrite);
        Assert.Equal(12, file.Seek(12, SeekOrigin.Current));
        Assert.Equal(BitmapFileStream.HeaderSize, file.Seek(BitmapFileStream.HeaderSize, SeekOrigin.Begin));
        var buffer = new byte[4];
        Assert.Equal(4, file.Read(buffer, 0, 4));
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, buffer);

        file.Seek(-2, SeekOrigin.End);
        Assert.Equal(2, file.Read(buffer, 0, 4));
        Assert.Equal(new byte[] { 30, 31 }, buffer[..2]);
        Assert.Equal(0, file.Read(buffer, 0, 4));
        Assert.Throws<NotSupportedException>(() => file.Write(buffer, 0, 1));
    }

    [Fact]
    public void ABitmapFileStreamRefusesAStrideNarrowerThanItsRows() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitmapFileStream(4, 1, 15, (_, _, _, _) => { }));

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

    /// <summary>A Luhn-valid card number built at run time, so no card-shaped literal is in the source.</summary>
    private static string Card() => string.Concat("4111", new string('1', 12));

    private static readonly byte[] Captured = [1, 2, 3, 4];

    private static PixelRecogniser Reads(string word) => new() { Words = [new OcrWord(word, 0, 0, 4, 1)] };

    private async Task<SqliteSessionStore> OpenAsync() => _store ??= await SqliteSessionStore.OpenAsync(_path, _key);

    private async Task StageAsync(SqliteSessionStore store, string sessionId, string frameId)
    {
        await store.CreateSessionAsync(new NewSession(sessionId, At, new RemoteTool { Kind = RemoteToolKind.Rdp }, false, null));
        Assert.True(_pending.TryEnqueue(sessionId, new StagedFrame(frameId, 1_000, FrameTrigger.Click, PixelDecoder.Width, PixelDecoder.Height, null, Captured)));
    }

    private RedactionWorker Worker(SqliteSessionStore store, IFrameTextRecogniser recogniser, IFrameMasker masker, IFrameDecoder? decoder) =>
        new(store, recogniser, masker, new RedactionEngine(), _pending, decoder: decoder);

    /// <summary>One byte a pixel, every pixel <see cref="Ink"/>: enough to see what was painted.</summary>
    private sealed class PixelFrame(ReadOnlyMemory<byte> encoded) : DecodedFrame(encoded, PixelDecoder.Width, PixelDecoder.Height)
    {
        public byte[] Pixels { get; } = Enumerable.Repeat(PixelDecoder.Ink, PixelDecoder.Width * PixelDecoder.Height).ToArray();

        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class PixelDecoder : IFrameDecoder
    {
        public const int Width = 20;
        public const int Height = 4;
        public const byte Ink = 0x7F;

        public List<PixelFrame> Decoded { get; } = [];

        public bool Throw { get; init; }

        public Task<DecodedFrame> DecodeAsync(ReadOnlyMemory<byte> image, CancellationToken ct = default)
        {
            if (Throw)
            {
                throw new InvalidOperationException("not an image this decoder understands");
            }

            var frame = new PixelFrame(image);
            Decoded.Add(frame);
            return Task.FromResult<DecodedFrame>(frame);
        }
    }

    private sealed class PixelRecogniser : IFrameTextRecogniser
    {
        public IReadOnlyList<OcrWord> Words { get; init; } = [];

        public double Confidence { get; init; } = 0.95;

        public bool Throw { get; init; }

        public Action? OnRead { get; init; }

        public List<DecodedFrame> Read { get; } = [];

        public int EncodedReads { get; private set; }

        public Task<RecognisedText> ReadAsync(ReadOnlyMemory<byte> image, CancellationToken ct = default)
        {
            EncodedReads++;
            return Task.FromResult(new RecognisedText(Words, Confidence));
        }

        public Task<RecognisedText> ReadAsync(DecodedFrame frame, CancellationToken ct = default)
        {
            Read.Add(frame);
            OnRead?.Invoke();
            ct.ThrowIfCancellationRequested();
            return Throw
                ? throw new InvalidOperationException("the recogniser fell over")
                : Task.FromResult(new RecognisedText(Words, Confidence));
        }
    }

    /// <summary>Paints each region in the decoded pixels and stores those pixels, as the Windows masker does.</summary>
    private sealed class PaintingMasker : IFrameMasker
    {
        public List<DecodedFrame> Painted { get; } = [];

        public int EncodedMasks { get; private set; }

        public bool SawDisposed { get; private set; }

        public bool Throw { get; init; }

        public MaskedImage Mask(ReadOnlyMemory<byte> image, IReadOnlyList<MaskedRegion> regions, int maxEdge)
        {
            EncodedMasks++;
            return new MaskedImage(image.ToArray(), PixelDecoder.Width, PixelDecoder.Height);
        }

        public MaskedImage Mask(DecodedFrame frame, IReadOnlyList<MaskedRegion> regions, int maxEdge)
        {
            Painted.Add(frame);
            var pixels = (PixelFrame)frame;
            SawDisposed |= pixels.Disposed;
            if (Throw)
            {
                throw new InvalidOperationException("the masker fell over");
            }

            foreach (var region in regions)
            {
                for (var y = (int)region.Y; y < region.Y + region.Height; y++)
                {
                    pixels.Pixels.AsSpan((y * frame.Width) + (int)region.X, (int)region.Width).Clear();
                }
            }

            return new MaskedImage([.. pixels.Pixels], frame.Width, frame.Height);
        }
    }

    private sealed class BytesRecogniser : IFrameTextRecogniser
    {
        public List<byte[]> Images { get; } = [];

        public Task<RecognisedText> ReadAsync(ReadOnlyMemory<byte> image, CancellationToken ct = default)
        {
            Images.Add(image.ToArray());
            return Task.FromResult(new RecognisedText([new OcrWord("Services", 0, 0, 4, 1)], 0.95));
        }
    }

    private sealed class BytesMasker : IFrameMasker
    {
        public List<byte[]> Images { get; } = [];

        public MaskedImage Mask(ReadOnlyMemory<byte> image, IReadOnlyList<MaskedRegion> regions, int maxEdge)
        {
            Images.Add(image.ToArray());
            return new MaskedImage([0xEE], PixelDecoder.Width, PixelDecoder.Height);
        }
    }

    private sealed class FixedKey(byte[] key) : IStoreKeyProvider
    {
        public byte[] GetKey() => (byte[])key.Clone();
    }
}
