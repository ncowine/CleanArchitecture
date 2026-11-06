using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Messaging.Hosting
{
    /// <summary>
    /// Validates the configuration, then starts every bus with its handlers' messages subscribed. Connecting happens in
    /// the background, so startup never waits for the broker. Stopping closes the buses.
    /// </summary>
    internal sealed class MessagingHostedService : IHostedService
    {
        private readonly IServiceProvider provider;
        private readonly MessagingRegistry registry;
        private readonly List<RabbitMQBus> started = new List<RabbitMQBus>();

        public MessagingHostedService(IServiceProvider provider, MessagingRegistry registry)
        {
            this.provider = provider;
            this.registry = registry;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            registry.Validate();

            // Handlers live as long as the application, so their routing keys are bound for good: on their buses, or on the
            // default bus when they receive from every bus.
            foreach ((RoutingKeyAttribute key, IReadOnlyCollection<string> buses) in registry.GetHandlerRoutingKeys())
            {
                foreach (string busName in buses.Count > 0 ? buses : new[] { registry.GetDefaultBus() })
                {
                    provider.GetRequiredKeyedService<RabbitMQBus>(busName).Bind(key.Exchange, key.RoutingKey);
                }
            }

            foreach (string busName in registry.BusNames)
            {
                RabbitMQBus bus = provider.GetRequiredKeyedService<RabbitMQBus>(busName);
                bus.Start(registry.GetMessageRegistrations(busName));
                started.Add(bus);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (RabbitMQBus bus in started)
            {
                bus.Dispose();
            }

            started.Clear();
            return Task.CompletedTask;
        }
    }
}
