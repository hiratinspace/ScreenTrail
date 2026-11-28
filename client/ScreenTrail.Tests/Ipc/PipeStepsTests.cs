namespace ScreenTrail.Tests.Ipc;

/// <summary>
/// The time limit the Windows pipe tests put on each wait (<see cref="PipeSteps"/>, security review B-01,
/// fix forward 2): a step that never ends fails its test, by name, instead of holding up the run.
/// </summary>
public sealed class PipeStepsTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(100);

    [Fact(Timeout = 30_000)]
    public async Task AStepThatNeverEndsFailsTheTestAndSaysWhichStep()
    {
        // Deaf to the step's own token, as an I/O call that is stuck may be; only the test's
        // own token (its Timeout) would end it.
        var never = new TaskCompletionSource();

        var failure = await Assert.ThrowsAsync<Xunit.Sdk.FailException>(() => PipeSteps.Within(Short, "the loopback client's write", _ => never.Task.WaitAsync(TestContext.Current.CancellationToken)));

        Assert.Equal("the loopback client's write did not finish within 0.1 s", failure.Message);
    }

    [Fact(Timeout = 30_000)]
    public async Task AStepIsCancelledAtItsLimit()
    {
        CancellationToken given = default;

        var finished = await PipeSteps.TryWithin(Short, token =>
        {
            given = token;
            return Task.Delay(Timeout.InfiniteTimeSpan, token);
        });

        Assert.False(finished);
        Assert.True(given.IsCancellationRequested);
        Assert.False(TestContext.Current.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task AFinishedStepGivesItsResult()
    {
        Assert.Equal(1, await PipeSteps.Within(TimeSpan.FromSeconds(5), "the read", _ => Task.FromResult(1)));
        Assert.Equal((true, 1), await PipeSteps.TryWithin(TimeSpan.FromSeconds(5), _ => Task.FromResult(1)));
    }

    [Fact]
    public async Task WhatAStepThrowsIsPassedOn()
    {
        // A refused connect is told by its exception (UnauthorizedAccessException, IOException, a
        // TimeoutException of its own), so the limit must not turn those into something else.
        await Assert.ThrowsAsync<IOException>(() => PipeSteps.Within(TimeSpan.FromSeconds(5), "the connect", _ => Task.FromException(new IOException("refused"))));
        await Assert.ThrowsAsync<TimeoutException>(() => PipeSteps.Within(TimeSpan.FromSeconds(5), "the connect", _ => Task.FromException(new TimeoutException())));
        await Assert.ThrowsAsync<IOException>(() => PipeSteps.TryWithin(TimeSpan.FromSeconds(5), _ => Task.FromException(new IOException("broken"))));
    }
}
