using RabbitMQ.Client;

namespace Common.RabbitMQ;

/// <summary>
/// Owns the single long-lived, auto-recovering AMQP connection for this process. Channels are cheap and
/// short-lived by design (RabbitMQ.Client's own guidance) — callers create one per publish or per
/// consumer, they don't share or pool them through here.
/// </summary>
public interface IRabbitMqConnectionProvider : IAsyncDisposable
{
    Task<IChannel> CreateChannelAsync(CreateChannelOptions? options = null, CancellationToken cancellationToken = default);
}
