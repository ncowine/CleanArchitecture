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

    // Cancelling only stops this caller's own wait, never the underlying fetch — see the
    // DataCache<,> class remarks for why (a fetch may be shared by other concurrent callers).
    public Task<GetSiteEquipmentSummary.Response?> GetAsync(Guid siteId, CancellationToken cancellationToken) =>
        _cache.GetAsync(siteId, cancellationToken);
}
