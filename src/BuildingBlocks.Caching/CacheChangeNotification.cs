namespace BuildingBlocks.Caching;

public enum ChangeType
{
    Added,
    Updated,
    Deleted,
}

public readonly record struct CacheChangeNotification<TKey>(
    IReadOnlyCollection<TKey> Keys,
    ChangeType ChangeType);
