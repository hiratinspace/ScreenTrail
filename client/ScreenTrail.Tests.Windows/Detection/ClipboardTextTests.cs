using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ScreenTrail.Core.Intel;
using ScreenTrail.Platform.Clipboard;

namespace ScreenTrail.Tests.Windows.Detection;

/// <summary>
/// K3, against the real clipboard: the text another process put there is read no further than its block,
/// and an owner that renders on request and never answers does not hold a session start. These tests
/// replace what is on the clipboard and leave it empty.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ClipboardTextTests
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint PM_REMOVE = 0x0001;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    [Fact(Timeout = 60_000)]
    public async Task AnUnterminatedBlockIsReadToItsEndAndNoFurther()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        var ct = TestContext.Current.CancellationToken;

        // 1,024 characters and no terminator: a size the allocator gives exactly, so the block ends there.
        const int Characters = 1_024;
        await OpenAsync(IntPtr.Zero, ct);
        try
        {
            Assert.True(EmptyClipboard());
            var block = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)(Characters * sizeof(char)));
            Assert.NotEqual(IntPtr.Zero, block);
            var pointer = GlobalLock(block);
            Marshal.Copy(new string('A', Characters).ToCharArray(), 0, pointer, Characters);
            _ = GlobalUnlock(block);
            Assert.NotEqual(IntPtr.Zero, SetClipboardData(CF_UNICODETEXT, block));
        }
        finally
        {
            _ = CloseClipboard();
        }

        try
        {
            var text = await Task.Run(ClipboardText.ReadOnce, ct);

            Assert.Equal(new string('A', Characters), text);
        }
        finally
        {
            await ClearAsync(ct);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task AnOwnerThatNeverRendersDoesNotHoldTheStart()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        var ct = TestContext.Current.CancellationToken;

        using var owner = new SilentOwner();
        await owner.PromiseTextAsync(ct);
        try
        {
            var read = ClipboardBlock.Bounded(ClipboardText.ReadOnce, ClipboardBlock.LongestWait);
            var clock = Stopwatch.StartNew();

            var text = await Task.Run(read, ct);

            // Loose on purpose: a shared runner is slow, and what is being shown is that the start is not
            // held for as long as the owner takes (here, until the test lets it go).
            Assert.Null(text);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"The read held the start for {clock.Elapsed}.");
        }
        finally
        {
            owner.Release();
            await ClearAsync(ct);
        }
    }

    private static async Task OpenAsync(IntPtr window, CancellationToken ct)
    {
        // Another program can hold the clipboard for a moment; try for a few seconds.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (OpenClipboard(window))
            {
                return;
            }

            await Task.Delay(50, ct);
        }

        Assert.Fail("The clipboard could not be opened.");
    }

    private static async Task ClearAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                _ = EmptyClipboard();
                _ = CloseClipboard();
                return;
            }

            await Task.Delay(50, ct);
        }
    }

    /// <summary>
    /// A clipboard owner on a thread of its own that promises text (delayed rendering) and then does not
    /// look at its messages, so a request for the text waits on it until <see cref="Release"/>.
    /// </summary>
    private sealed class SilentOwner : IDisposable
    {
        private readonly TaskCompletionSource _promised = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _released = new(false);
        private readonly Thread _thread;

        public SilentOwner()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "silent clipboard owner" };
        }

        public Task PromiseTextAsync(CancellationToken ct)
        {
            _thread.Start();
            return _promised.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
        }

        public void Release() => _released.Set();

        public void Dispose()
        {
            _released.Set();
            _ = _thread.Join(TimeSpan.FromSeconds(30));
            _released.Dispose();
        }

        private void Run()
        {
            var window = CreateWindowEx(0, "STATIC", string.Empty, 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            try
            {
                var opened = false;
                for (var attempt = 0; window != IntPtr.Zero && attempt < 100 && !opened; attempt++)
                {
                    opened = OpenClipboard(window);
                    if (!opened)
                    {
                        Thread.Sleep(50);
                    }
                }

                if (!opened)
                {
                    _promised.TrySetException(new InvalidOperationException("The owner could not take the clipboard."));
                    return;
                }

                // Delayed rendering: text is promised with no memory behind it.
                _ = EmptyClipboard();
                _ = SetClipboardData(CF_UNICODETEXT, IntPtr.Zero);
                _ = CloseClipboard();
                if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
                {
                    _promised.TrySetException(new InvalidOperationException("The owner's promise was not taken."));
                    return;
                }

                _promised.TrySetResult();

                // Silent: no message is looked at until the test is done.
                _released.Wait();

                // Now answer whatever waited, with nothing (the window renders nothing), and go.
                var clock = Stopwatch.StartNew();
                while (clock.Elapsed < TimeSpan.FromSeconds(2))
                {
                    while (PeekMessage(out _, IntPtr.Zero, 0, 0, PM_REMOVE))
                    {
                    }

                    Thread.Sleep(20);
                }
            }
            finally
            {
                if (window != IntPtr.Zero)
                {
                    _ = DestroyWindow(window);
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out Msg message, IntPtr window, uint filterMin, uint filterMax, uint remove);
}
