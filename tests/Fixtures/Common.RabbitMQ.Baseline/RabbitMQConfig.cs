namespace Common.RabbitMQ
{
    /// <summary>
    /// Connection settings for one bus. An application has one config per bus it takes part in.
    /// </summary>
    public class RabbitMQConfig
    {
        /// <summary>Logical bus name, matched against <see cref="RemoteEventAttribute.Bus"/>.</summary>
        public string BusName { get; set; }

        public string HostName { get; set; } = "localhost";

        public int Port { get; set; } = 5672;

        public string VirtualHost { get; set; } = "/";

        public string UserName { get; set; } = "guest";

        public string Password { get; set; } = "guest";

        public string ExchangeName { get; set; }

        /// <summary>Shown in the RabbitMQ management UI and used as the queue name prefix.</summary>
        public string ClientName { get; set; }

        /// <summary>How often the connect loops check for a dropped connection, and the wait before reconnecting.</summary>
        public int ReconnectDelaySeconds { get; set; } = 5;

        /// <summary>How long the outstanding queue waits when there is nothing to send (or no connection).</summary>
        public int OutstandingPollIntervalMilliseconds { get; set; } = 100;

        public int PrefetchCount { get; set; } = 50;

        public int HeartbeatSeconds { get; set; } = 30;
    }
}
