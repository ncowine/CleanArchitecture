using Equipment.Application.Inventory;

namespace Equipment.Application.Abstractions;

/// <summary>
/// Single-item lookup used by <see cref="GetEquipment"/> — the cache-aside read path. Kept separate from
/// <see cref="IEquipmentReadService"/> (the plain paged search) because only this one gets a caching
/// decorator: it's the read hit hardest by repeat traffic (the same asset looked up over and over),
/// whereas a filtered search result is a poor caching candidate (too many distinct shapes).
/// </summary>
public interface IEquipmentDirectory
{
    Task<GetEquipment.Response?> GetAsync(Guid equipmentId, CancellationToken cancellationToken);
}
