namespace ScreenTrail.Core.Privacy;

/// <summary>
/// A frame's brightness, one byte a pixel, row after row from the top (A-02).
///
/// What Core needs of the picture itself, for what the recogniser cannot report: a QR code, which is
/// never text, and text the recogniser read no word of. The platform fills it from the decoded frame
/// (<see cref="DecodedFrame.ReadBrightness"/>), and the worker lets go of it with the decoded frame.
/// </summary>
public sealed class FrameBrightness
{
    public FrameBrightness(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.LongLength != (long)width * height)
        {
            throw new ArgumentException("A frame's brightness is one byte for each of its pixels.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Row after row from the top, <see cref="Width"/> bytes each.</summary>
    internal byte[] Pixels { get; }

    /// <summary>
    /// One row of 32-bit blue, green, red and alpha pixels as brightness, weighted as the eye weighs the
    /// three (about 0.30 red, 0.59 green, 0.11 blue).
    /// </summary>
    public static void FromBgra(ReadOnlySpan<byte> bgra, Span<byte> brightness)
    {
        if (bgra.Length < brightness.Length * 4)
        {
            throw new ArgumentException("A row of 32-bit pixels is four bytes for each brightness byte.", nameof(bgra));
        }

        for (var x = 0; x < brightness.Length; x++)
        {
            var pixel = bgra.Slice(x * 4, 3);
            brightness[x] = (byte)(((29 * pixel[0]) + (150 * pixel[1]) + (77 * pixel[2]) + 128) >> 8);
        }
    }

    /// <summary>Clears the pixels, so what the frame showed does not stay in memory after its check.</summary>
    internal void Clear() => Array.Clear(Pixels);
}
