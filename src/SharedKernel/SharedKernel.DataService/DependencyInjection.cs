using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Data;

namespace SharedKernel.DataService;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the shared reference-data store and its long-lived cache decorator. Every caller —
    /// modules and the standalone reference endpoint alike — resolves <see cref="IReferenceDataService"/>
    /// and transparently gets the cached path; nobody needs to know the decorator exists.
    /// </summary>
    public static IServiceCollection AddSharedKernelReferenceData(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<ReferenceDbContext>(options => options.UseSqlite(connectionString));

        services.AddScoped<ReferenceDataService>();
        services.AddScoped<IReferenceDataService>(provider => new CachedReferenceDataService(
            provider.GetRequiredService<ReferenceDataService>(),
            provider.GetRequiredService<HybridCache>()));

        return services;
    }
}
