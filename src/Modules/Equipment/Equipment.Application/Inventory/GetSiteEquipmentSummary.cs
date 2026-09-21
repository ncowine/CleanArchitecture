using BuildingBlocks.Messaging;
using Equipment.Application.Abstractions;

namespace Equipment.Application.Inventory;

/// <summary>
/// A per-site equipment rollup: counts merged from potentially many rows in the Equipment database plus
/// the site's name from SharedKernel's reference data. Backed by a computed/merged cache
/// (Equipment.Infrastructure.Caching.SiteEquipmentSummaryCache) that is refreshed by equipment CRUD
/// events rather than recomputed on every request — here the aggregation itself, not just the database
/// round trip, is the expensive part worth avoiding on every call.
/// </summary>
public static class GetSiteEquipmentSummary
{
    public sealed record Query(Guid SiteId) : IRequest<Response?>;

    public sealed record Response(
        Guid SiteId,
        string SiteName,
        int TotalCount,
        int AvailableCount,
        int ReservedCount,
        IReadOnlyDictionary<string, int> CountByCategory);

    public sealed class Handler : IRequestHandler<Query, Response?>
    {
        private readonly ISiteEquipmentSummaryDirectory _summaries;

        public Handler(ISiteEquipmentSummaryDirectory summaries)
        {
            _summaries = summaries;
        }

        public Task<Response?> Handle(Query query, CancellationToken cancellationToken) =>
            _summaries.GetAsync(query.SiteId, cancellationToken);
    }
}
