using Equipment.Application.Abstractions;
using Equipment.Application.Inventory;
using Equipment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.DataService;

namespace Equipment.Infrastructure.Caching;

/// <summary>The uncached database read behind <see cref="CachingEquipmentDirectory"/> — the "miss" path.</summary>
internal sealed class EquipmentDirectory : IEquipmentDirectory
{
    private readonly EquipmentDbContext _db;
    private readonly IReferenceDataService _referenceData;

    public EquipmentDirectory(EquipmentDbContext db, IReferenceDataService referenceData)
    {
        _db = db;
        _referenceData = referenceData;
    }

    public async Task<GetEquipment.Response?> GetAsync(Guid equipmentId, CancellationToken cancellationToken)
    {
        var asset = await _db.Equipment
            .AsNoTracking()
            .Where(asset => asset.Id == equipmentId)
            .Select(asset => new
            {
                asset.Id,
                asset.Name,
                Category = asset.Category.ToString(),
                asset.AssetTag,
                Status = asset.Status.ToString(),
                asset.SiteId,
                asset.CreatedOnUtc,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (asset is null)
        {
            return null;
        }

        // Cross-database read: Sites live in SharedKernel's own store, not Equipment's. Its own cache
        // decorator (CachedReferenceDataService) means this rarely reaches that database either.
        var siteName = asset.SiteId is { } siteId
            ? (await _referenceData.GetSiteAsync(siteId, cancellationToken))?.Name
            : null;

        return new GetEquipment.Response(
            asset.Id, asset.Name, asset.Category, asset.AssetTag, asset.Status, siteName, asset.CreatedOnUtc);
    }
}
