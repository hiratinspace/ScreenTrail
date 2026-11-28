using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ScreenTrail.Core.Intel;

namespace ScreenTrail.Platform.Clipboard;

/// <summary>
/// One read of the clipboard's text, for the ticket hint at session start (ST-077).
///
/// The Win32 clipboard directly rather than WPF's: the service has no WPF, and the raw API has no thread
/// requirement. Read once, when a session starts, and handed to <c>TicketHint</c> which keeps digits or
/// nothing; the text itself is not kept, logged or passed on. A clipboard is whatever the technician
/// last copied, which may be a password (INV-10, ST-077 AC3). Anything that is not plain text, or a
/// clipboard held open by another process, is null.
///
/// The block is another process's memory: it is read no further than the size Windows gives for it, and
/// to its first NUL inside that (<see cref="ClipboardBlock.Text"/>, K3). The host calls this through
/// <see cref="ClipboardBlock.Bounded"/>, so an owner that renders on request cannot hold a session start.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ClipboardText
{
    private const uint CF_UNICODETEXT = 13;

    public static string? ReadOnce()
    {
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT) || !OpenClipboard(IntPtr.Zero))
        {
            return null;
        }

        try
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return ClipboardBlock.Text(pointer, (ulong)GlobalSize(handle));
            }
            finally
            {
                _ = GlobalUnlock(handle);
            }
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr handle);
}
