using System.Reflection;
using Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Outbox.Messaging;

public static class DependencyInjection
{
    /// <summary>
    /// Sends <typeparamref name="TContext"/>'s outbox messages of the given classes to RabbitMQ, through the outbox
    /// processor, to the bus <c>AddMessaging</c> configured. Each class needs <c>[Message("...")]</c>. Use instead of <see cref="OutboxServiceCollectionExtensions.AddOutboxProcessing{TContext, TDispatcher}"/>
    /// for a module whose outbox only feeds the broker.
    /// </summary>
    public static IServiceCollection AddOutboxPublishing<TContext>(this IServiceCollection services, params Type[] messageTypes)
        where TContext : DbContext
    {
        services.AddSingleton(new OutboxMessageTypes<TContext>(messageTypes));
        services.AddOutboxProcessing<TContext, MessagingOutboxDispatcher<TContext>>();
        return services;
    }

    /// <summary>As above, for every <c>[Message]</c> class in <paramref name="assembly"/>, such as a module's *.Messages project.</summary>
    public static IServiceCollection AddOutboxPublishing<TContext>(this IServiceCollection services, Assembly assembly)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return services.AddOutboxPublishing<TContext>(assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.GetCustomAttribute<MessageAttribute>(inherit: false) != null)
            .ToArray());
    }
}
