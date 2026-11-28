using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Core.Privacy;

/// <param name="Words">Every word found, with where it sits in the image.</param>
/// <param name="MeanConfidence">0–1. Low confidence means the image was hard to read, which matters: a
/// secret the recogniser could not read is a secret the pattern engine cannot mask.</param>
public sealed record RecognisedText(IReadOnlyList<OcrWord> Words, double MeanConfidence)
{
    public static readonly RecognisedText Nothing = new([], 0);

    public bool IsEmpty => Words.Count == 0;
}

/// <summary>Reads the text in a frame (ST-041). The implementation is per-platform; the rules are not.</summary>
public interface IFrameTextRecogniser
{
    Task<RecognisedText> ReadAsync(ReadOnlyMemory<byte> image, CancellationToken ct = default);

    /// <summary>
    /// Reads a frame already decoded for its redaction, so the masker can paint the same pixels without
    /// decoding the frame a second time (ST-031). One that cannot use the decoded pixels reads the
    /// captured bytes instead, which is the same frame read the same way.
    /// </summary>
    Task<RecognisedText> ReadAsync(DecodedFrame frame, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return ReadAsync(frame.Encoded, ct);
    }
}

/// <param name="Image">The image with every region painted over, and downscaled for storage.</param>
public sealed record MaskedImage(byte[] Image, int Width, int Height);

/// <summary>
/// Paints the masked regions over a frame and shrinks it for storage (ST-041).
///
/// The order is the point: masking happens at native resolution, where the regions were found, and the
/// downscale happens afterwards. Shrinking first would move every box a little and leave slivers of what
/// was meant to be covered.
/// </summary>
public interface IFrameMasker
{
    MaskedImage Mask(ReadOnlyMemory<byte> image, IReadOnlyList<MaskedRegion> regions, int maxEdge);

    /// <summary>
    /// Paints the regions over the decoded frame the recogniser read and shrinks it for storage (ST-031).
    /// The painting may happen in the decoded pixels themselves, so nothing reads the frame afterwards. One
    /// that cannot use the decoded pixels paints the captured bytes instead, which is the same frame.
    /// </summary>
    MaskedImage Mask(DecodedFrame frame, IReadOnlyList<MaskedRegion> regions, int maxEdge)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return Mask(frame.Encoded, regions, maxEdge);
    }
}

/// <summary>
/// Decodes a captured frame once, for the recogniser to read and the masker to paint (ST-031).
///
/// Before this the recogniser decoded the JPEG to read it and the masker decoded it again to paint it.
/// The decoded frame lives only while one frame is being redacted: the queue still holds the encoded
/// JPEG, so what waits for redaction is as small as ADR-0006 sized it.
/// </summary>
public interface IFrameDecoder
{
    Task<DecodedFrame> DecodeAsync(ReadOnlyMemory<byte> image, CancellationToken ct = default);
}

/// <summary>
/// One captured frame, decoded once for its redaction (ST-031). The pixels are the platform's own; Core
/// only carries the frame from the recogniser to the masker and lets go of it as soon as the masker is
/// done, so at most one decoded frame per redaction reader is held at a time.
/// </summary>
public abstract class DecodedFrame : IDisposable
{
    protected DecodedFrame(ReadOnlyMemory<byte> encoded, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Encoded = encoded;
        Width = width;
        Height = height;
    }

    /// <summary>The frame as captured, for a reader or a masker that decodes it itself.</summary>
    public ReadOnlyMemory<byte> Encoded { get; }

    /// <summary>The frame's width in pixels, the coordinates every region is found and painted in.</summary>
    public int Width { get; }

    /// <summary>Its height in pixels.</summary>
    public int Height { get; }

    /// <summary>
    /// The frame's brightness, for the checks the worker makes on the picture itself (A-02): QR codes,
    /// and text the recogniser read no word of. Read before the masker paints. Null from a frame that
    /// cannot give it, and then those checks are not made.
    /// </summary>
    public virtual FrameBrightness? ReadBrightness() => null;

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Lets go of the decoded pixels.</summary>
    protected virtual void Dispose(bool disposing)
    {
    }
}
