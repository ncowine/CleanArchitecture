using Equipment.Application.Inventory;

namespace Equipment.Application.Abstractions;

/// <summary>
/// Per-site equipment rollup lookup used by <see cref="GetSiteEquipmentSummary"/> — mirrors
/// <see cref="IEquipmentDirectory"/>'s shape, but backs a computed/merged value (aggregated across many
/// equipment rows plus a cross-database site name) instead of a single row.
/// </summary>
public interface ISiteEquipmentSummaryDirectory
{
    Task<GetSiteEquipmentSummary.Response?> GetAsync(Guid siteId, CancellationToken cancellationToken);
}
