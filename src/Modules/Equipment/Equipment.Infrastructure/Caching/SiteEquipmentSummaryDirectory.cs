using Equipment.Application.Abstractions;
using Equipment.Application.Inventory;

namespace Equipment.Infrastructure.Caching;

internal sealed class SiteEquipmentSummaryDirectory : ISiteEquipmentSummaryDirectory
{
    private readonly SiteEquipmentSummaryCache _cache;

    public SiteEquipmentSummaryDirectory(SiteEquipmentSummaryCache cache)
    {
        _cache = cache;
    }

    // DataCache has no per-call cancellation of its own — a fetch it starts may be shared by other
    // concurrent callers, so one caller giving up can't cancel it out from under them. cancellationToken
    // is accepted for interface symmetry with IEquipmentDirectory but isn't forwarded.
    public Task<GetSiteEquipmentSummary.Response?> GetAsync(Guid siteId, CancellationToken cancellationToken) =>
        _cache.GetAsync(siteId);
}
