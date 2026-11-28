using System.Runtime.InteropServices;
using ScreenTrail.Core.Intel;

namespace ScreenTrail.Tests.Intel;

/// <summary>
/// K3: the clipboard's text is memory another process wrote. The read is held to the size Windows gives
/// for the block and to <see cref="ClipboardBlock.LongestRead"/>, and stops at the first NUL inside it,
/// whatever follows; a clipboard owner that does not answer cannot hold a session start for longer than
/// the wait the read is given.
/// </summary>
public sealed class ClipboardBlockTests
{
    [Fact]
    public void AnUnterminatedBlockIsReadNoFurtherThanItsSize()
    {
        // The block is 256 characters with no terminator. Past its end the memory goes on with more of the
        // same and only then a NUL: what is past the end is not the clipboard's.
        using var memory = Memory.Of(new string('A', 256) + new string('B', 256) + "\0");

        var text = ClipboardBlock.Text(memory.Pointer, byteSize: 256 * sizeof(char));

        Assert.Equal(new string('A', 256), text);
    }

    [Fact]
    public void ATerminatedBlockEndsAtItsFirstNul()
    {
        using var memory = Memory.Of("Ticket #48213\0left over from before\0");

        var text = ClipboardBlock.Text(memory.Pointer, byteSize: (ulong)memory.Bytes);

        Assert.Equal("Ticket #48213", text);
    }

    [Fact]
    public void ABlockLargerThanTheLongestReadGivesTheLongestRead()
    {
        using var memory = Memory.Of(new string('7', ClipboardBlock.LongestRead) + new string('8', 16) + "\0");

        var text = ClipboardBlock.Text(memory.Pointer, byteSize: 64UL * 1024 * 1024);

        Assert.Equal(new string('7', ClipboardBlock.LongestRead), text);
    }

    [Theory]
    [InlineData(0UL, "")]
    [InlineData(1UL, "")]
    [InlineData(7UL, "abc")]
    public void ABlockTooSmallForWholeCharactersIsReadToTheLastWholeOne(ulong byteSize, string expected)
    {
        using var memory = Memory.Of("abcdefgh");

        Assert.Equal(expected, ClipboardBlock.Text(memory.Pointer, byteSize));
    }

    [Fact(Timeout = 30_000)]
    public async Task AReadThatDoesNotAnswerIsGivenUpAndTheStartGoesOn()
    {
        using var never = new ManualResetEventSlim(false);
        try
        {
            var read = ClipboardBlock.Bounded(() =>
            {
                never.Wait();
                return "Ticket #48213";
            }, TimeSpan.FromMilliseconds(50));

            Assert.Null(await Task.Run(read, TestContext.Current.CancellationToken));
        }
        finally
        {
            never.Set();
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task AReadThatAnswersInTimeIsWhatTheClipboardSaid()
    {
        var read = ClipboardBlock.Bounded(() => "Ticket #48213", TimeSpan.FromSeconds(20));

        Assert.Equal("Ticket #48213", await Task.Run(read, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 30_000)]
    public async Task WhileOneReadIsStillWaitingNoSecondReadIsStarted()
    {
        using var never = new ManualResetEventSlim(false);
        var started = 0;
        try
        {
            var read = ClipboardBlock.Bounded(() =>
            {
                Interlocked.Increment(ref started);
                never.Wait();
                return "Ticket #48213";
            }, TimeSpan.FromMilliseconds(50));

            Assert.Null(await Task.Run(read, TestContext.Current.CancellationToken));
            Assert.Null(await Task.Run(read, TestContext.Current.CancellationToken));
            Assert.Equal(1, Volatile.Read(ref started));
        }
        finally
        {
            never.Set();
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task AReadThatThrowsIsNoText()
    {
        var read = ClipboardBlock.Bounded(() => throw new InvalidOperationException("held open"), TimeSpan.FromSeconds(20));

        Assert.Null(await Task.Run(read, TestContext.Current.CancellationToken));
    }

    private sealed class Memory : IDisposable
    {
        private Memory(IntPtr pointer, int bytes)
        {
            Pointer = pointer;
            Bytes = bytes;
        }

        public IntPtr Pointer { get; }

        public int Bytes { get; }

        public static Memory Of(string characters)
        {
            var bytes = characters.Length * sizeof(char);
            var pointer = Marshal.AllocHGlobal(bytes);
            Marshal.Copy(characters.ToCharArray(), 0, pointer, characters.Length);
            return new Memory(pointer, bytes);
        }

        public void Dispose() => Marshal.FreeHGlobal(Pointer);
    }
}
