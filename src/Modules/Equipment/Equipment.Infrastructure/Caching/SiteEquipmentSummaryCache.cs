using BuildingBlocks.Caching;
using Equipment.Application.Inventory;
using Equipment.Domain;
using Equipment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.DataService;

namespace Equipment.Infrastructure.Caching;

/// <summary>
/// A per-site equipment rollup — a computed/merged cache, not a plain cache-aside lookup like
/// <see cref="CachingEquipmentDirectory"/>: <see cref="FetchAsync"/> aggregates over every equipment row
/// at a site (grouped by status/category) AND resolves the site's name from a different database
/// (SharedKernel's reference data). Both the fetch and the aggregation can be expensive, so results are
/// kept here and refreshed via <see cref="OnEquipmentChanged"/> rather than recomputed per request.
/// <para>
/// A single equipment row contributes to exactly one site's summary, but that link isn't visible from an
/// equipment id alone — <see cref="_equipmentSites"/> records it the moment a summary is (re)computed, so
/// a later CRUD event on that equipment id can find its way back to the right cached site key even though
/// this cache is keyed by site, not by equipment.
/// </para>
/// <para>
/// Registered singleton (required — see <see cref="DataCache{TKey,TValue}"/> remarks) and as an
/// <see cref="Microsoft.Extensions.Hosting.IHostedService"/> so <see cref="InitAsync"/> pre-warms every
/// known site's summary before the app starts accepting requests.
/// </para>
/// </summary>
public sealed class SiteEquipmentSummaryCache : DataCache<Guid, GetSiteEquipmentSummary.Response>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DependencyIndex<Guid, Guid> _equipmentSites = new(); // equipmentId -> site summary key(s) built from it

    public SiteEquipmentSummaryCache(IServiceScopeFactory scopeFactory, ILogger<SiteEquipmentSummaryCache> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>Call from wherever an equipment CRUD event is handled — see <see cref="Equipment.Application.Abstractions.IEquipmentChangeNotifier"/>.</summary>
    public void OnEquipmentChanged(Guid equipmentId, Guid? siteId)
    {
        var targets = new HashSet<Guid>(_equipmentSites.GetDependents(equipmentId));
        if (siteId is { } sid)
            targets.Add(sid);

        if (targets.Count == 0)
            return;

        // Deliberately ChangeType.Deleted here — NOT because the site is gone, but for its evict-only
        // behavior in the base cache (see DataCache.ProcessChangesAsync): remove and stop, don't
        // refetch. ChangeType.Updated would refetch immediately in the background, which is only safe if
        // the caller is guaranteed to see the change as already durable (e.g. a message-bus consumer,
        // whose broker only delivers after the producer's transaction committed). This notifier is called
        // synchronously from inside the equipment handler, BEFORE TransactionBehaviorBase's
        // SaveChanges+Commit runs — an immediate refetch here would race the still-open write and could
        // cache stale data. Evicting is race-free: whoever asks next (always later, always after this
        // request's commit) triggers an on-demand refetch through the normal cache-aside path instead.
        Notify(targets, ChangeType.Deleted);
    }

    protected override async Task InitAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var referenceData = scope.ServiceProvider.GetRequiredService<IReferenceDataService>();

        var sites = await referenceData.GetSitesAsync(cancellationToken);
        if (sites.Count == 0)
            return;

        await GetAsync(sites.Select(site => site.Id).ToHashSet(), cancellationToken);
    }

    protected override async Task<IReadOnlyDictionary<Guid, GetSiteEquipmentSummary.Response>> FetchAsync(
        HashSet<Guid> keys, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EquipmentDbContext>();
        var referenceData = scope.ServiceProvider.GetRequiredService<IReferenceDataService>();

        var assets = await db.Equipment
            .AsNoTracking()
            .Where(asset => asset.SiteId != null && keys.Contains(asset.SiteId.Value))
            .Select(asset => new { asset.Id, SiteId = asset.SiteId!.Value, asset.Status, asset.Category })
            .ToListAsync(ct);

        var results = new Dictionary<Guid, GetSiteEquipmentSummary.Response>();

        foreach (var siteId in keys)
        {
            // A missing site is a genuine "not found" — an existing site with zero equipment still gets
            // a (zero-count) summary, since the site itself is real reference data.
            var site = await referenceData.GetSiteAsync(siteId, ct);
            if (site is null)
                continue;

            var siteAssets = assets.Where(asset => asset.SiteId == siteId).ToList();
            foreach (var asset in siteAssets)
                _equipmentSites.Track(siteId, asset.Id);

            var countByCategory = siteAssets
                .GroupBy(asset => asset.Category.ToString())
                .ToDictionary(group => group.Key, group => group.Count());

            results[siteId] = new GetSiteEquipmentSummary.Response(
                siteId,
                site.Name,
                siteAssets.Count,
                siteAssets.Count(asset => asset.Status == EquipmentStatus.Available),
                siteAssets.Count(asset => asset.Status == EquipmentStatus.Reserved),
                countByCategory);
        }

        return results;
    }

    protected override void OnEvicted(Guid siteId) => _equipmentSites.Forget(siteId);
}
