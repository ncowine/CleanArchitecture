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
    /// Starts the buses once every hosted service has started, so subscriptions made in any hosted service's
    /// <c>StartAsync</c> are in place first. That matters for a shared queue: it holds messages from while the app was
    /// stopped, and a message with no subscriber yet is dropped on arrival.
    /// </summary>
    internal sealed class MessagingHostedService : IHostedLifecycleService
    {
        private readonly IServiceProvider provider;
        private readonly MessagingRegistry registry;
        private readonly List<RabbitMQBus> started = new List<RabbitMQBus>();

        public MessagingHostedService(IServiceProvider provider, MessagingRegistry registry)
        {
            this.provider = provider;
            this.registry = registry;
        }

        public Task StartingAsync(CancellationToken cancellationToken)
        {
            registry.Validate();
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StartedAsync(CancellationToken cancellationToken)
        {
            foreach (string busName in registry.BusNames)
            {
                RabbitMQBus bus = provider.GetRequiredKeyedService<RabbitMQBus>(busName);
                bus.Start();
                started.Add(bus);
            }

            return Task.CompletedTask;
        }

        public Task StoppingAsync(CancellationToken cancellationToken)
        {
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

        public Task StoppedAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
