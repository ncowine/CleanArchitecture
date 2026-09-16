namespace Equipment.Infrastructure.Caching;

internal static class EquipmentCacheKeys
{
    public static string ForEquipment(Guid equipmentId) => $"equipment:{equipmentId}";
}
