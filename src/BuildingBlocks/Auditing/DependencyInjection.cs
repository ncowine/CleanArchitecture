using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Auditing;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the audit capture services: the default structured-logging sink, a default actor, the
    /// per-request scope, and <see cref="IAuditRecorder"/> for recording anything the command pipeline
    /// doesn't (reads from a third-party API, cache hits, security events). All registered with
    /// <c>TryAdd</c>, so the host can override any of them — e.g. an Elasticsearch sink for Kibana.
    /// Called for you by <c>AddMediator</c>; call it directly in a host that has no mediator.
    /// </summary>
    public static IServiceCollection AddAuditing(this IServiceCollection services)
    {
        services.TryAddScoped<IAuditSink, LoggingAuditSink>();
        services.TryAddScoped<ICurrentActor, SystemActor>();
        services.TryAddScoped<IAuditScope, AuditScope>();
        services.TryAddScoped<IAuditRecorder, AuditRecorder>();

        return services;
    }
}
