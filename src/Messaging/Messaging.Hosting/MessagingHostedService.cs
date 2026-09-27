using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
        private readonly ILogger<MessagingHostedService> logger;
        private readonly List<RabbitMQBus> started = new List<RabbitMQBus>();

        public MessagingHostedService(IServiceProvider provider, MessagingRegistry registry, ILogger<MessagingHostedService> logger)
        {
            this.provider = provider;
            this.registry = registry;
            this.logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            registry.Validate();

            foreach (BusRegistration registration in registry.Buses)
            {
                RabbitMQBus bus = provider.GetRequiredKeyedService<RabbitMQBus>(registration.Name);
                bus.Log += OnBusLog;
                bus.Start(registry.GetMessageRegistrations(registration.Name));
                started.Add(bus);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (RabbitMQBus bus in started)
            {
                bus.Dispose();
                bus.Log -= OnBusLog;
            }

            started.Clear();
            return Task.CompletedTask;
        }

        private void OnBusLog(object sender, string message)
        {
            // Connection and failure events are logged at the right level by the telemetry observer.
            logger.LogDebug("[{Bus}] {Message}", ((RabbitMQBus)sender).BusName, message);
        }
    }
}
