using RabbitMQ.Client;

namespace Common.RabbitMQ;

internal sealed class RabbitMqRawSender : IRawRabbitMqSender
{
    private static readonly CreateChannelOptions ConfirmingChannel = new(
        publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true);

    private readonly IRabbitMqConnectionProvider _connections;

    public RabbitMqRawSender(IRabbitMqConnectionProvider connections)
    {
        _connections = connections;
    }

    public async Task SendAsync(RawMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await using var channel = await _connections.CreateChannelAsync(ConfirmingChannel, cancellationToken).ConfigureAwait(false);

            await channel.ExchangeDeclareAsync(
                message.Exchange, message.ExchangeType.ToAmqpString(), durable: true, autoDelete: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var properties = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Type = message.MessageType,
                CorrelationId = message.CorrelationId,
            };

            // With publisher confirmations enabled on the channel, this await only completes once the
            // broker has actually acknowledged the message — a nack or basic.return throws
            // (PublishException) rather than completing silently, which is what makes "did this succeed"
            // a real question we can answer.
            await channel.BasicPublishAsync(
                exchange: message.Exchange,
                routingKey: message.RoutingKey,
                mandatory: true,
                basicProperties: properties,
                body: message.Body,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            RabbitMqDiagnostics.Published.Add(1, new KeyValuePair<string, object?>("exchange", message.Exchange));
        }
        catch (Exception)
        {
            // Recorded here, at the one place every publish path (plain publisher, durable publisher,
            // outbound relay) funnels through, then rethrown — each caller still decides its own retry.
            RabbitMqDiagnostics.PublishFailed.Add(1, new KeyValuePair<string, object?>("exchange", message.Exchange));
            throw;
        }
    }
}
