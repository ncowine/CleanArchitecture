using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;

namespace Messaging.Hosting
{
    internal sealed class MessagePublisher : IMessagePublisher, IConfirmedMessagePublisher
    {
        private readonly IServiceProvider provider;
        private readonly MessagingRegistry registry;

        public MessagePublisher(IServiceProvider provider, MessagingRegistry registry)
        {
            this.provider = provider;
            this.registry = registry;
        }

        public Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        {
            return PublishAsync(message, null, cancellationToken);
        }

        /// <summary>The routing key is <see cref="PublishOptions.RoutingKey"/>, or the wire name.</summary>
        public Task PublishAsync<TMessage>(TMessage message, PublishOptions options, CancellationToken cancellationToken = default)
        {
            string routingKey = !string.IsNullOrEmpty(options?.RoutingKey) ? options.RoutingKey : null;
            GetBus(message).Enqueue(WireNames.Get(message.GetType()), message, routingKey);
            return Task.CompletedTask;
        }

        public Task PublishConfirmedAsync(object message, string messageId, CancellationToken cancellationToken = default)
        {
            return GetBus(message).PublishConfirmedAsync(WireNames.Get(message.GetType()), message, null, messageId, cancellationToken);
        }

        /// <summary>Every message goes to the application's only bus, as in the legacy router.</summary>
        private RabbitMQBus GetBus(object message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (registry.BusNames.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Cannot publish {message.GetType().Name}: publishing needs exactly one bus and this application has {registry.BusNames.Count}.");
            }

            return provider.GetRequiredKeyedService<RabbitMQBus>(registry.BusNames.Single());
        }
    }
}
