using System.Text.Json;
using Microsoft.Data.Sqlite;
using ScreenTrail.Core.Sessions;
using ScreenTrail.Core.Store;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Tests.Store;

/// <summary>
/// A frame row as a build before ADR-0006 left it: the captured image, with <c>redaction_pending = 1</c>.
///
/// The store has no way to write one any more (INV-1, review finding A-04): a frame reaches it only through
/// <see cref="ISessionStore.SaveRedactedFrameAsync"/>, already masked. A store file carried over from an
/// older build can still hold such a row, though, and the store still has to keep it from every reader,
/// count it and delete it. So the tests that check that write the row straight into the file, through a
/// connection of their own, the way the older build did, rather than through anything the product offers.
/// </summary>
internal static class LegacyPendingFrame
{
    /// <returns>Whether the row was written: false for a session that is gone or discarded, as the older build refused it.</returns>
    public static async Task<bool> WriteAsync(string path, IStoreKeyProvider key, string sessionId, StagedFrame frame, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(frame);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ConnectionString);
        await connection.OpenAsync(ct);
        StoreKey.Apply(connection, key.GetKey());

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO frames (id, session_id, ts_ms, trigger, width, height, cursor_x, cursor_y, redaction_pending, image)
            SELECT @id, @session, @ts, @trigger, @w, @h, @cx, @cy, 1, @image
            WHERE EXISTS (SELECT 1 FROM sessions owner WHERE owner.id = @session AND owner.state IS NOT @discarded)
            """;
        command.Parameters.AddWithValue("@id", frame.Id);
        command.Parameters.AddWithValue("@session", sessionId);
        command.Parameters.AddWithValue("@ts", frame.TsMs);
        command.Parameters.AddWithValue("@trigger", JsonSerializer.Serialize(frame.Trigger, SessionJson.Options).Trim('"'));
        command.Parameters.AddWithValue("@w", frame.Width);
        command.Parameters.AddWithValue("@h", frame.Height);
        command.Parameters.AddWithValue("@cx", (object?)frame.Cursor?.X ?? DBNull.Value);
        command.Parameters.AddWithValue("@cy", (object?)frame.Cursor?.Y ?? DBNull.Value);
        command.Parameters.AddWithValue("@image", frame.Image.ToArray());
        command.Parameters.AddWithValue("@discarded", SessionStateNames.Discarded);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }
}
