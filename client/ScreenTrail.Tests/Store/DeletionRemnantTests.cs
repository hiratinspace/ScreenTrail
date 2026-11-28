using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using ScreenTrail.Core.Store;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Tests.Store;

/// <summary>
/// F-08: what the technician deletes in Review, blurs, discards, or what retention purges, is gone from
/// the database file and from its write-ahead log when the command answers. The store runs in WAL mode,
/// so a delete is first a set of zeroed pages appended to <c>store.db-wal</c>; each user-facing
/// destructive write now folds the log into <c>store.db</c> and empties it before it returns.
///
/// Each test writes a screenshot that stands for one showing what masking missed, then enough ordinary
/// frames for SQLite's automatic checkpoint to have copied it into <c>store.db</c>, then does the
/// operation and reads <c>store.db</c> on its own (a copy without its log), with the test's own key.
/// </summary>
public sealed class DeletionRemnantTests : IAsyncDisposable
{
    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("REVIEW-MARKER-A-PASSWORD-ON-SCREEN-");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "screentrail-tests", Guid.NewGuid().ToString("N"));
    private readonly FixedKey _key = new(RandomNumberGenerator.GetBytes(32));
    private readonly DateTimeOffset _at = DateTimeOffset.UtcNow.AddHours(-1);
    private SqliteSessionStore? _store;

    private string DbPath => Path.Combine(_dir, "store.db");

    [Fact(Timeout = 60_000)]
    public async Task AFrameDeletedInReviewIsGoneFromTheDatabaseFileAndTheLog()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SessionWithTheSecretAsync(ct);

        Assert.True(await store.DeleteFrameAsync("f-secret", ct));

        await AssertNoRemnantAsync(ct);
    }

    [Fact(Timeout = 60_000)]
    public async Task AFrameBlurredInReviewHasNoUnblurredCopyInTheDatabaseFileOrTheLog()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SessionWithTheSecretAsync(ct);

        await store.ApplyUserBlurAsync("f-secret", new byte[64 * 1024], new MaskedRegion { X = 0, Y = 0, Width = 1920, Height = 1080, Kind = MaskKind.UserBlur }, ct);

        await AssertNoRemnantAsync(ct);
    }

    [Fact(Timeout = 60_000)]
    public async Task ADiscardedSessionIsGoneFromTheDatabaseFileAndTheLog()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SessionWithTheSecretAsync(ct);

        _ = await store.DiscardSessionAsync("s1", ct);

        await AssertNoRemnantAsync(ct);
    }

    [Fact(Timeout = 60_000)]
    public async Task TheLastChunkOfADiscardLeavesNothingInTheDatabaseFileOrTheLog()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SessionWithTheSecretAsync(ct);

        _ = await store.DiscardSessionsAsync(["s1"], ct);
        while (!await store.PurgeDiscardedContentAsync("s1", chunk: 4, ct))
        {
        }

        await AssertNoRemnantAsync(ct);
    }

    [Fact(Timeout = 60_000)]
    public async Task ARetentionPurgeLeavesNothingInTheDatabaseFileOrTheLog()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await SessionWithTheSecretAsync(ct);

        _ = await store.PurgeRawDataAsync("s1", ct);

        await AssertNoRemnantAsync(ct);
    }

    [Fact(Timeout = 60_000)]
    public async Task ARetentionPurgeAlsoTakesTheModelsFirstDraft()
    {
        // The note as the model first wrote it is kept beside the technician's edits for the publish-time
        // readers (the metric and the style signal). Raw data past retention is not kept, and nor is it.
        var ct = TestContext.Current.CancellationToken;
        var store = await OpenAsync(ct);
        await store.CreateSessionAsync(new NewSession("s1", _at, new RemoteTool { Kind = RemoteToolKind.Rdp }, false, null), ct);
        await store.SaveDraftAsync("s1", Note("Reset the password for the customer's mailbox"), ct);
        Assert.NotNull(await store.LoadOriginalDraftAsync("s1", ct));

        _ = await store.PurgeRawDataAsync("s1", ct);

        Assert.Null(await store.LoadOriginalDraftAsync("s1", ct));
        Assert.NotNull((await store.LoadSessionAsync("s1", ct))?.Draft);
    }

    [Fact(Timeout = 60_000)]
    public async Task TheLogIsHeldToALimitWhenItIsNotEmptied()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = await OpenAsync(ct);

        Assert.InRange(await store.JournalSizeLimitAsync(ct), 1, 8L * 1024 * 1024);
    }

    private async Task<SqliteSessionStore> SessionWithTheSecretAsync(CancellationToken ct)
    {
        var store = await OpenAsync(ct);
        await store.CreateSessionAsync(new NewSession("s1", _at, new RemoteTool { Kind = RemoteToolKind.Rdp }, false, null), ct);
        Assert.True(await SaveAsync(store, "f-secret", 10, Picture(64 * 1024), ct));

        // About 6 MB of ordinary frames: past SQLite's 1000-page automatic checkpoint at least once, so the
        // screenshot is in store.db itself and not only in the log.
        for (var i = 0; i < 15; i++)
        {
            Assert.True(await SaveAsync(store, $"f{i}", 100 + i, RandomNumberGenerator.GetBytes(400 * 1024), ct));
        }

        // The precondition the tests rest on: before the operation, the file on its own holds the screenshot.
        Assert.Equal(1, await RemnantRowsAsync(ct));
        return store;
    }

    private async Task AssertNoRemnantAsync(CancellationToken ct)
    {
        Assert.True(await RemnantRowsAsync(ct) == 0, "store.db on its own still holds the screenshot or the text read from it.");

        var log = new FileInfo(DbPath + "-wal");
        Assert.True(!log.Exists || log.Length == 0, $"store.db-wal still holds {log.Length} bytes after the command answered.");
    }

    /// <summary>Rows of a copy of store.db, taken without its log, that hold the screenshot's bytes or its text.</summary>
    private async Task<long> RemnantRowsAsync(CancellationToken ct)
    {
        var copy = Path.Combine(_dir, $"copy-{Guid.NewGuid():N}.db");
        File.Copy(DbPath, copy);
        try
        {
            await using var connection = await RawAsync(copy, ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM frames WHERE id = 'f-secret' AND (instr(image, CAST('REVIEW-MARKER' AS BLOB)) > 0 OR ocr_text IS NOT NULL)";
            return Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                File.Delete(copy + suffix);
            }
        }
    }

    private async Task<SqliteConnection> RawAsync(string path, CancellationToken ct)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ConnectionString);
        await connection.OpenAsync(ct);
        StoreKey.Apply(connection, _key.GetKey());
        return connection;
    }

    private Task<bool> SaveAsync(SqliteSessionStore store, string id, long ts, byte[] image, CancellationToken ct) =>
        store.SaveRedactedFrameAsync(
            "s1",
            new StagedFrame(id, ts, FrameTrigger.Click, 1920, 1080, null, ReadOnlyMemory<byte>.Empty),
            new RedactionOutcome(image, "text read from the screenshot", [], false, _at),
            ct);

    private static byte[] Picture(int bytes)
    {
        var image = new byte[bytes];
        for (var i = 0; i < image.Length; i++)
        {
            image[i] = Marker[i % Marker.Length];
        }

        return image;
    }

    private static DraftNote Note(string title) => new()
    {
        Problem = "p",
        Steps = [new DraftStep { Text = "t", Confidence = StepConfidence.High, FrameRefs = [] }],
        Result = "r",
        FollowUps = [],
        SuggestedTitle = title,
        SuggestedTimeMinutes = 30,
        KbCandidate = false,
        KbReason = "n",
        Source = DraftSource.Cloud,
        PromptVersion = "note_v2",
    };

    private async Task<SqliteSessionStore> OpenAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_dir);
        return _store ??= await SqliteSessionStore.OpenAsync(DbPath, _key, ct: ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_store is not null)
        {
            await _store.DisposeAsync();
        }

        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private sealed class FixedKey(byte[] key) : IStoreKeyProvider
    {
        public byte[] GetKey() => (byte[])key.Clone();
    }
}
