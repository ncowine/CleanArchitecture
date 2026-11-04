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

        public void Publish<TMessage>(TMessage message)
        {
            Publish(message, null);
        }

        /// <summary>The routing key is <see cref="PublishOptions.RoutingKey"/>, or the wire name.</summary>
        public void Publish<TMessage>(TMessage message, PublishOptions options)
        {
            GetBus(message, options).Enqueue(WireNames.Get(message.GetType()), message, RoutingKey(options));
        }

        public Task PublishConfirmedAsync(object message, string messageId, CancellationToken cancellationToken = default)
        {
            return PublishConfirmedAsync(message, messageId, null, cancellationToken);
        }

        public Task PublishConfirmedAsync(object message, string messageId, PublishOptions options, CancellationToken cancellationToken = default)
        {
            return GetBus(message, options).PublishConfirmedAsync(WireNames.Get(message.GetType()), message, RoutingKey(options), messageId, cancellationToken);
        }

        private static string RoutingKey(PublishOptions options)
        {
            return !string.IsNullOrEmpty(options?.RoutingKey) ? options.RoutingKey : null;
        }

        /// <summary>The bus named in <paramref name="options"/>, or the default bus.</summary>
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
                    $"Cannot publish {message.GetType().Name}: this application has {registry.BusNames.Count} buses and no default. Call UseDefaultBus, or set PublishOptions.Bus.");
            }

            if (!registry.BusNames.Contains(busName))
            {
                throw new InvalidOperationException($"Cannot publish {message.GetType().Name}: bus '{busName}' is not configured for this application.");
            }

            return provider.GetRequiredKeyedService<RabbitMQBus>(busName);
        }
    }
}
