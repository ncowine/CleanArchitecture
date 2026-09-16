using BuildingBlocks.Pagination;
using Equipment.Application.Abstractions;
using Equipment.Application.Inventory;
using Equipment.Domain;
using Equipment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Equipment.Infrastructure.Reads;

internal sealed class EquipmentReadService : IEquipmentReadService
{
    private readonly EquipmentDbContext _db;

    public EquipmentReadService(EquipmentDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<SearchEquipment.EquipmentListItem>> SearchAsync(
        int page, int pageSize, string? category, string? status, CancellationToken cancellationToken)
    {
        var query = _db.Equipment.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(category) && Enum.TryParse<EquipmentCategory>(category, ignoreCase: true, out var parsedCategory))
        {
            query = query.Where(asset => asset.Category == parsedCategory);
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<EquipmentStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(asset => asset.Status == parsedStatus);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(asset => asset.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(asset => new { asset.Id, asset.Name, asset.Category, asset.AssetTag, asset.Status })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new SearchEquipment.EquipmentListItem(
                row.Id, row.Name, row.Category.ToString(), row.AssetTag, row.Status.ToString()))
            .ToList();

        return new PagedResult<SearchEquipment.EquipmentListItem>(items, page, pageSize, totalCount);
    }
}
