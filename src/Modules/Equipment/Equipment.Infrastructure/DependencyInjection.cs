using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using BuildingBlocks.Outbox.Messaging;
using BuildingBlocks.Persistence;
using Equipment.Application;
using Equipment.Application.Abstractions;
using Equipment.Contracts;
using Equipment.Infrastructure.Behaviors;
using Equipment.Infrastructure.Caching;
using Equipment.Infrastructure.Catalogue;
using Equipment.Infrastructure.Contracts;
using Equipment.Infrastructure.IntegrationEvents;
using Equipment.Infrastructure.Persistence;
using Equipment.Infrastructure.Reads;
using Equipment.Infrastructure.Repositories;
using Equipment.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Equipment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddEquipmentModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddEquipmentApplication();

        // Audit change-tracking: capture before/after values of every write for the audit trail.
        services.AddAuditChangeTracking();
        services.AddDbContext<EquipmentDbContext>((sp, options) =>
            options.UseSqlite(connectionString).UseAuditChangeTracking(sp));

        services.AddScoped<IEquipmentRepository, EfEquipmentRepository>();

        // Cache-aside single lookup: EquipmentDirectory is the cache-miss fallthrough to the database,
        // decorated with a cache (in-memory now, Redis-ready) since it's the hottest read.
        services.AddScoped<EquipmentDirectory>();
        services.AddScoped<IEquipmentDirectory>(provider => new CachingEquipmentDirectory(
            provider.GetRequiredService<EquipmentDirectory>(),
            provider.GetRequiredService<HybridCache>()));
        services.AddScoped<IEquipmentCacheInvalidator, EquipmentCacheInvalidator>();

        // Computed/merged cache: per-site equipment rollup, aggregated across many rows plus a
        // cross-database site name. Registered twice under the same singleton instance — once as itself
        // so the directory below can call it, once as IHostedService so its InitAsync pre-warms every
        // known site's summary before the app starts accepting requests.
        services.AddSingleton<SiteEquipmentSummaryCache>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<SiteEquipmentSummaryCache>());
        services.AddSingleton<ISiteEquipmentSummaryDirectory, SiteEquipmentSummaryDirectory>();
        services.AddSingleton<IEquipmentChangeNotifier, EquipmentChangeNotifier>();

        // Plain paged search — no caching (too many distinct filter/paging shapes to be worth it).
        services.AddScoped<IEquipmentReadService, EquipmentReadService>();

        // Service abstraction with zero database access: reads a bundled file, not the inventory table.
        services.AddScoped<IEquipmentCatalogueReader, EquipmentCatalogueFileReader>();

        // Published reservation contract: the Onboarding module calls this directly to reserve/release
        // equipment for a new hire's onboarding saga.
        services.AddScoped<IEquipmentReservationService, EquipmentReservationService>();

        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        // Integration events for other applications. Off by default: nothing is recorded until the host calls
        // AddEquipmentIntegrationEvents (it does when "Messaging" is configured). TryAdd, so the order of the two
        // calls doesn't matter.
        services.TryAddScoped<IEquipmentOutbox, NoBrokerEquipmentOutbox>();

        return services;
    }

    /// <summary>
    /// Sends Equipment's integration events (<see cref="EquipmentCreated"/>, <see cref="EquipmentUpdated"/>,
    /// <see cref="EquipmentDeleted"/>) to RabbitMQ through the module's outbox (docs/messaging/adr/0003): the handler
    /// stages the event in the same transaction as the change, and the outbox processor relays it with a confirmed
    /// publish once committed.
    /// </summary>
    /// <remarks>
    /// Call from the host only when it has also called <c>AddMessaging</c> with a route for each of these classes — the
    /// relay needs <c>IConfirmedMessagePublisher</c>. The module lists WHICH events it publishes; the host decides
    /// WHERE they go (buses, routing keys).
    /// </remarks>
    public static IServiceCollection AddEquipmentIntegrationEvents(this IServiceCollection services)
    {
        services.AddScoped<OutboxWriter<EquipmentDbContext>>();
        services.RemoveAll<IEquipmentOutbox>();
        services.AddScoped<IEquipmentOutbox, EquipmentOutbox>();

        services.AddOutboxPublishing<EquipmentDbContext>(
            typeof(EquipmentCreated),
            typeof(EquipmentUpdated),
            typeof(EquipmentDeleted));

        return services;
    }
}
