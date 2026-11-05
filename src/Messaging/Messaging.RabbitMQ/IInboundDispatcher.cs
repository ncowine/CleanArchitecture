using System.Threading;
using System.Threading.Tasks;

namespace Messaging.RabbitMQ
{
    public interface IInboundDispatcher
    {
        /// <summary>
        /// Called on the consumer's thread, one message at a time. The message is acknowledged when the returned task
        /// completes, whether it succeeded or failed.
        /// </summary>
        Task Dispatch(MessageRegistration registration, object message, MessageContext context, CancellationToken cancellationToken);
    }
}
