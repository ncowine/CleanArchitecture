using System.Threading;
using System.Threading.Tasks;
using Messaging;
using Messaging.RabbitMQ;

namespace Common.RabbitMQ.Tests.Broker
{
    public sealed class NullDispatcher : IInboundDispatcher
    {
        public Task Dispatch(MessageRegistration registration, object message, MessageContext context, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
