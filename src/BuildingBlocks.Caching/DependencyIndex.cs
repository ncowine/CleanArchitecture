using System.Collections.Concurrent;

namespace BuildingBlocks.Caching;

/// <summary>
/// Tracks which computed/merged cache keys (<typeparamref name="TDependentKey"/>) were built from which
/// source entity keys (<typeparamref name="TSourceKey"/>) — the piece <see cref="DataCache{TKey,TValue}"/>
/// itself has no way to know, since it only ever sees its own key space.
/// <para>
/// Typical wiring, for a cache whose <c>FetchAsync</c> merges data from one or more source types:
/// </para>
/// <list type="number">
/// <item>Every time <c>FetchAsync</c> (re)computes a dependent value, call <see cref="Track"/> for each
/// source key it read along the way.</item>
/// <item>When a source entity changes (e.g. a message-bus consumer reacting to a CRUD event), call
/// <see cref="GetDependents"/> to find which dependent keys to
/// <see cref="DataCache{TKey,TValue}.Notify(TKey,ChangeType)"/>.</item>
/// <item>Override <see cref="DataCache{TKey,TValue}.OnEvicted"/> on the dependent cache to call
/// <see cref="Forget"/>, so the index doesn't grow forever as entries are purged.</item>
/// </list>
/// <para>
/// A dependent value built from more than one source TYPE (e.g. Employee ids and Project ids) needs one
/// <see cref="DependencyIndex{TSourceKey,TDependentKey}"/> per source type — this index only relates a
/// single source key space to a single dependent key space.
/// </para>
/// </summary>
public sealed class DependencyIndex<TSourceKey, TDependentKey>
    where TSourceKey : notnull
    where TDependentKey : notnull
{
    private readonly ConcurrentDictionary<TSourceKey, ConcurrentDictionary<TDependentKey, byte>> _bySource = new();
    private readonly ConcurrentDictionary<TDependentKey, ConcurrentDictionary<TSourceKey, byte>> _bySourceOfDependent = new();

    /// <summary>Records that <paramref name="dependentKey"/>'s cached value was built from <paramref name="sourceKey"/>.</summary>
    public void Track(TDependentKey dependentKey, TSourceKey sourceKey)
    {
        _bySource.GetOrAdd(sourceKey, static _ => new()).TryAdd(dependentKey, 0);
        _bySourceOfDependent.GetOrAdd(dependentKey, static _ => new()).TryAdd(sourceKey, 0);
    }

    /// <summary>The dependent keys currently known to depend on <paramref name="sourceKey"/>.</summary>
    public IReadOnlyCollection<TDependentKey> GetDependents(TSourceKey sourceKey)
        => _bySource.TryGetValue(sourceKey, out var dependents) ? dependents.Keys.ToArray() : [];

    /// <summary>Removes all links for a dependent key that has left its cache — call this from that cache's eviction hook.</summary>
    public void Forget(TDependentKey dependentKey)
    {
        if (!_bySourceOfDependent.TryRemove(dependentKey, out var sourceKeys))
            return;

        foreach (var sourceKey in sourceKeys.Keys)
        {
            if (!_bySource.TryGetValue(sourceKey, out var dependents))
                continue;

            dependents.TryRemove(dependentKey, out _);
            if (dependents.IsEmpty)
                _bySource.TryRemove(sourceKey, out _);
        }
    }
}
