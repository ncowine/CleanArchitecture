using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Outbox;

public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers the background dispatcher for <typeparamref name="TContext"/> and the module's
    /// dispatch logic. Add once per module that publishes outbox messages.
    /// </summary>
    public static IServiceCollection AddOutboxProcessing<TContext, TDispatcher>(this IServiceCollection services)
        where TContext : DbContext
        where TDispatcher : class, IOutboxDispatcher<TContext>
    {
        services.AddScoped<IOutboxDispatcher<TContext>, TDispatcher>();
        services.AddHostedService<OutboxProcessor<TContext>>();
        return services;
    }

    /// <summary>Registers <see cref="IOutbox"/> bound to <typeparamref name="TContext"/> for modules that enqueue events.</summary>
    public static IServiceCollection AddOutboxWriter<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddScoped<IOutbox, OutboxWriter<TContext>>();
        return services;
    }

    /// <summary>Registers dead-letter inspection and replay over <typeparamref name="TContext"/>'s outbox.</summary>
    public static IServiceCollection AddOutboxAdmin<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddScoped<IDeadLetterReader, OutboxDeadLetterReader<TContext>>();
        services.AddScoped<IOutboxReplayer, OutboxReplayer<TContext>>();
        return services;
    }
}
