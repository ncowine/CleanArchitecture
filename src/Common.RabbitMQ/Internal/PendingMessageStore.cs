using Microsoft.Data.Sqlite;

namespace Common.RabbitMQ;

/// <summary>A pending outbound message, staged locally before (and possibly after several failed
/// attempts at) a confirmed publish.</summary>
internal sealed record PendingMessage(
    Guid Id, string Exchange, RabbitExchangeType ExchangeType, string RoutingKey,
    string MessageType, string? CorrelationId, byte[] Body, int Attempts);

/// <summary>
/// The disk-persisted retry queue behind <see cref="DurableRabbitMqPublisher"/>: a message is written
/// here before the network is ever touched, and removed only once a publish is actually confirmed — so a
/// process crash mid-publish, or the broker being unreachable, never silently drops it. Deliberately plain
/// ADO.NET (not EF Core, which doesn't support net472) — this library has to build for both TFMs. Uses
/// synchronous <c>using</c> throughout, even in async methods: net472's Microsoft.Data.Sqlite doesn't
/// implement <c>IAsyncDisposable</c> on these types the way the net10.0 build does.
/// </summary>
internal sealed class PendingMessageStore
{
    private readonly string _connectionString;

    public PendingMessageStore(string databasePath)
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
            CREATE TABLE IF NOT EXISTS PendingMessages (
                Id TEXT PRIMARY KEY,
                Exchange TEXT NOT NULL,
                ExchangeType TEXT NOT NULL,
                RoutingKey TEXT NOT NULL,
                MessageType TEXT NOT NULL,
                CorrelationId TEXT NULL,
                Body BLOB NOT NULL,
                Attempts INTEGER NOT NULL DEFAULT 0,
                CreatedOnUtc TEXT NOT NULL,
                NextAttemptOnUtc TEXT NOT NULL,
                LastAttemptOnUtc TEXT NULL,
                LastError TEXT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public async Task EnqueueAsync(Guid id, RawMessage message, CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PendingMessages (Id, Exchange, ExchangeType, RoutingKey, MessageType, CorrelationId, Body, Attempts, CreatedOnUtc, NextAttemptOnUtc)
            VALUES ($id, $exchange, $exchangeType, $routingKey, $messageType, $correlationId, $body, 0, $createdOnUtc, $createdOnUtc);
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$exchange", message.Exchange);
        command.Parameters.AddWithValue("$exchangeType", message.ExchangeType.ToString());
        command.Parameters.AddWithValue("$routingKey", message.RoutingKey);
        command.Parameters.AddWithValue("$messageType", message.MessageType);
        command.Parameters.AddWithValue("$correlationId", (object?)message.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$body", message.Body);
        command.Parameters.AddWithValue("$createdOnUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PendingMessage>> TakeBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        var results = new List<PendingMessage>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Exchange, ExchangeType, RoutingKey, MessageType, CorrelationId, Body, Attempts
            FROM PendingMessages
            WHERE NextAttemptOnUtc <= $now
            ORDER BY CreatedOnUtc
            LIMIT $batchSize;
            """;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$batchSize", batchSize);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new PendingMessage(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                ParseExchangeType(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                (byte[])reader["Body"],
                reader.GetInt32(7)));
        }

        return results;
    }

    public async Task MarkDeliveredAsync(Guid id, CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PendingMessages WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records a failed attempt and schedules the next one. <paramref name="nextAttemptOnUtc"/>
    /// is computed by the caller (via <see cref="RetryBackoff"/>) from the attempt count it already knows,
    /// rather than read back here — avoids a second round trip just to recompute the same number.</summary>
    public async Task MarkFailedAsync(Guid id, string error, DateTimeOffset nextAttemptOnUtc, CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE PendingMessages
            SET Attempts = Attempts + 1, LastAttemptOnUtc = $now, LastError = $error, NextAttemptOnUtc = $nextAttemptOnUtc
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$error", error);
        command.Parameters.AddWithValue("$nextAttemptOnUtc", nextAttemptOnUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // The generic Enum.Parse<TEnum> overload isn't available on net472.
    private static RabbitExchangeType ParseExchangeType(string value) =>
#if NET472
        (RabbitExchangeType)Enum.Parse(typeof(RabbitExchangeType), value);
#else
        Enum.Parse<RabbitExchangeType>(value);
#endif
}
