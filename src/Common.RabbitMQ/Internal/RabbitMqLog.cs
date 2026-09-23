using Microsoft.Extensions.Logging;

namespace Common.RabbitMQ;

internal static partial class RabbitMqLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Connecting to RabbitMQ at {HostName}:{Port}.")]
    public static partial void Connecting(ILogger logger, string hostName, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publish to exchange '{Exchange}' failed; queued for retry.")]
    public static partial void PublishFailed(ILogger logger, Exception exception, string exchange);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Relay retry failed for pending message {MessageId} (attempt {Attempts}).")]
    public static partial void RelayRetryFailed(ILogger logger, Exception exception, Guid messageId, int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pending message {MessageId} has failed {Attempts} delivery attempts and is still queued locally.")]
    public static partial void RelayStuck(ILogger logger, Guid messageId, int attempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Received message of unknown type '{MessageType}' on queue '{Queue}'; dead-lettering.")]
    public static partial void UnknownMessageType(ILogger logger, string messageType, string queue);

    [LoggerMessage(Level = LogLevel.Error, Message = "No handler registered for message type '{MessageType}' on queue '{Queue}'; dead-lettering.")]
    public static partial void NoHandlerRegistered(ILogger logger, string messageType, string queue);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handling message on queue '{Queue}' failed (attempt {Attempt}); will retry.")]
    public static partial void DeliveryFailed(ILogger logger, Exception exception, string queue, int attempt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Message on queue '{Queue}' dead-lettered after {Attempt} attempts.")]
    public static partial void DeadLettered(ILogger logger, string queue, int attempt);
}
