using System.Threading;
using System.Threading.Tasks;
using Messaging.RabbitMQ;

namespace Messaging.Hosting
{
    /// <summary>
    /// Publishing for a server that sends through an outbox (ADR 0003). Unlike <see cref="IMessagePublisher"/>, which
    /// buffers and returns, this completes only once the broker has confirmed the message on every bus it's routed to.
    /// </summary>
    public interface IConfirmedMessagePublisher
    {
        /// <summary>
        /// Sends <paramref name="message"/> to every bus its runtime type is routed to, with the route's routing key, and
        /// waits for each broker confirm. If it fails after some buses confirmed, calling it again sends to those buses
        /// again: delivery is at least once, so receivers must tolerate duplicates.
        /// </summary>
        /// <param name="messageId">Sent as the message-id property; null for a new one. Pass the outbox row's ID.</param>
        /// <exception cref="BrokerUnavailableException">A bus couldn't reach the broker. Try again later.</exception>
        Task PublishConfirmedAsync(object message, string messageId, CancellationToken cancellationToken = default);
    }
}
