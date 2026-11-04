using System.Threading;
using System.Threading.Tasks;
using Messaging.RabbitMQ;

namespace Messaging.Hosting
{
    /// <summary>
    /// Publishing for a server that sends through an outbox (ADR 0003). Unlike <see cref="IMessagePublisher"/>, which
    /// buffers and returns, this completes only once the broker has confirmed the message.
    /// </summary>
    public interface IConfirmedMessagePublisher
    {
        /// <summary>
        /// Sends <paramref name="message"/> to the default bus and waits for the broker's confirm. Delivery is at least
        /// once, so receivers must tolerate duplicates.
        /// </summary>
        /// <param name="messageId">Sent as the message-id property; null for a new one. Pass the outbox row's ID.</param>
        /// <exception cref="BrokerUnavailableException">The bus couldn't reach the broker. Try again later.</exception>
        Task PublishConfirmedAsync(object message, string messageId, CancellationToken cancellationToken = default);

        /// <summary>As the other overload, with per-call settings such as the bus or the routing key.</summary>
        Task PublishConfirmedAsync(object message, string messageId, PublishOptions options, CancellationToken cancellationToken = default);
    }
}
