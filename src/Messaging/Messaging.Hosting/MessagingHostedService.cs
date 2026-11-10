using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Messaging.Hosting
{
    internal sealed class MessagingHostedService : IHostedService
    {
        private readonly IServiceProvider provider;
        private readonly MessagingRegistry registry;
        private readonly List<RabbitMQBus> started = new List<RabbitMQBus>();
        private readonly ILogger logger;

        public MessagingHostedService(IServiceProvider provider, MessagingRegistry registry)
        {
            this.provider = provider;
            this.registry = registry;
            logger = provider.GetService<ILogger<MessagingHostedService>>() ?? (ILogger)NullLogger.Instance;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            registry.Validate();

            foreach ((RoutingKeyAttribute key, IReadOnlyCollection<string> buses) in registry.GetHandlerRoutingKeys())
            {
                List<RabbitMQBus> targets = buses.Count > 0
                    ? buses.Select(name => provider.GetRequiredKeyedService<RabbitMQBus>(name)).ToList()
                    : new List<RabbitMQBus> { MessageSubscriber.BindingBus(provider, registry, null, key.Exchange) };

                if (!string.IsNullOrWhiteSpace(key.Exchange))
                {
                    List<RabbitMQBus> usingExchange = targets.Where(bus => bus.UsesExchange(key.Exchange)).ToList();
                    if (usingExchange.Count == 0)
                    {
                        throw BusSelection.NoBusUses(key.Exchange);
                    }

                    foreach (RabbitMQBus skipped in targets.Except(usingExchange))
                    {
                        logger.LogInformation("[{Bus}] [RoutingKey(\"{RoutingKey}\")] on exchange '{Exchange}' is not bound on this bus: it doesn't use that exchange. It is bound on {Buses}.",
                            skipped.BusName, key.RoutingKey, key.Exchange, string.Join(", ", usingExchange.Select(bus => bus.BusName)));
                    }

                    targets = usingExchange;
                }

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
