using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using Equipment.Application;
using Equipment.Application.Abstractions;
using Equipment.Contracts;
using Equipment.Infrastructure.Behaviors;
using Equipment.Infrastructure.Caching;
using Equipment.Infrastructure.Catalogue;
using Equipment.Infrastructure.Contracts;
using Equipment.Infrastructure.Persistence;
using Equipment.Infrastructure.Reads;
using Equipment.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

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

        // Plain paged search — no caching (too many distinct filter/paging shapes to be worth it).
        services.AddScoped<IEquipmentReadService, EquipmentReadService>();

        // Service abstraction with zero database access: reads a bundled file, not the inventory table.
        services.AddScoped<IEquipmentCatalogueReader, EquipmentCatalogueFileReader>();

        // Published reservation contract: the Onboarding module calls this directly to reserve/release
        // equipment for a new hire's onboarding saga.
        services.AddScoped<IEquipmentReservationService, EquipmentReservationService>();

        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        return services;
    }
}
