using System.Diagnostics.Metrics;

namespace BuildingBlocks.Caching;

/// <summary>
/// Records hits/misses/evictions/item-count for one <see cref="DataCache{TKey,TValue}"/> instance onto
/// the single shared <see cref="CachingDiagnostics.MeterName"/> meter, tagged with "cache.name" (the
/// concrete cache type, e.g. "SiteEquipmentSummaryCache") so per-cache series stay just the cache's own
/// name in Prometheus/Grafana rather than a meter name repeating "BuildingBlocks.Caching." on every row.
/// </summary>
internal sealed class CacheMetrics
{
    private static readonly Meter Meter = new(CachingDiagnostics.MeterName);

    private readonly KeyValuePair<string, object?> _cacheNameTag;
    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private readonly Counter<long> _evictions;

    public CacheMetrics(string cacheTypeName, Func<int> itemCountCallback)
    {
        _cacheNameTag = new KeyValuePair<string, object?>("cache.name", cacheTypeName);

        _hits = Meter.CreateCounter<long>("cache.hits", description: "Cache hit (served from store)");
        _misses = Meter.CreateCounter<long>("cache.misses", description: "Cache miss (triggered a fetch)");
        _evictions = Meter.CreateCounter<long>("cache.evictions", description: "Entry removed by purge loop");

        Meter.CreateObservableGauge(
            "cache.item_count",
            () => new Measurement<int>(itemCountCallback(), _cacheNameTag),
            description: "Current number of items in the cache");
    }

    public void RecordHit() => _hits.Add(1, _cacheNameTag);

    public void RecordMisses(long count) => _misses.Add(count, _cacheNameTag);

    public void RecordEvictions(long count) => _evictions.Add(count, _cacheNameTag);
}
