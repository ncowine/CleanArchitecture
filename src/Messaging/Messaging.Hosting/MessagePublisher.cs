using System;
using System.Collections.Generic;
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
            RouteRegistration route = registry.GetRoute(messageType)
                ?? throw new InvalidOperationException($"{messageType.Name} has no route. Add messaging.Route<{messageType.Name}>().");
            string wireName = WireNames.Get(messageType);
            string routingKey = !string.IsNullOrEmpty(options?.RoutingKey) ? options.RoutingKey : route.RoutingKey?.Invoke(message);

            IReadOnlyList<string> busNames = registry.GetRouteBuses(route);
            foreach (string busName in busNames)
            {
                provider.GetRequiredKeyedService<RabbitMQBus>(busName).Enqueue(wireName, message, routingKey);
            }

            return Task.CompletedTask;
        }
    }
}
