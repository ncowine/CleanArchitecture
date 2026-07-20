using System.Threading.Channels;

namespace BuildingBlocks.Auditing.Elasticsearch;

/// <summary>
/// A bounded in-memory hand-off between the request thread (which produces audit records) and the
/// background shipper (which bulk-sends them to Elasticsearch). Bounded + non-blocking on purpose:
/// enqueuing never blocks or fails a business command. When full, <see cref="TryEnqueue"/> returns
/// <c>false</c> and the caller falls back to logging the record instead of losing it silently.
/// </summary>
public sealed class AuditShipmentQueue
{
    private readonly Channel<AuditEntry> _channel;

    public AuditShipmentQueue(int capacity)
        => _channel = Channel.CreateBounded<AuditEntry>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait, // with TryWrite this returns false instead of blocking
        });

    public bool TryEnqueue(AuditEntry entry) => _channel.Writer.TryWrite(entry);

    public ChannelReader<AuditEntry> Reader => _channel.Reader;
}
