using Equipment.Domain;

namespace Equipment.Application.Abstractions;

public interface IEquipmentRepository
{
    Task AddAsync(EquipmentAsset asset, CancellationToken cancellationToken);

    Task<EquipmentAsset?> GetAsync(Guid equipmentId, CancellationToken cancellationToken);

    void Remove(EquipmentAsset asset);
}
