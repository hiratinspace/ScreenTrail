using System.Runtime.InteropServices;

namespace ScreenTrail.Core.Intel;

/// <summary>
/// How the clipboard's text is read for the ticket hint (ST-077), kept apart from the Win32 calls so the
/// Mac suite can hold it to its rules (K3).
///
/// The text is a block of memory another process wrote, and nothing makes it end in a NUL. It is read no
/// further than the size Windows reports for the block and no further than <see cref="LongestRead"/>
/// characters, and it ends at the first NUL inside that. A clipboard owner that renders on request (a
/// remote tool syncing the far machine's clipboard) can make the read wait on it, and the read runs just
/// before a session starts, so it is given <see cref="LongestWait"/> and then the start goes on without
/// a hint, which is optional.
/// </summary>
public static class ClipboardBlock
{
    /// <summary>The most characters read, whatever the block's size (TicketHint reads no more).</summary>
    public const int LongestRead = 4_096;

    /// <summary>How long a session start waits for the clipboard before it goes on without a hint.</summary>
    public static readonly TimeSpan LongestWait = TimeSpan.FromMilliseconds(250);

    /// <summary>The text of a block of <paramref name="byteSize"/> bytes, as Windows reports its size.</summary>
    public static string Text(IntPtr block, ulong byteSize)
    {
        var characters = (int)Math.Min(byteSize / sizeof(char), LongestRead);
        if (characters == 0)
        {
            return string.Empty;
        }

        var text = Marshal.PtrToStringUni(block, characters);
        var end = text.IndexOf('\0', StringComparison.Ordinal);
        return end >= 0 ? text[..end] : text;
    }

    /// <summary>
    /// <paramref name="read"/>, run on a thread of its own and given up after <paramref name="wait"/>:
    /// null when it has not answered by then or it threw. While a read that was given up is still waiting
    /// on its owner, no second one is started, so a hung owner holds one thread, not one per session.
    /// </summary>
    public static Func<string?> Bounded(Func<string?> read, TimeSpan wait)
    {
        ArgumentNullException.ThrowIfNull(read);
        var outstanding = 0;
        return () =>
        {
            if (Interlocked.CompareExchange(ref outstanding, 1, 0) != 0)
            {
                return null;
            }

            var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    _ = answer.TrySetResult(read());
                }
                catch (Exception)
                {
                    // An unreadable clipboard is no hint. What went wrong is not passed on: it may quote the text.
                    _ = answer.TrySetResult(null);
                }
                finally
                {
                    Volatile.Write(ref outstanding, 0);
                }
            })
            {
                IsBackground = true,
                Name = "ScreenTrail clipboard read",
            };
            thread.Start();

            // Not answered in time: the thread may still finish later, and what it reads then is dropped.
            return answer.Task.Wait(wait) ? answer.Task.Result : null;
        };
    }
}
