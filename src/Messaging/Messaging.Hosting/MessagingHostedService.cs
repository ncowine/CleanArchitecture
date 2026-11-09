using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Messaging.Hosting
{
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

            foreach ((RoutingKeyAttribute key, IReadOnlyCollection<string> buses) in registry.GetHandlerRoutingKeys())
            {
                IEnumerable<RabbitMQBus> targets = buses.Count > 0
                    ? buses.Select(name => provider.GetRequiredKeyedService<RabbitMQBus>(name))
                    : new[] { MessageSubscriber.BindingBus(provider, registry, null, key.Exchange) };

                foreach (RabbitMQBus bus in targets)
                {
                    bus.Bind(key.Exchange, key.RoutingKey);
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
