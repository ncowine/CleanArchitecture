using Microsoft.Data.Sqlite;

namespace Common.RabbitMQ;

/// <summary>
/// Optional inbound dedup — the "marker row keyed on message id" idempotency strategy, as a reusable
/// building block instead of something every handler re-implements. At-least-once delivery means a
/// handler <em>will</em> run twice eventually; most handlers are naturally idempotent by state (an
/// upsert, a "release if reserved" check) and don't need this. Reach for it — via
/// <c>AddInboxDeduplication</c> — only when the operation is genuinely additive and has no natural field
/// to check instead (the same guidance the transactional outbox gives for its own third strategy).
/// </summary>
internal sealed class ProcessedMessageStore
{
    private readonly string _connectionString;

    public ProcessedMessageStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        Initialize();
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ProcessedMessages (
                MessageId TEXT PRIMARY KEY,
                ProcessedOnUtc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>Checked before the handler runs, so an already-succeeded delivery (redelivered because
    /// its ack was lost, not because it failed) is skipped without running the handler again.</summary>
    public async Task<bool> IsProcessedAsync(Guid messageId, CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM ProcessedMessages WHERE MessageId = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", messageId.ToString());

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    /// <summary>Recorded only after the handler has actually succeeded — never before. Marking a message
    /// processed ahead of the handler running would mean a transient failure's retry finds it already
    /// "processed" and skips the handler forever, silently losing the message instead of retrying it.</summary>
    public async Task MarkProcessedAsync(Guid messageId, CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        // OR IGNORE: two concurrent deliveries of the same id (prefetch > 1) can both pass IsProcessedAsync
        // before either marks — a narrow, accepted race the same way any optimistic dedup has one; this
        // just makes sure the second write doesn't throw.
        command.CommandText = "INSERT OR IGNORE INTO ProcessedMessages (MessageId, ProcessedOnUtc) VALUES ($id, $now);";
        command.Parameters.AddWithValue("$id", messageId.ToString());
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
