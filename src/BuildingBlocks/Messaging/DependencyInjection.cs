using System.Reflection;
using BuildingBlocks.Auditing;
using BuildingBlocks.Correlation;
using BuildingBlocks.Messaging.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Messaging;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the dispatcher (<see cref="ISender"/>) and the global pipeline behaviors that wrap
    /// every request, in outermost-to-innermost order. Call once at the composition root.
    /// Register handlers per module with <see cref="AddHandlersFromAssembly"/>; modules append their
    /// own behaviors (e.g. a transaction) by registering more <c>IPipelineBehavior&lt;,&gt;</c> after this.
    /// </summary>
    public static IServiceCollection AddMediator(this IServiceCollection services)
    {
        services.TryAddScoped<ISender, Sender>();

        // Correlation id for the current scope; set by middleware per request, by the dispatcher per
        // outbox message. Shared by audit and the outbox writer.
        services.TryAddScoped<ICorrelationContext, CorrelationContext>();

        // Audit: structured-logging sink + a default actor; the host overrides the actor (last wins).
        services.TryAddScoped<IAuditSink, LoggingAuditSink>();
        services.TryAddScoped<ICurrentActor, SystemActor>();

        // Outermost to innermost. Audit sits outside validation so rejected commands are still audited;
        // it only wraps IAuditableRequest, so reads pass straight through.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }

    /// <summary>
    /// Scans <paramref name="assembly"/> for <see cref="IRequestHandler{TRequest,TResponse}"/>
    /// implementations and registers each as scoped. Each module calls this for its own assembly.
    /// </summary>
    public static IServiceCollection AddHandlersFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        var handlers = assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });

        foreach (var handler in handlers)
        {
            var handlerInterfaces = handler.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>));

            foreach (var handlerInterface in handlerInterfaces)
            {
                services.AddScoped(handlerInterface, handler);
            }
        }

        return services;
    }
}
