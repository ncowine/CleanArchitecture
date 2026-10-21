using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Caching;

public static class DataCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TCache"/> as a singleton (required — see <see cref="DataCache{TKey,TValue}"/>
    /// remarks) and the SAME instance as an <see cref="IHostedService"/>, so the host awaits its
    /// <see cref="DataCache{TKey,TValue}.InitAsync"/> during startup, before accepting requests.
    /// </summary>
    public static IServiceCollection AddDataCache<TCache>(this IServiceCollection services)
        where TCache : class, IHostedService
    {
        services.AddSingleton<TCache>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<TCache>());
        return services;
    }
}
