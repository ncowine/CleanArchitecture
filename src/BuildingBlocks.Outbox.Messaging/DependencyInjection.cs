using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Outbox.Messaging;

public static class DependencyInjection
{
    /// <summary>
    /// Sends <typeparamref name="TContext"/>'s outbox messages of the given classes to RabbitMQ, through the outbox
    /// processor. Each class needs <c>[Message("...")]</c> and a route (<c>messaging.Route&lt;T&gt;()</c> in
    /// <c>AddMessaging</c>). Use instead of <see cref="OutboxServiceCollectionExtensions.AddOutboxProcessing{TContext, TDispatcher}"/>
    /// for a module whose outbox only feeds the broker.
    /// </summary>
    public static IServiceCollection AddOutboxPublishing<TContext>(this IServiceCollection services, params Type[] messageTypes)
        where TContext : DbContext
    {
        services.AddSingleton(new OutboxMessageTypes<TContext>(messageTypes));
        services.AddOutboxProcessing<TContext, MessagingOutboxDispatcher<TContext>>();
        return services;
    }
}
