using System.Collections.Generic;

namespace Legacy.RabbitMQ
{
    /// <summary>One application's connection: its own exchange, and the other applications' exchanges it listens to.</summary>
    public class RabbitMQConfig
    {
        public string HostName { get; set; } = "localhost";

        public int Port { get; set; } = 5672;

        /// <summary>Fact F3: every application uses the default virtual host.</summary>
        public string VirtualHost { get; set; } = "/";

        public string UserName { get; set; } = "guest";

        public string Password { get; set; } = "guest";

        /// <summary>This application's own exchange: it publishes here and declares it (fact F2, assumption A1).</summary>
        public string ExchangeName { get; set; }

        /// <summary>Other applications' exchanges this application receives from (fact F2, assumptions A9 and A16).</summary>
        public List<string> SubscribeTo { get; set; } = new List<string>();

        /// <summary>Shown in the management UI, sent as app-id and used as the queue name prefix.</summary>
        public string ClientName { get; set; }

        /// <summary>Assumption A13: how often the connect loops check the connection, and the wait before reconnecting.</summary>
        public int ReconnectDelaySeconds { get; set; } = 5;

        public int OutstandingPollIntervalMilliseconds { get; set; } = 100;

        public int PrefetchCount { get; set; } = 50;

        public int HeartbeatSeconds { get; set; } = 30;
    }
}
