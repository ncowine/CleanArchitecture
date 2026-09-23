namespace BuildingBlocks.Caching;

/// <summary>
/// The single meter every <see cref="DataCache{TKey,TValue}"/> publishes on (see <see cref="CacheMetrics"/>).
/// Subscribe to <see cref="MeterName"/> once in OpenTelemetry and every cache is covered — current and
/// future — distinguished by the "cache.name" tag on each measurement rather than a meter per cache type.
/// </summary>
public static class CachingDiagnostics
{
    public const string MeterName = "BuildingBlocks.Caching";
}
