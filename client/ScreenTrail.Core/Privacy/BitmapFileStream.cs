using System.Buffers.Binary;

namespace ScreenTrail.Core.Privacy;

/// <summary>
/// A decoded frame's pixels seen as a 32-bit BMP file, without copying them (ST-031).
///
/// The OCR engine reads a full HD frame at twice its size, and the only scaler the platform offers works
/// on a picture being decoded. Handing it the frame's own pixels behind a BMP header lets it scale the
/// pixels the frame was already decoded to, with the same scaler, instead of decoding the JPEG a second
/// time. A BMP is not compressed, so reading one is a copy of its rows and nothing else.
///
/// Top-down, 32 bits a pixel in blue, green, red and an unused byte, 96 DPI. The pixels may have a row
/// stride wider than the picture (a platform buffer can pad its rows); the file's rows never do, so a
/// read is mapped row by row onto the source.
/// </summary>
public sealed class BitmapFileStream : Stream
{
    /// <summary>The file header and the info header: 14 and 40 bytes.</summary>
    public const int HeaderSize = 54;

    /// <summary>96 DPI in pixels a metre, which is how BMP records it.</summary>
    private const int PixelsPerMetre = 3780;

    private readonly byte[] _header;
    private readonly PixelReader _read;
    private readonly int _rowBytes;
    private readonly int _stride;
    private long _position;

    /// <param name="width">The picture's width in pixels.</param>
    /// <param name="height">Its height in pixels.</param>
    /// <param name="stride">Bytes from one row of the source to the next: four a pixel or more.</param>
    /// <param name="read">Reads the source pixels; called only for bytes inside the picture.</param>
    public BitmapFileStream(int width, int height, int stride, PixelReader read)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(read);
        var rowBytes = checked(width * 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, rowBytes);

        var length = HeaderSize + ((long)rowBytes * height);
        if (length > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "A BMP file records its size in 32 bits.");
        }

        Length = length;
        _header = Header(width, height);
        _rowBytes = rowBytes;
        _stride = stride;
        _read = read;
    }

    /// <summary>Copies <paramref name="count"/> bytes of the source pixels, from <paramref name="source"/> on.</summary>
    public delegate void PixelReader(long source, byte[] destination, int offset, int count);

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length { get; }

    public override long Position
    {
        get => _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    /// <summary>The 54 bytes in front of a top-down 32-bit picture of this size.</summary>
    public static byte[] Header(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var image = checked((uint)width * 4 * (uint)height);
        var header = new byte[HeaderSize];
        var span = header.AsSpan();

        // BITMAPFILEHEADER: the signature, the file's size, two reserved words, where the pixels start.
        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(span[2..], checked(image + HeaderSize));
        BinaryPrimitives.WriteInt32LittleEndian(span[10..], HeaderSize);

        // BITMAPINFOHEADER. A negative height is a picture stored top row first, as the frame is.
        BinaryPrimitives.WriteInt32LittleEndian(span[14..], 40);
        BinaryPrimitives.WriteInt32LittleEndian(span[18..], width);
        BinaryPrimitives.WriteInt32LittleEndian(span[22..], -height);
        BinaryPrimitives.WriteInt16LittleEndian(span[26..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[28..], 32);
        BinaryPrimitives.WriteInt32LittleEndian(span[30..], 0); // BI_RGB: not compressed
        BinaryPrimitives.WriteUInt32LittleEndian(span[34..], image);
        BinaryPrimitives.WriteInt32LittleEndian(span[38..], PixelsPerMetre);
        BinaryPrimitives.WriteInt32LittleEndian(span[42..], PixelsPerMetre);
        return header;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        var total = 0;
        while (count > 0 && _position < Length)
        {
            int n;
            if (_position < HeaderSize)
            {
                n = (int)Math.Min(count, HeaderSize - _position);
                Array.Copy(_header, _position, buffer, offset, n);
            }
            else
            {
                // Within one row of the file at a time, so each read maps onto one run of the source.
                var (row, column) = Math.DivRem(_position - HeaderSize, _rowBytes);
                n = (int)Math.Min(count, _rowBytes - column);
                _read((row * _stride) + column, buffer, offset, n);
            }

            _position += n;
            offset += n;
            count -= n;
            total += n;
        }

        return total;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
