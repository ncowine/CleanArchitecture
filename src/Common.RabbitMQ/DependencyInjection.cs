using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Common.RabbitMQ;

public static class DependencyInjection
{
    /// <summary>Registers the connection, the type/topology registries, and a plain (non-durable)
    /// publisher. Call once at the composition root. After building the host, resolve
    /// <see cref="IMessageTypeRegistry"/> and <see cref="ITopologyRegistry"/> and register every message
    /// this process publishes or consumes — there's no attribute scanning; it's explicit on purpose.</summary>
    public static IServiceCollection AddRabbitMq(this IServiceCollection services, Action<RabbitMqConnectionOptions> configure)
    {
        var options = new RabbitMqConnectionOptions();
        configure(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IMessageTypeRegistry, MessageTypeRegistry>();
        services.TryAddSingleton<ITopologyRegistry, TopologyRegistry>();
        services.TryAddSingleton<IRabbitMqConnectionProvider, RabbitMqConnectionProvider>();
        services.TryAddSingleton<IRawRabbitMqSender, RabbitMqRawSender>();
        services.TryAddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();

        return services;
    }

    /// <summary>
    /// Swaps in the disk-durable publisher, for a host with no database-backed outbox of its own (the WPF
    /// client). API hosts should skip this: the transactional outbox already provides the same guarantee,
    /// and layering a second retry mechanism underneath would just duplicate it.
    /// </summary>
    public static IServiceCollection AddDurablePublishing(this IServiceCollection services, string databasePath)
    {
        services.AddSingleton(new PendingMessageStore(databasePath));
        services.RemoveAll<IRabbitMqPublisher>();
        services.AddSingleton<IRabbitMqPublisher, DurableRabbitMqPublisher>();
        services.AddHostedService<RabbitMqOutboundRelay>();

        return services;
    }

    /// <summary>Adds one queue this process consumes from. Call once per queue; a single background
    /// service handles all of them.</summary>
    public static IServiceCollection AddRabbitMqConsumer(this IServiceCollection services, QueueSubscription subscription)
    {
        services.AddSingleton(subscription);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RabbitMqConsumerHostedService>());

        return services;
    }

    /// <summary>
    /// Turns on inbound dedup (<see cref="ProcessedMessageStore"/>) for every consumer in this process.
    /// Most handlers don't need this — they're naturally idempotent by state. Reach for it only when a
    /// handler is genuinely additive with no natural field to check instead; see the store's own docs.
    /// </summary>
    public static IServiceCollection AddInboxDeduplication(this IServiceCollection services, string databasePath)
    {
        services.AddSingleton(new ProcessedMessageStore(databasePath));

        return services;
    }

    /// <summary>Registers a health check that's only healthy when a channel can actually be opened right
    /// now against the broker — not just "the options are configured."</summary>
    public static IHealthChecksBuilder AddRabbitMqHealthCheck(this IHealthChecksBuilder builder, string name = "rabbitmq")
    {
        builder.Services.TryAddSingleton<RabbitMqHealthCheck>();
        return builder.Add(new HealthCheckRegistration(name, provider => provider.GetRequiredService<RabbitMqHealthCheck>(), failureStatus: null, tags: null));
    }
}
