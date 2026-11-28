using System.Security.Cryptography;
using ScreenTrail.Core.Ipc;
using ScreenTrail.Core.Store;
using ScreenTrail.Shared.Ipc;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Tests.Ipc;

/// <summary>
/// F-10: a store whose key will not open no longer ends the service at every start. The pipe is served in
/// the <c>store_unavailable</c> state, which captures nothing and says why, and takes only a retry and
/// "Delete everything" with its typed confirmation. The key file and the store are never replaced or
/// changed on the way: only the erasure takes them, and it leaves the folder empty.
/// </summary>
public sealed class StoreUnavailableTests : IAsyncDisposable
{
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(10);
    private readonly string _pipeName = IpcPipeNames.ForUser("test-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _token = IpcToken.Generate();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "screentrail-tests", Guid.NewGuid().ToString("N"));
    private IpcServer? _server;
    private int _marked;

    [Fact(Timeout = 60_000)]
    public async Task TheStateSaysNothingIsCapturedAndWhy()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = Handler(StoreUnavailableReasons.KeyRefused);
        await using var ui = await ConnectAsync(handler, ct);

        Assert.Equal(CaptureStates.StoreUnavailable, ui.State.State);
        Assert.Equal(StoreUnavailableReasons.KeyRefused, ui.State.Reason);
        Assert.Null(ui.State.SessionId);
    }

    [Fact(Timeout = 60_000)]
    public async Task EverythingButRetryAndDeleteEverythingIsRefusedInWords()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = Handler(StoreUnavailableReasons.KeyRefused);
        await using var ui = await ConnectAsync(handler, ct);

        foreach (var build in new Func<int, IpcCommand>[]
        {
            id => new StartCommand { RequestId = id },
            id => new ListSessionsCommand { RequestId = id, Limit = 10 },
            id => new GetSettingsCommand { RequestId = id },
            id => new DiscardCommand { RequestId = id, Confirmation = "x" },
            id => new RequestConfirmationCommand { RequestId = id, Action = "discard_session" },
        })
        {
            var result = await ui.SendAsync(build, ct);
            Assert.False(result.Ok);
            Assert.Equal(StoreUnavailable.Refusal, result.Error);
        }

        Assert.False(handler.Outcome.IsCompleted);
        Assert.Equal(0, _marked);
    }

    [Fact(Timeout = 60_000)]
    public async Task DeleteEverythingWithoutItsTypedConfirmationDeletesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = Handler(StoreUnavailableReasons.KeyRefused);
        await using var ui = await ConnectAsync(handler, ct);

        var bare = await ui.SendAsync(id => new EraseAllLocalDataCommand { RequestId = id }, ct);
        var forged = await ui.SendAsync(id => new EraseAllLocalDataCommand { RequestId = id, Confirmation = "made-up" }, ct);

        Assert.False(bare.Ok);
        Assert.False(forged.Ok);
        Assert.Equal(0, _marked);
        Assert.False(handler.Outcome.IsCompleted);
    }

    [Fact(Timeout = 60_000)]
    public async Task DeleteEverythingWithItsTypedConfirmationIsWrittenDownAndEndsTheState()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = Handler(StoreUnavailableReasons.KeyRefused);
        await using var ui = await ConnectAsync(handler, ct);

        var issued = await ui.RequestAsync<ConfirmationIssued>(id => new RequestConfirmationCommand { RequestId = id, Action = "erase_everything" }, ct);
        var erased = await ui.SendAsync(id => new EraseAllLocalDataCommand { RequestId = id, Confirmation = issued.Token }, ct);

        Assert.Equal("DELETE EVERYTHING", issued.Phrase);
        Assert.True(erased.Ok);
        Assert.Equal(1, _marked);
        Assert.Equal(StoreUnavailableOutcome.Erase, await handler.Outcome.WaitAsync(Soon, ct));
    }

    [Fact(Timeout = 60_000)]
    public async Task RetryEndsTheStateToTryTheStoreAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = Handler(StoreUnavailableReasons.StoreRefused);
        await using var ui = await ConnectAsync(handler, ct);

        var retried = await ui.SendAsync(id => new RetryStoreCommand { RequestId = id }, ct);

        Assert.True(retried.Ok);
        Assert.Equal(StoreUnavailableOutcome.Retry, await handler.Outcome.WaitAsync(Soon, ct));
        Assert.Equal(0, _marked);
    }

    [Fact(Timeout = 60_000)]
    public async Task AStoreThatOpensOnRetryIsTheOneUsed()
    {
        var ct = TestContext.Current.CancellationToken;
        var opens = 0;
        var served = new List<string>();

        var store = await StoreUnavailable.OpenOrServeAsync(
            _ => ++opens == 1 ? throw new StoreKeyException("refused") { Reason = StoreUnavailableReasons.KeyRefused } : Task.FromResult("the store"),
            (reason, _) =>
            {
                served.Add(reason);
                return Task.FromResult(StoreUnavailableOutcome.Retry);
            },
            ct);

        Assert.Equal("the store", store);
        Assert.Equal([StoreUnavailableReasons.KeyRefused], served);
    }

    [Fact(Timeout = 60_000)]
    public async Task AStoreOpenedWithAnotherKeyIsRefusedAndLeftAlone()
    {
        // The service's own open, with a key that is not the store's: the unavailable state is served, and the
        // store's file is exactly as it was. Nothing on the way out makes a new key or touches the data.
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "store.db");
        var key = new FixedKey(RandomNumberGenerator.GetBytes(32));
        await using (var first = await SqliteSessionStore.OpenAsync(path, key, ct: ct))
        {
            await first.CreateSessionAsync(new NewSession("s1", DateTimeOffset.UtcNow.AddHours(-1), new RemoteTool { Kind = RemoteToolKind.Rdp }, false, null), ct);
        }

        var before = File.ReadAllBytes(path);
        var served = new List<string>();

        var store = await StoreUnavailable.OpenOrServeAsync(
            token => SqliteSessionStore.OpenAsync(path, new FixedKey(RandomNumberGenerator.GetBytes(32)), ct: token),
            (reason, _) =>
            {
                served.Add(reason);
                return Task.FromResult(StoreUnavailableOutcome.Erase);
            },
            ct);

        Assert.Null(store);
        Assert.Equal([StoreUnavailableReasons.StoreRefused], served);
        Assert.Equal(before, File.ReadAllBytes(path));
        await using var reopened = await SqliteSessionStore.OpenAsync(path, key, ct: ct);
        Assert.NotNull(await reopened.LoadSessionAsync("s1", ct));
    }

    [Fact(Timeout = 60_000)]
    public async Task DeleteEverythingInTheUnavailableStateLeavesAnEmptyFolder()
    {
        // As the host does it: the confirmed erase is written down as owed, the pipe closes, and the erasure
        // runs over a folder whose store would not open. The key goes first, as always.
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "store.db");
        await using (var first = await SqliteSessionStore.OpenAsync(path, new FixedKey(RandomNumberGenerator.GetBytes(32)), ct: ct))
        {
            await first.CreateSessionAsync(new NewSession("s1", DateTimeOffset.UtcNow.AddHours(-1), new RemoteTool { Kind = RemoteToolKind.Rdp }, false, null), ct);
        }

        File.WriteAllBytes(Path.Combine(_dir, "store.key"), RandomNumberGenerator.GetBytes(230));
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{}");

        var store = await StoreUnavailable.OpenOrServeAsync(
            token => SqliteSessionStore.OpenAsync(path, new FixedKey(RandomNumberGenerator.GetBytes(32)), ct: token),
            async (reason, token) =>
            {
                var handler = new StoreUnavailable(reason, NoChecks, () => LocalDataEraser.MarkPending(_dir));
                var caller = Guid.NewGuid();
                var issued = Assert.IsType<ConfirmationIssued>(await handler.ReplyToAsync(new RequestConfirmationCommand { RequestId = 1, Action = "erase_everything" }, caller, token));
                Assert.True((await handler.HandleAsync(new EraseAllLocalDataCommand { RequestId = 2, Confirmation = issued.Token }, caller, token)).Ok);
                return await handler.Outcome;
            },
            ct);

        Assert.Null(store);
        Assert.True(LocalDataEraser.IsPending(_dir));
        var erased = LocalDataEraser.Erase(_dir);

        Assert.True(erased.Complete);
        Assert.Equal("store.key", Path.GetFileName(erased.Deleted[0]));
        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    private StoreUnavailable Handler(string reason) => new(reason, NoChecks, () => Interlocked.Increment(ref _marked));

    private static CapabilitiesReported NoChecks() => new() { CheckedAt = DateTimeOffset.UtcNow, CanCapture = false, Checks = [] };

    private async Task<IpcClient> ConnectAsync(StoreUnavailable handler, CancellationToken ct)
    {
        _server = new IpcServer(IpcServer.DefaultPipeFactory(_pipeName), _token, new AnyClient(), handler, StoreUnavailable.NoAudit, "0.0.1-test");
        _server.Start();
        await Task.Yield();
        return await IpcClient.ConnectAsync(_pipeName, _token, "test-ui", serverVerifier: null, "0.0.1", Soon, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private sealed class AnyClient : IClientVerifier
    {
        public ValueTask<string?> VerifyAsync(System.IO.Pipes.PipeStream connection, CancellationToken ct = default) => ValueTask.FromResult<string?>(null);
    }

    private sealed class FixedKey(byte[] key) : IStoreKeyProvider
    {
        public byte[] GetKey() => (byte[])key.Clone();
    }
}
