using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Messaging.RabbitMQ;

namespace Messaging.Hosting
{
    /// <summary>
    /// Each bus's connection state, read when metrics are collected: the health check's view, as a metric a dashboard
    /// and alert rules can use.
    /// </summary>
    internal sealed class BusGauges : IDisposable
    {
        private readonly Meter meter = new Meter(MessagingTelemetry.Name);
        private readonly IReadOnlyList<RabbitMQBus> buses;

        public BusGauges(IReadOnlyList<RabbitMQBus> buses)
        {
            this.buses = buses;
            meter.CreateObservableGauge(MessagingTelemetry.Connected, ObserveConnected,
                description: "1 while the bus's consumer or publisher connection is open, 0 while it is not.");
        }

        public void Dispose()
        {
            meter.Dispose();
        }

        private IEnumerable<Measurement<int>> ObserveConnected()
        {
            foreach (RabbitMQBus bus in buses)
            {
                yield return new Measurement<int>(bus.IsConsumerConnected ? 1 : 0, Tag("messaging.bus.name", bus.BusName), Tag("messaging.connection.role", "consumer"));
                yield return new Measurement<int>(bus.IsPublisherConnected ? 1 : 0, Tag("messaging.bus.name", bus.BusName), Tag("messaging.connection.role", "publisher"));
            }
        }

        private static KeyValuePair<string, object> Tag(string key, object value)
        {
            return new KeyValuePair<string, object>(key, value);
        }
    }
}
