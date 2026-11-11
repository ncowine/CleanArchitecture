using System.Threading;
using System.Threading.Tasks;

namespace Messaging.RabbitMQ
{
    public interface IInboundDispatcher
    {
        Task DispatchAsync(MessageRegistration registration, object message, MessageContext context, CancellationToken cancellationToken);
    }
}
