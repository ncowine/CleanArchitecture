using Equipment.Application.Abstractions;
using Equipment.Application.Inventory;
using Equipment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Equipment.Infrastructure.Caching;

/// <summary>The uncached database read behind <see cref="CachingEquipmentDirectory"/> — the "miss" path.</summary>
internal sealed class EquipmentDirectory : IEquipmentDirectory
{
    private readonly EquipmentDbContext _db;

    public EquipmentDirectory(EquipmentDbContext db)
    {
        _db = db;
    }

    public Task<GetEquipment.Response?> GetAsync(Guid equipmentId, CancellationToken cancellationToken) =>
        _db.Equipment
            .AsNoTracking()
            .Where(asset => asset.Id == equipmentId)
            .Select(asset => new GetEquipment.Response(
                asset.Id,
                asset.Name,
                asset.Category.ToString(),
                asset.AssetTag,
                asset.Status.ToString(),
                asset.CreatedOnUtc))
            .FirstOrDefaultAsync(cancellationToken);
}
