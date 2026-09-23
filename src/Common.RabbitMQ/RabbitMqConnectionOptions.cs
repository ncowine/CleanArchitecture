namespace Common.RabbitMQ;

/// <summary>Connection settings for the single AMQP connection this process keeps open.</summary>
public sealed class RabbitMqConnectionOptions
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public bool UseTls { get; set; }
    public TimeSpan NetworkRecoveryInterval { get; set; } = TimeSpan.FromSeconds(5);
    public string ClientProvidedName { get; set; } = "common-rabbitmq";

    /// <summary>Extra cluster nodes (host, port) to fail over to beyond <see cref="HostName"/>/<see cref="Port"/>.
    /// Empty by default — a single node. RabbitMQ.Client tries each in turn until one accepts the connection,
    /// and automatic recovery reconnects to whichever is reachable if the current one drops.</summary>
    public IList<(string HostName, int Port)> AdditionalNodes { get; } = [];
}
