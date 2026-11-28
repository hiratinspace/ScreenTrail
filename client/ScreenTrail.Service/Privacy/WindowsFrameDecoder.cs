using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ScreenTrail.Core.Privacy;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;

namespace ScreenTrail.Service.Privacy;

/// <summary>
/// Decodes a captured frame once, for the OCR engine to read and the masker to paint (ST-031).
///
/// The recogniser used to decode the JPEG to read it and the masker decoded it again, with a different
/// decoder, to paint on it. Now one decode, by the same Windows decoder the recogniser always used, gives
/// a 32-bit picture at the frame's own size; the recogniser reads it (scaling it up the way it always
/// did) and the masker paints those same pixels in place. The picture is the frame's only decoded copy and
/// it lives until the masker is done with it: the queue still holds the encoded JPEG (ADR-0006).
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class WindowsFrameDecoder : IFrameDecoder
{
    public async Task<DecodedFrame> DecodeAsync(ReadOnlyMemory<byte> image, CancellationToken ct = default)
    {
        using var stream = WindowsImages.Open(image);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);

        // At the frame's own size, in the format the OCR engine and the masker both take, as captured:
        // the captured JPEG carries no colour profile and no orientation, and neither is applied.
        var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(false);

        try
        {
            // The masker paints this picture in place, so it has to be one that can be written to.
            if (bitmap.IsReadOnly)
            {
                var writable = SoftwareBitmap.Copy(bitmap);
                bitmap.Dispose();
                bitmap = writable;
            }

            return new WindowsDecodedFrame(image, bitmap);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}

/// <summary>A frame decoded once: a 32-bit blue, green, red, alpha picture at the size it was captured.</summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class WindowsDecodedFrame : DecodedFrame
{
    private bool _disposed;

    public WindowsDecodedFrame(ReadOnlyMemory<byte> encoded, SoftwareBitmap bitmap)
        : base(encoded, (bitmap ?? throw new ArgumentNullException(nameof(bitmap))).PixelWidth, bitmap.PixelHeight)
    {
        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
        {
            throw new ArgumentException("A decoded frame is 32-bit blue, green, red and alpha.", nameof(bitmap));
        }

        Bitmap = bitmap;
    }

    /// <summary>The decoded picture. Read by the recogniser, then painted by the masker.</summary>
    public SoftwareBitmap Bitmap { get; }

    /// <summary>The pixels in memory, for as long as the lock is held.</summary>
    public PixelLock Lock(BitmapBufferAccessMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new PixelLock(Bitmap, mode, Width, Height);
    }

    /// <summary>
    /// The picture's brightness, row by row from the locked pixels, for the worker's checks of the picture
    /// itself (A-02). Read before the masker paints, so it is the frame as captured.
    /// </summary>
    public override FrameBrightness? ReadBrightness()
    {
        using var pixels = Lock(BitmapBufferAccessMode.Read);
        var brightness = new byte[Width * Height];
        var row = new byte[Width * 4];
        for (var y = 0; y < Height; y++)
        {
            pixels.Read((long)y * pixels.Stride, row, 0, row.Length);
            FrameBrightness.FromBgra(row, brightness.AsSpan(y * Width, Width));
        }

        Array.Clear(row);
        return new FrameBrightness(Width, Height, brightness);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            Bitmap.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// A <see cref="SoftwareBitmap"/>'s pixels, locked in place: where the first row starts and how far apart
/// the rows are. Painting through it paints the picture itself, with no copy.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class PixelLock : IDisposable
{
    private readonly BitmapBuffer _buffer;
    private readonly IMemoryBufferReference _reference;
    private readonly long _available;

    public PixelLock(SoftwareBitmap bitmap, BitmapBufferAccessMode mode, int width, int height)
    {
        _buffer = bitmap.LockBuffer(mode);
        try
        {
            var plane = _buffer.GetPlaneDescription(0);
            _reference = _buffer.CreateReference();
            var (data, capacity) = MemoryBufferByteAccess.Of(_reference);

            // Every byte the picture's rows cover must be inside the buffer, or nothing here is safe.
            var needed = plane.StartIndex + ((long)plane.Stride * (height - 1)) + (width * 4L);
            if (plane.Stride < width * 4 || needed > capacity)
            {
                throw new InvalidOperationException("The decoded picture's buffer is smaller than the picture.");
            }

            Start = data + plane.StartIndex;
            Stride = plane.Stride;
            _available = capacity - plane.StartIndex;
        }
        catch
        {
            (_reference as IDisposable)?.Dispose();
            _buffer.Dispose();
            throw;
        }
    }

    /// <summary>The first byte of the top row.</summary>
    public IntPtr Start { get; }

    /// <summary>Bytes from one row to the next.</summary>
    public int Stride { get; }

    /// <summary>Copies pixels out, for <see cref="BitmapFileStream"/>.</summary>
    public void Read(long source, byte[] destination, int offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(source);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(source + count, _available);
        Marshal.Copy(Start + (nint)source, destination, offset, count);
    }

    public void Dispose()
    {
        (_reference as IDisposable)?.Dispose();
        _buffer.Dispose();
    }
}

/// <summary>
/// The pointer behind a Windows memory buffer, through the COM interface every one of them implements
/// (<c>IMemoryBufferByteAccess</c>). Called through its table of methods rather than a declared COM
/// interface, so it works whatever the runtime's COM support, and needs no unsafe code.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal static class MemoryBufferByteAccess
{
    private static readonly Guid Iid = new("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D");

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetBufferMethod(IntPtr self, out IntPtr value, out uint capacity);

    public static (IntPtr Data, uint Capacity) Of(IMemoryBufferReference reference)
    {
        var unknown = ((IWinRTObject)reference).NativeObject.ThisPtr;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in Iid, out var access));
        try
        {
            // IUnknown's three methods come first; GetBuffer is the fourth.
            var methods = Marshal.ReadIntPtr(access);
            var getBuffer = Marshal.GetDelegateForFunctionPointer<GetBufferMethod>(Marshal.ReadIntPtr(methods, 3 * IntPtr.Size));
            Marshal.ThrowExceptionForHR(getBuffer(access, out var data, out var capacity));
            return (data, capacity);
        }
        finally
        {
            _ = Marshal.Release(access);
            GC.KeepAlive(reference);
        }
    }
}

/// <summary>Streams over a frame's bytes for the Windows decoders, without copying them first.</summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal static class WindowsImages
{
    public static IRandomAccessStream Open(ReadOnlyMemory<byte> image)
    {
        var bytes = MemoryMarshal.TryGetArray(image, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(image.ToArray(), writable: false);
        return bytes.AsRandomAccessStream();
    }
}
