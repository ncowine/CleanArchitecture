using System.Collections.Generic;

namespace Common.RabbitMQ
{
    public class RabbitMQConfig
    {
        public string BusName { get; set; }

        public string HostName { get; set; } = "localhost";

        public int Port { get; set; } = 5672;

        public string VirtualHost { get; set; } = "/";

        public string UserName { get; set; } = "guest";

        public string Password { get; set; } = "guest";

        public string ExchangeName { get; set; }

        public string ClientName { get; set; }

        public int ReconnectDelaySeconds { get; set; } = 5;

        public int OutstandingPollIntervalMilliseconds { get; set; } = 100;

        public int PrefetchCount { get; set; } = 50;

        public int HeartbeatSeconds { get; set; } = 30;

        public string ExchangeType { get; set; } = "topic";

        public List<RabbitMQSubscription> Subscriptions { get; set; } = new List<RabbitMQSubscription>();
    }
}
