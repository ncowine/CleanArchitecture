namespace Equipment.Application.Abstractions;

public interface IEquipmentCacheInvalidator
{
    Task RemoveAsync(Guid equipmentId, CancellationToken cancellationToken);
}
