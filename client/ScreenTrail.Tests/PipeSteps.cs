using System.Globalization;

namespace ScreenTrail.Tests;

/// <summary>
/// A step of a pipe test with a time limit (security review B-01, fix forward 2). Linked into the Windows
/// suite, whose pipe tests use it for every wait.
///
/// The Windows suite runs its tests one at a time, so a connect, read or write that never ends holds up
/// every test after it until the job is cancelled, and those tests never run. Each step is handed a token
/// that is cancelled at its limit and is waited for a little longer than that. A step that does not stop
/// when cancelled is left behind rather than waited for. <see cref="Within(TimeSpan, string, Func{CancellationToken, Task})"/>
/// fails the test with the step's name when the limit is reached; <see cref="TryWithin(TimeSpan, Func{CancellationToken, Task})"/>
/// says whether the step finished, for a step whose result the test can go on without. Anything else the
/// step throws is passed on as it is, so a test that tells a refused connect by its exception still can.
/// <c>PipeTestWaitsTests</c> holds the Windows pipe tests to these.
/// </summary>
internal static class PipeSteps
{
    /// <summary>How long a step is still waited for once its token has been cancelled.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    public static async Task Within(TimeSpan limit, string what, Func<CancellationToken, Task> step) =>
        _ = await Within(limit, what, async token =>
        {
            await step(token);
            return true;
        });

    public static async Task<T> Within<T>(TimeSpan limit, string what, Func<CancellationToken, Task<T>> step)
    {
        var (finished, result) = await TryWithin(limit, step);
        if (!finished)
        {
            Assert.Fail($"{what} did not finish within {limit.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s");
        }

        return result!;
    }

    public static async Task<bool> TryWithin(TimeSpan limit, Func<CancellationToken, Task> step)
    {
        var (finished, _) = await TryWithin(limit, async token =>
        {
            await step(token);
            return true;
        });
        return finished;
    }

    public static async Task<(bool Finished, T? Result)> TryWithin<T>(TimeSpan limit, Func<CancellationToken, Task<T>> step)
    {
        var test = TestContext.Current.CancellationToken;
        using var limited = CancellationTokenSource.CreateLinkedTokenSource(test);
        limited.CancelAfter(limit);
        var running = step(limited.Token);

        if (await Task.WhenAny(running, Task.Delay(limit + Grace, test)) != running)
        {
            test.ThrowIfCancellationRequested();
            _ = running.ContinueWith(left => left.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return (false, default);
        }

        try
        {
            return (true, await running);
        }
        catch (OperationCanceledException) when (limited.IsCancellationRequested && !test.IsCancellationRequested)
        {
            return (false, default);
        }
    }
}
