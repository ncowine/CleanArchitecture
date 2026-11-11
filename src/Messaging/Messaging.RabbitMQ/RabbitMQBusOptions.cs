using System;
using System.Collections.Generic;

namespace Messaging.RabbitMQ
{
    public sealed class RabbitMQBusOptions
    {
        public string BusName { get; set; }

        public string HostName { get; set; } = "localhost";

        public int Port { get; set; } = 5672;

        public string VirtualHost { get; set; } = "/";

        public string UserName { get; set; } = "guest";

        public string Password { get; set; } = "guest";

        /// <summary>Connect over TLS (AMQPS). The broker's TLS listener is usually port 5671: set <see cref="Port"/> too.</summary>
        public bool UseTls { get; set; }

        /// <summary>The name the broker's certificate must match. Defaults to <see cref="HostName"/>.</summary>
        public string TlsServerName { get; set; }

        public string ExchangeName { get; set; }

        public string ExchangeType { get; set; } = "topic";

        public List<SubscriptionOptions> Subscriptions { get; set; } = new List<SubscriptionOptions>();

        public string ClientName { get; set; }

        public string InstanceId { get; set; }

        public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan OutstandingPollInterval { get; set; } = TimeSpan.FromMilliseconds(100);

        public ushort PrefetchCount { get; set; } = 50;

        public TimeSpan Heartbeat { get; set; } = TimeSpan.FromSeconds(30);

        public QueueMode QueueMode { get; set; } = QueueMode.PerInstance;

        public string SharedQueueName { get; set; }

        public int MaxAttempts { get; set; } = 3;

        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

        public int DeliveryLimit { get; set; } = 5;
    }
}
