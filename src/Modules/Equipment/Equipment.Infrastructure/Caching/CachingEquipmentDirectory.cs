using Equipment.Application.Abstractions;
using Equipment.Application.Inventory;
using Microsoft.Extensions.Caching.Hybrid;

namespace Equipment.Infrastructure.Caching;

/// <summary>
/// Caching decorator over <see cref="EquipmentDirectory"/> — the cache-aside pattern the exercise calls
/// for: cache → database if not found → store in cache → return. Handlers and the published contract are
/// untouched; caching stays an infrastructure concern.
/// <para>
/// Uses <see cref="HybridCache.GetOrCreateAsync"/> so concurrent misses for the same asset collapse into
/// a single database call (stampede protection). Invalidation on writes is handled separately by
/// <see cref="EquipmentCacheInvalidator"/>.
/// </para>
/// </summary>
internal sealed class CachingEquipmentDirectory : IEquipmentDirectory
{
    private readonly EquipmentDirectory _inner;
    private readonly HybridCache _cache;

    public CachingEquipmentDirectory(EquipmentDirectory inner, HybridCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public Task<GetEquipment.Response?> GetAsync(Guid equipmentId, CancellationToken cancellationToken) =>
        _cache.GetOrCreateAsync(
            EquipmentCacheKeys.ForEquipment(equipmentId),
            (inner: _inner, equipmentId),
            static (state, ct) => new ValueTask<GetEquipment.Response?>(state.inner.GetAsync(state.equipmentId, ct)),
            cancellationToken: cancellationToken).AsTask();
}
