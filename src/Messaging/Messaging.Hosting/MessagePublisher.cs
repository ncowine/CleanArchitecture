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
            string routingKey = !string.IsNullOrEmpty(options?.RoutingKey) ? options.RoutingKey : null;
            GetBus(message, options).Enqueue(WireNames.Get(message.GetType()), message, routingKey);
        }

        private RabbitMQBus GetBus(object message, PublishOptions options)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            string busName = !string.IsNullOrEmpty(options?.Bus) ? options.Bus : registry.GetDefaultBus();
            if (busName == null)
            {
                throw new InvalidOperationException(
                    $"Cannot publish {message.GetType().Name}: there is no default bus. Call UseDefaultBus, or set PublishOptions.Bus.");
            }

            if (!registry.BusNames.Contains(busName))
            {
                throw new InvalidOperationException($"Cannot publish {message.GetType().Name}: bus '{busName}' is not configured for this application.");
            }

            return provider.GetRequiredKeyedService<RabbitMQBus>(busName);
        }
    }
}
