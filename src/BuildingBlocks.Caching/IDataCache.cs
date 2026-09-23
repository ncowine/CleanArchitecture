namespace BuildingBlocks.Caching;

public interface IDataCache<TKey, TValue>
    where TKey : notnull
{
    /// <summary>
    /// Cancelling only stops this caller's wait — see <see cref="DataCache{TKey,TValue}"/> remarks for why
    /// the underlying fetch itself can't be cancelled (it may be shared with other concurrent callers).
    /// </summary>
    Task<TValue?> GetAsync(TKey key, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="GetAsync(TKey, CancellationToken)"/>
    Task<IReadOnlyDictionary<TKey, TValue>> GetAsync(HashSet<TKey> keys, CancellationToken cancellationToken = default);
}
