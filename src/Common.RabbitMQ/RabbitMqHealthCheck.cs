using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Common.RabbitMQ;

/// <summary>Healthy only if a channel can actually be opened right now — not just "the options are
/// configured." Register via <c>AddRabbitMqHealthCheck</c> on an <c>IHealthChecksBuilder</c>.</summary>
public sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly IRabbitMqConnectionProvider _connections;

    public RabbitMqHealthCheck(IRabbitMqConnectionProvider connections)
    {
        _connections = connections;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var channel = await _connections.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Could not open a RabbitMQ channel.", exception);
        }
    }
}
