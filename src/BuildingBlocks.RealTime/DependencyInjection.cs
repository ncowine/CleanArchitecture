using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.RealTime;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the realtime collector and the post-commit dispatch behavior, plus a no-op notifier and an
    /// in-memory presence tracker as defaults. Call once, <b>after</b> <c>AddMediator</c> and <b>before</b>
    /// the modules add their transaction behaviors, so the dispatch behavior sits outside the transaction and
    /// its flush runs after the commit. The host overrides <see cref="IRealtimeNotifier"/> with a real
    /// transport (e.g. SignalR).
    /// </summary>
    public static IServiceCollection AddRealtimeDispatch(this IServiceCollection services)
    {
        services.AddScoped<RealtimeDispatch>();
        services.TryAddScoped<IRealtimeDispatch>(provider => provider.GetRequiredService<RealtimeDispatch>());
        services.TryAddScoped<IRealtimeNotifier, NullRealtimeNotifier>();
        services.TryAddSingleton<IPresenceTracker, InMemoryPresenceTracker>();

        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(RealtimeDispatchBehavior<,>));

        return services;
    }
}
