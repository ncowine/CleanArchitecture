using Equipment.Application.Abstractions;
using Microsoft.Extensions.Caching.Hybrid;

namespace Equipment.Infrastructure.Caching;

internal sealed class EquipmentCacheInvalidator : IEquipmentCacheInvalidator
{
    private readonly HybridCache _cache;

    public EquipmentCacheInvalidator(HybridCache cache)
    {
        _cache = cache;
    }

    public Task RemoveAsync(Guid equipmentId, CancellationToken cancellationToken) =>
        _cache.RemoveAsync(EquipmentCacheKeys.ForEquipment(equipmentId), cancellationToken).AsTask();
}
