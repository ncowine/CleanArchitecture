using Microsoft.Extensions.Caching.Hybrid;
using SharedKernel.Models;

namespace SharedKernel.DataService;

/// <summary>
/// Caching decorator over <see cref="ReferenceDataService"/>. Reference data barely ever changes, so this
/// uses a much longer expiration than the app's 5-minute default — there is no write path through this
/// app to invalidate on, so an out-of-band data change is picked up on cache expiry or the next app
/// restart, whichever comes first.
/// <para>
/// Uses <see cref="HybridCache.GetOrCreateAsync"/> so concurrent misses collapse into a single database
/// call (stampede protection) — same pattern as Equipment's CachingEquipmentDirectory.
/// </para>
/// </summary>
internal sealed class CachedReferenceDataService : IReferenceDataService
{
    private static readonly HybridCacheEntryOptions LongLived = new()
    {
        Expiration = TimeSpan.FromHours(24),
        LocalCacheExpiration = TimeSpan.FromHours(24),
    };

    private readonly ReferenceDataService _inner;
    private readonly HybridCache _cache;

    public CachedReferenceDataService(ReferenceDataService inner, HybridCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public Task<IReadOnlyList<Site>> GetSitesAsync(CancellationToken cancellationToken) =>
        _cache.GetOrCreateAsync(
            "reference:sites",
            _inner,
            static (inner, ct) => new ValueTask<IReadOnlyList<Site>>(inner.GetSitesAsync(ct)),
            LongLived,
            cancellationToken: cancellationToken).AsTask();

    public Task<Site?> GetSiteAsync(Guid siteId, CancellationToken cancellationToken) =>
        _cache.GetOrCreateAsync(
            $"reference:sites:{siteId}",
            (inner: _inner, siteId),
            static (state, ct) => new ValueTask<Site?>(state.inner.GetSiteAsync(state.siteId, ct)),
            LongLived,
            cancellationToken: cancellationToken).AsTask();
}
