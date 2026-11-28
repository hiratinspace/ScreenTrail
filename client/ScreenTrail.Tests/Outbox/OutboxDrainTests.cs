using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using ScreenTrail.Core.Outbox;
using ScreenTrail.Core.Store;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Tests.Outbox;

/// <summary>
/// F-07: the queue keeps working through a store error. One error in one turn costs that turn, and a row
/// this build cannot read (a newer build's kind after a downgrade, a damaged date) is moved to failed by
/// its id, so it stops being the oldest due row and the work behind it goes.
/// </summary>
public sealed class OutboxDrainTests : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "screentrail-tests", $"{Guid.NewGuid():N}.db");
    private readonly FixedKey _key = new(RandomNumberGenerator.GetBytes(32));
    private SqliteSessionStore? _store;

    [Fact(Timeout = 30_000)]
    public async Task AStoreErrorInOneTurnDoesNotStopTheNextDrain()
    {
        var ct = TestContext.Current.CancellationToken;
        var at = DateTimeOffset.UtcNow;
        var store = new ThrowsOnceStore();
        store.Items.Add(Item("o1", "s1", OutboxKind.Draft, at.AddMinutes(-1)));
        var sent = new List<string>();
        var outbox = new Core.Outbox.Outbox(store, (item, _) =>
        {
            sent.Add(item.Id);
            return Task.FromResult(SendOutcome.Done("r1"));
        });
        var failures = new List<string>();
        var turns = 0;

        await outbox.RunAsync(
            _ => Task.FromResult(++turns <= 3),
            waiting: null,
            failed: failure => failures.Add(failure.GetType().Name),
            ct);

        Assert.Equal(["o1"], sent);
        Assert.Equal([nameof(IOException)], failures);
        Assert.Equal(4, turns);
    }

    [Fact(Timeout = 30_000)]
    public async Task ARowWithAKindThisBuildDoesNotKnowIsFailedAndTheRowBehindItIsSent()
    {
        var ct = TestContext.Current.CancellationToken;
        var at = DateTimeOffset.UtcNow.AddHours(-1);
        await SessionsAsync(ct, "s1", "s2");
        await RawRowAsync("a", "s1", "PublishReceipt", "pending", Iso(at), Iso(at), ct);
        await RawRowAsync("b", "s2", "Draft", "pending", Iso(at.AddMinutes(5)), Iso(at.AddMinutes(5)), ct);

        var (outbox, sent) = Sending(await OpenAsync(ct));

        Assert.True(await outbox.DrainAsync(ct));

        Assert.Equal(["s2"], sent);
        Assert.Equal(("failed", Unreadable), await RawStateAsync("a", ct));
    }

    [Theory(Timeout = 30_000)]
    [InlineData("created_at")]
    [InlineData("due_at")]
    [InlineData("last_attempt_at")]
    public async Task ARowWithADateThisBuildCannotReadIsFailedAndTheRowBehindItIsSent(string column)
    {
        var ct = TestContext.Current.CancellationToken;
        var at = DateTimeOffset.UtcNow.AddHours(-1);
        await SessionsAsync(ct, "s1", "s2");
        await RawRowAsync("a", "s1", "Draft", "pending", Iso(at), Iso(at), ct);
        await RawRowAsync("b", "s2", "Draft", "pending", Iso(at.AddMinutes(5)), Iso(at.AddMinutes(5)), ct);
        await RawExecAsync($"UPDATE outbox SET {column} = 'not a date' WHERE id = 'a'", ct);

        var (outbox, sent) = Sending(await OpenAsync(ct));

        // A damaged due date is not due by its own reckoning: string order puts 'not a date' after any
        // ISO date, so it is the second row that is due first. Either way, nothing throws.
        Assert.True(await outbox.DrainAsync(ct));
        _ = await outbox.DrainAsync(ct);

        Assert.Contains("s2", sent);
        Assert.DoesNotContain("s1", sent);
        if (column != "due_at")
        {
            Assert.Equal(("failed", Unreadable), await RawStateAsync("a", ct));
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task AnUncertainRowThisBuildCannotReadIsFailedAndTheQueueGoesOn()
    {
        var ct = TestContext.Current.CancellationToken;
        var at = DateTimeOffset.UtcNow.AddHours(-1);
        await SessionsAsync(ct, "s1", "s2");
        await RawRowAsync("a", "s1", "PublishReceipt", "uncertain", Iso(at), Iso(at), ct);
        await RawRowAsync("b", "s2", "Draft", "pending", Iso(at.AddMinutes(5)), Iso(at.AddMinutes(5)), ct);

        var (outbox, sent) = Sending(await OpenAsync(ct), confirming: true);

        Assert.True(await outbox.DrainAsync(ct));

        Assert.Equal(["s2"], sent);
        Assert.Equal(("failed", Unreadable), await RawStateAsync("a", ct));
    }

    [Fact(Timeout = 30_000)]
    public async Task AnUnreadableRowIsMovedOnceAndNotTriedAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        var at = DateTimeOffset.UtcNow.AddHours(-1);
        await SessionsAsync(ct, "s1");
        await RawRowAsync("a", "s1", "PublishReceipt", "pending", Iso(at), Iso(at), ct);
        var store = await OpenAsync(ct);

        Assert.Null(await store.TakeDueAsync(DateTimeOffset.UtcNow, ct));
        Assert.Null(await store.TakeDueAsync(DateTimeOffset.UtcNow, ct));
        Assert.Equal(new OutboxWaiting(0, 0, 0), await store.CountWaitingAsync(ct));
    }

    /// <summary>The fixed reason written for a row this build cannot read; never anything from the row.</summary>
    private const string Unreadable = "This build of ScreenTrail cannot read this item.";

    private static (Core.Outbox.Outbox Outbox, List<string> Sent) Sending(IOutboxStore store, bool confirming = false)
    {
        var sent = new List<string>();
        var outbox = new Core.Outbox.Outbox(store, (item, _) =>
        {
            sent.Add(item.SessionId);
            return Task.FromResult(SendOutcome.Done(item.SessionId));
        })
        {
            // Asked whether an uncertain item landed: the far end does not know of it.
            Confirm = confirming ? (_, _) => Task.FromResult<string?>(null) : null,
        };
        return (outbox, sent);
    }

    private static OutboxItem Item(string id, string sessionId, OutboxKind kind, DateTimeOffset at) => new(
        id,
        sessionId,
        kind,
        $"{kind}:{sessionId}",
        "{}",
        OutboxState.Pending,
        Attempts: 0,
        CreatedAt: at,
        DueAt: at,
        LastAttemptAt: null,
        LastError: null,
        RemoteId: null);

    private static string Iso(DateTimeOffset at) => at.ToString("O", CultureInfo.InvariantCulture);

    private async Task SessionsAsync(CancellationToken ct, params string[] ids)
    {
        var store = await OpenAsync(ct);
        foreach (var id in ids)
        {
            await store.CreateSessionAsync(new NewSession(id, DateTimeOffset.UtcNow.AddHours(-2), new RemoteTool { Kind = RemoteToolKind.Rdp }, false, null), ct);
        }

        // Closed, so the rows below are written by a connection of their own, as an older or newer build would.
        await store.DisposeAsync();
        _store = null;
    }

    private Task RawRowAsync(string id, string session, string kind, string state, string created, string due, CancellationToken ct) =>
        RawExecAsync(
            $"INSERT INTO outbox (id, session_id, kind, idempotency_key, payload, state, attempts, created_at, due_at) VALUES ('{id}', '{session}', '{kind}', '{kind}:{session}', '{{}}', '{state}', 0, '{created}', '{due}')",
            ct);

    private async Task RawExecAsync(string sql, CancellationToken ct)
    {
        await using var connection = await RawAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        _ = await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<(string State, string? Error)> RawStateAsync(string id, CancellationToken ct)
    {
        await using var connection = await RawAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT state, last_error FROM outbox WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        Assert.True(await reader.ReadAsync(ct));
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private async Task<SqliteConnection> RawAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ConnectionString);
        await connection.OpenAsync(ct);
        StoreKey.Apply(connection, _key.GetKey());
        return connection;
    }

    private async Task<SqliteSessionStore> OpenAsync(CancellationToken ct) =>
        _store ??= await SqliteSessionStore.OpenAsync(_path, _key, ct: ct);

    public async ValueTask DisposeAsync()
    {
        if (_store is not null)
        {
            await _store.DisposeAsync();
        }

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            File.Delete(_path + suffix);
        }
    }

    /// <summary>The queue in a list whose first look for due work fails, as a full disk makes it fail.</summary>
    private sealed class ThrowsOnceStore : IOutboxStore
    {
        private int _takes;

        public List<OutboxItem> Items { get; } = [];

        public Task<bool> EnqueueAsync(OutboxItem item, CancellationToken ct = default)
        {
            Items.Add(item);
            return Task.FromResult(true);
        }

        public Task<OutboxItem?> TakeDueAsync(DateTimeOffset now, CancellationToken ct = default) =>
            ++_takes == 1
                ? throw new IOException("database or disk is full")
                : Task.FromResult(Items.FirstOrDefault(item => item.State == OutboxState.Pending && item.DueAt <= now));

        public Task<OutboxItem?> TakeUncertainAsync(CancellationToken ct = default) => Task.FromResult<OutboxItem?>(null);

        public Task<bool> UpdateAsync(OutboxItem item, CancellationToken ct = default)
        {
            var at = Items.FindIndex(existing => existing.Id == item.Id);
            if (at >= 0)
            {
                Items[at] = item;
            }

            return Task.FromResult(at >= 0);
        }

        public Task<OutboxWaiting> CountWaitingAsync(CancellationToken ct = default) =>
            Task.FromResult(new OutboxWaiting(Items.Count(item => item.State == OutboxState.Pending), 0, 0));
    }

    private sealed class FixedKey(byte[] key) : IStoreKeyProvider
    {
        public byte[] GetKey() => (byte[])key.Clone();
    }
}
