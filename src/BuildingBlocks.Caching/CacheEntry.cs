namespace BuildingBlocks.Caching;

internal sealed class CacheEntry<TValue>
{
    public TValue Value { get; }

    public DateTime CreatedAtUtc { get; }

    private long _lastAccessedTicks;

    public DateTime LastAccessedUtc
        => new(Interlocked.Read(ref _lastAccessedTicks), DateTimeKind.Utc);

    public CacheEntry(TValue value)
    {
        Value = value;
        CreatedAtUtc = DateTime.UtcNow;
        _lastAccessedTicks = CreatedAtUtc.Ticks;
    }

    public void Touch()
        => Interlocked.Exchange(ref _lastAccessedTicks, DateTime.UtcNow.Ticks);
}
