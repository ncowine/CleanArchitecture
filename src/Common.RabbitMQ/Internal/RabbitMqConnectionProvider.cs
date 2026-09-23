using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Common.RabbitMQ;

internal sealed class RabbitMqConnectionProvider : IRabbitMqConnectionProvider
{
    private readonly RabbitMqConnectionOptions _options;
    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqConnectionProvider(RabbitMqConnectionOptions options, ILogger<RabbitMqConnectionProvider> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<IChannel> CreateChannelAsync(CreateChannelOptions? options = null, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.CreateChannelAsync(options, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                VirtualHost = _options.VirtualHost,
                UserName = _options.UserName,
                Password = _options.Password,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                NetworkRecoveryInterval = _options.NetworkRecoveryInterval,
                ClientProvidedName = _options.ClientProvidedName,
            };
            factory.Ssl.Enabled = _options.UseTls;

            var endpoints = new List<AmqpTcpEndpoint> { new(_options.HostName, _options.Port) };
            foreach (var (hostName, port) in _options.AdditionalNodes)
            {
                endpoints.Add(new AmqpTcpEndpoint(hostName, port));
            }

            RabbitMqLog.Connecting(_logger, _options.HostName, _options.Port);
            // The endpoint-list overload even for a single node: RabbitMQ.Client tries each in turn on
            // connect, and automatic recovery uses the same list to fail over if the current one drops.
            _connection = await factory.CreateConnectionAsync(endpoints, cancellationToken).ConfigureAwait(false);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            _connection.Dispose();
        }

        _gate.Dispose();
    }
}
