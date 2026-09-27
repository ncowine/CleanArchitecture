using System;
using System.Collections.Generic;
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

        /// <summary>
        /// The routing key is, in order: <see cref="PublishOptions.RoutingKey"/>, the route's key
        /// (<see cref="RouteBuilder{TMessage}.WithRoutingKey(Func{TMessage, string})"/>), the wire name.
        /// </summary>
        public Task PublishAsync<TMessage>(TMessage message, PublishOptions options, CancellationToken cancellationToken = default)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            Type messageType = message.GetType();
            RouteRegistration route = GetRoute(messageType);
            string wireName = WireNames.Get(messageType);
            string routingKey = !string.IsNullOrEmpty(options?.RoutingKey) ? options.RoutingKey : route.RoutingKey?.Invoke(message);

            IReadOnlyList<string> busNames = registry.GetRouteBuses(route);
            foreach (string busName in busNames)
            {
                provider.GetRequiredKeyedService<RabbitMQBus>(busName).Enqueue(wireName, message, routingKey);
            }

            return Task.CompletedTask;
        }

        public async Task PublishConfirmedAsync(object message, string messageId, CancellationToken cancellationToken = default)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            Type messageType = message.GetType();
            RouteRegistration route = GetRoute(messageType);
            string wireName = WireNames.Get(messageType);
            string routingKey = route.RoutingKey?.Invoke(message);

            foreach (string busName in registry.GetRouteBuses(route))
            {
                await provider.GetRequiredKeyedService<RabbitMQBus>(busName)
                    .PublishConfirmedAsync(wireName, message, routingKey, messageId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private RouteRegistration GetRoute(Type messageType)
        {
            return registry.GetRoute(messageType)
                ?? throw new InvalidOperationException($"{messageType.Name} has no route. Add messaging.Route<{messageType.Name}>().");
        }
    }
}
