namespace BuildingBlocks.Caching;

public interface IDataCache<TKey, TValue>
    where TKey : notnull
{
    Task<TValue?> GetAsync(TKey key);

    Task<IReadOnlyDictionary<TKey, TValue>> GetAsync(HashSet<TKey> keys);
}
