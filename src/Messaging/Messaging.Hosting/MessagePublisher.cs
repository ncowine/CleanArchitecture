using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;

namespace Messaging.Hosting
{
    internal sealed class MessagePublisher : IMessagePublisher
    {
        private readonly IServiceProvider provider;
        private readonly MessagingRegistry registry;

        public MessagePublisher(IServiceProvider provider, MessagingRegistry registry)
        {
            this.provider = provider;
            this.registry = registry;
        }

        public void Publish<TMessage>(TMessage message)
        {
            Publish(message, null);
        }

        public void Publish<TMessage>(TMessage message, PublishOptions options)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            string routingKey = !string.IsNullOrEmpty(options?.RoutingKey) ? options.RoutingKey : null;
            GetBusToPublishOn(options).Publish(WireNames.GetWireName(message.GetType()), message, routingKey);
        }

        private RabbitMQBus GetBusToPublishOn(PublishOptions options)
        {
            string busName = !string.IsNullOrEmpty(options?.Bus) ? options.Bus : registry.DefaultBus;
            if (!registry.BusNames.Contains(busName))
            {
                throw new InvalidOperationException($"Bus '{busName}' is not configured.");
            }

            return provider.GetRequiredKeyedService<RabbitMQBus>(busName);
        }
    }
}
