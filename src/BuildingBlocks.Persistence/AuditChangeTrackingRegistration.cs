using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Persistence;

/// <summary>
/// Wiring for the audit change-tracking interceptor. Each module registers it once and attaches it to its
/// DbContext options, so writes on that context are captured for the audit trail.
/// </summary>
public static class AuditChangeTrackingRegistration
{
    /// <summary>Registers the scoped interceptor. Idempotent — safe to call from every module.</summary>
    public static IServiceCollection AddAuditChangeTracking(this IServiceCollection services)
    {
        services.TryAddScoped<AuditingSaveChangesInterceptor>();
        return services;
    }

    /// <summary>
    /// Attaches the audit interceptor to a DbContext. Use inside the <c>(sp, options) =&gt; ...</c> overload of
    /// <c>AddDbContext</c> so the scoped interceptor (and its per-request audit scope) resolve correctly.
    /// </summary>
    public static DbContextOptionsBuilder UseAuditChangeTracking(
        this DbContextOptionsBuilder options, IServiceProvider serviceProvider)
        => options.AddInterceptors(serviceProvider.GetRequiredService<AuditingSaveChangesInterceptor>());
}
