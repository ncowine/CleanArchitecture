using Equipment.Application.Abstractions;

namespace Equipment.Infrastructure.Caching;

internal sealed class EquipmentChangeNotifier : IEquipmentChangeNotifier
{
    private readonly SiteEquipmentSummaryCache _summaries;

    public EquipmentChangeNotifier(SiteEquipmentSummaryCache summaries)
    {
        _summaries = summaries;
    }

    public void Notify(Guid equipmentId, Guid? siteId = null) => _summaries.OnEquipmentChanged(equipmentId, siteId);
}
