using System.Runtime.Versioning;
using System.Security.Cryptography;
using ScreenTrail.Core.Ipc;
using ScreenTrail.Core.Store;
using ScreenTrail.Service.Store;
using ScreenTrail.Shared.Ipc;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Tests.Windows.Store;

/// <summary>
/// ST-005 on Windows: the store key lives under DPAPI. A blob this user didn't protect (another user,
/// another machine, or a tampered file) is refused with a clear error rather than opening a store with
/// the wrong key. Full DifferentUserCannotDecrypt needs a second Windows account and stays a manual check.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiKeyProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "screentrail-tests", Guid.NewGuid().ToString("N"));

    public static bool OnWindows => OperatingSystem.IsWindows();

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs DPAPI (Windows)")]
    public void KeyIsCreatedOnceAndStable()
    {
        var provider = new DpapiKeyProvider(Path.Combine(_dir, "store.key"));

        var first = provider.GetKey();
        var second = provider.GetKey();

        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
        Assert.NotEqual(first, new byte[32]);
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs DPAPI (Windows)")]
    public void KeyFileOnDiskIsNotTheKey()
    {
        var path = Path.Combine(_dir, "store.key");
        var key = new DpapiKeyProvider(path).GetKey();

        Assert.Equal(-1, File.ReadAllBytes(path).AsSpan().IndexOf(key));
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs DPAPI (Windows)")]
    public void ForeignOrTamperedKeyFileIsRefused()
    {
        var path = Path.Combine(_dir, "store.key");
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(path, new byte[64]); // not a DPAPI blob this user protected

        Assert.Throws<StoreKeyException>(() => new DpapiKeyProvider(path).GetKey());
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs DPAPI (Windows)")]
    public async Task StoreOpensWithTheDpapiKey()
    {
        var provider = new DpapiKeyProvider(Path.Combine(_dir, "store.key"));
        var dbPath = Path.Combine(_dir, "store.db");

        await using (var store = await SqliteSessionStore.OpenAsync(dbPath, provider, ct: TestContext.Current.CancellationToken))
        {
            Assert.Equal(SqliteSessionStore.LatestSchemaVersion, await store.GetSchemaVersionAsync(TestContext.Current.CancellationToken));
        }

        await using var reopened = await SqliteSessionStore.OpenAsync(dbPath, provider, ct: TestContext.Current.CancellationToken);
        Assert.Equal(SqliteSessionStore.LatestSchemaVersion, await reopened.GetSchemaVersionAsync(TestContext.Current.CancellationToken));
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs DPAPI (Windows)")]
    public void AZeroLengthKeyWithNoStoreYieldsAFreshKey()
    {
        // F-10: the first write cut short by a power cut, before any store was made.
        var path = Path.Combine(_dir, "store.key");
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(path, []);

        var key = new DpapiKeyProvider(path).GetKey();

        Assert.Equal(32, key.Length);
        Assert.Equal(key, new DpapiKeyProvider(path).GetKey());
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs DPAPI (Windows)")]
    public void AZeroFilledKeyBesideAnEmptyStoreYieldsAFreshKey()
    {
        // The store file is made moments after the key, so a power cut can leave both, neither written.
        var path = Path.Combine(_dir, "store.key");
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(path, new byte[230]);
        File.WriteAllBytes(Path.Combine(_dir, "store.db"), []);

        Assert.Equal(32, new DpapiKeyProvider(path).GetKey().Length);
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs DPAPI (Windows)")]
    public void AnEmptyKeyBesideAStoreThatHoldsDataIsRefusedAndKept()
    {
        var path = Path.Combine(_dir, "store.key");
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(path, []);
        File.WriteAllBytes(Path.Combine(_dir, "store.db"), RandomNumberGenerator.GetBytes(8192));

        Assert.Throws<StoreKeyException>(() => new DpapiKeyProvider(path).GetKey());
        Assert.Empty(File.ReadAllBytes(path));
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs DPAPI (Windows)")]
    public void AKeyThatWillNotUnprotectIsRefusedForThatReasonAndKept()
    {
        var path = Path.Combine(_dir, "store.key");
        Directory.CreateDirectory(_dir);
        var foreign = RandomNumberGenerator.GetBytes(230);
        File.WriteAllBytes(path, foreign);

        var refused = Assert.Throws<StoreKeyException>(() => new DpapiKeyProvider(path).GetKey());

        Assert.Equal(StoreUnavailableReasons.KeyRefused, refused.Reason);
        Assert.Equal(foreign, File.ReadAllBytes(path));
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs DPAPI (Windows)")]
    public void AKeyWrittenLeavesNothingBesideIt()
    {
        var path = Path.Combine(_dir, "store.key");

        _ = new DpapiKeyProvider(path).GetKey();

        Assert.Equal(["store.key"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs DPAPI (Windows)", Timeout = 60_000)]
    public async Task AStoreOpenedWithAnotherKeyIsRefusedAndLeftAlone()
    {
        // A store made under this account's key, then a key file Windows will not unprotect in its place (a
        // password reset, a profile from another PC). The service's open serves the unavailable state; the
        // key file and the store are exactly as they were, and no new key is made.
        var ct = TestContext.Current.CancellationToken;
        var keyPath = Path.Combine(_dir, "store.key");
        var dbPath = Path.Combine(_dir, "store.db");
        await using (var store = await SqliteSessionStore.OpenAsync(dbPath, new DpapiKeyProvider(keyPath), ct: ct))
        {
            await store.CreateSessionAsync(new NewSession("s1", DateTimeOffset.UtcNow.AddHours(-1), new RemoteTool { Kind = RemoteToolKind.Rdp }, false, null), ct);
        }

        var foreign = RandomNumberGenerator.GetBytes(File.ReadAllBytes(keyPath).Length);
        File.WriteAllBytes(keyPath, foreign);
        var storeBefore = File.ReadAllBytes(dbPath);
        var served = new List<string>();

        var opened = await StoreUnavailable.OpenOrServeAsync(
            token => SqliteSessionStore.OpenAsync(dbPath, new DpapiKeyProvider(keyPath), ct: token),
            (reason, _) =>
            {
                served.Add(reason);
                return Task.FromResult(StoreUnavailableOutcome.Erase);
            },
            ct);

        Assert.Null(opened);
        Assert.Equal([StoreUnavailableReasons.KeyRefused], served);
        Assert.Equal(foreign, File.ReadAllBytes(keyPath));
        Assert.Equal(storeBefore, File.ReadAllBytes(dbPath));
    }

    [Fact(SkipUnless = nameof(OnWindows), Skip = "Needs DPAPI (Windows)", Timeout = 60_000)]
    public async Task AnUnprotectableKeyYieldsTheUnavailableStateAndDeleteEverythingThereLeavesAnEmptyFolder()
    {
        var ct = TestContext.Current.CancellationToken;
        var keyPath = Path.Combine(_dir, "store.key");
        var dbPath = Path.Combine(_dir, "store.db");
        await using (var store = await SqliteSessionStore.OpenAsync(dbPath, new DpapiKeyProvider(keyPath), ct: ct))
        {
            await store.CreateSessionAsync(new NewSession("s1", DateTimeOffset.UtcNow.AddHours(-1), new RemoteTool { Kind = RemoteToolKind.Rdp }, false, null), ct);
        }

        File.WriteAllBytes(keyPath, RandomNumberGenerator.GetBytes(230));
        string? state = null;

        var opened = await StoreUnavailable.OpenOrServeAsync(
            token => SqliteSessionStore.OpenAsync(dbPath, new DpapiKeyProvider(keyPath), ct: token),
            async (reason, token) =>
            {
                var handler = new StoreUnavailable(
                    reason,
                    () => new CapabilitiesReported { CheckedAt = DateTimeOffset.UtcNow, CanCapture = false, Checks = [] },
                    () => LocalDataEraser.MarkPending(_dir));
                state = handler.CurrentState.State;
                var caller = Guid.NewGuid();
                var issued = Assert.IsType<ConfirmationIssued>(await handler.ReplyToAsync(new RequestConfirmationCommand { RequestId = 1, Action = "erase_everything" }, caller, token));
                Assert.True((await handler.HandleAsync(new EraseAllLocalDataCommand { RequestId = 2, Confirmation = issued.Token }, caller, token)).Ok);
                return await handler.Outcome;
            },
            ct);

        Assert.Null(opened);
        Assert.Equal(CaptureStates.StoreUnavailable, state);
        var erased = LocalDataEraser.Erase(_dir);
        Assert.True(erased.Complete);
        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
