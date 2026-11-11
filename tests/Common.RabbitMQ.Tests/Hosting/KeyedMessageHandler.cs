using System.Threading;
using System.Threading.Tasks;
using Messaging;
using Messaging.Hosting;

namespace Common.RabbitMQ.Tests.Hosting
{
    /// <summary>Binds <see cref="Key"/> on the bus's own exchange at startup, for <see cref="HostingRoutingKeyTests"/>.</summary>
    [RoutingKey(Key)]
    public sealed class KeyedMessageHandler : IMessageHandler<KeyedMessage>
    {
        public const string Key = "compat.handler-key";

        public Task HandleAsync(KeyedMessage message, MessageContext context, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
