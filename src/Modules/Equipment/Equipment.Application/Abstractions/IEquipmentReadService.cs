using BuildingBlocks.Pagination;
using Equipment.Application.Inventory;

namespace Equipment.Application.Abstractions;

public interface IEquipmentReadService
{
    Task<PagedResult<SearchEquipment.EquipmentListItem>> SearchAsync(
        int page, int pageSize, string? category, string? status, CancellationToken cancellationToken);
}
