using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Messaging.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Messaging.Hosting
{
    /// <summary>
    /// Unhealthy while any bus has a consumer or publisher connection down. Reports each bus's buffered message count.
    /// </summary>
    internal sealed class MessagingHealthCheck : IHealthCheck
    {
        private readonly IServiceProvider provider;
        private readonly MessagingRegistry registry;

        public MessagingHealthCheck(IServiceProvider provider, MessagingRegistry registry)
        {
            this.provider = provider;
            this.registry = registry;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            List<string> down = new List<string>();

            foreach (BusRegistration registration in registry.Buses)
            {
                RabbitMQBus bus = provider.GetRequiredKeyedService<RabbitMQBus>(registration.Name);
                data[$"{registration.Name}.consumer"] = bus.IsConsumerConnected ? "connected" : "disconnected";
                data[$"{registration.Name}.publisher"] = bus.IsPublisherConnected ? "connected" : "disconnected";
                data[$"{registration.Name}.outstanding"] = bus.OutstandingCount;

                if (!bus.IsConsumerConnected || !bus.IsPublisherConnected)
                {
                    down.Add(registration.Name);
                }
            }

            HealthCheckResult result = down.Count == 0
                ? HealthCheckResult.Healthy("All buses connected.", data)
                : new HealthCheckResult(context.Registration.FailureStatus, $"Not connected: {string.Join(", ", down.OrderBy(n => n))}.", data: data);

            return Task.FromResult(result);
        }
    }
}
