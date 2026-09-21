using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Caching;

/// <summary>
/// An ID-keyed, in-memory cache with request coalescing: concurrent callers asking for the same key
/// (or overlapping keys across a single-key and a bulk request) share one underlying fetch, so only one
/// call ever reaches <see cref="FetchAsync"/> no matter how many callers are waiting on it.
/// <para>
/// Freshness is driven by two independent paths: a background purge loop (idle/absolute TTL, see
/// <see cref="CacheOptions"/>) and explicit <see cref="Notify(TKey, ChangeType)"/> calls — wire the
/// latter to your event source (e.g. a message-bus consumer reacting to CRUD events) so cached entries
/// are refreshed proactively instead of only expiring on a timer.
/// </para>
/// <para>
/// A concrete cache type (e.g. <c>EmployeeDataCache : DataCache&lt;Guid, Employee&gt;</c>) owns a
/// background purge loop and change processor for its lifetime, so it MUST be registered as a
/// <b>singleton</b> in DI. Registering it Scoped/Transient gives every request an empty store and spins
/// up a fresh pair of background loops each time.
/// </para>
/// <para>
/// Override <see cref="InitAsync"/> to run logic before the cache serves its first request (e.g.
/// pre-warming a known key set). It only runs if the SAME singleton instance is ALSO registered as an
/// <see cref="IHostedService"/> — e.g. <c>services.AddSingleton&lt;IHostedService&gt;(sp =>
/// sp.GetRequiredService&lt;MyCache&gt;())</c> — so the host awaits it during startup, before accepting
/// requests. Without that second registration <see cref="InitAsync"/> is simply never called and the
/// cache starts empty, which is also a perfectly fine default (see <see cref="GetAsync(TKey)"/>).
/// </para>
/// </summary>
public abstract class DataCache<TKey, TValue> : IDataCache<TKey, TValue>, IHostedService, IDisposable
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, CacheEntry<TValue>> _store = new();

    // Maps a key to its in-flight fetch task. When multiple callers request the same key
    // concurrently, they all receive the SAME Lazy<Task> — so only one DB call is made.
    // Lazy<Task<T>> is used (instead of just Task<T>) because:
    //   - ConcurrentDictionary.GetOrAdd may invoke the factory on multiple threads,
    //     but Lazy guarantees the inner factory (the actual DB call) runs exactly once.
    //   - Without Lazy, two threads could both create separate Tasks before GetOrAdd
    //     picks a winner, resulting in duplicate DB calls.
    private readonly ConcurrentDictionary<TKey, Lazy<Task<TValue?>>> _inflightFetches = new();

    private readonly CacheOptions _options;
    private readonly CacheMetrics _metrics;
    private readonly ILogger _logger;
    private readonly Channel<CacheChangeNotification<TKey>> _changeChannel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _changeProcessorTask;
    private readonly Task _purgeTask;

    protected DataCache(ILogger logger, CacheOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new CacheOptions();
        _metrics = new CacheMetrics(GetType().Name, () => _store.Count);

        _changeChannel = Channel.CreateBounded<CacheChangeNotification<TKey>>(
            new BoundedChannelOptions(_options.ChangeQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });

        _changeProcessorTask = ProcessChangesAsync(_cts.Token);
        _purgeTask = PurgeLoopAsync(_cts.Token);
    }

    public async Task<TValue?> GetAsync(TKey key)
    {
        if (TryGetFromStore(key, out var value))
            return value;

        _metrics.Misses.Add(1);
        return await FetchAndCacheAsync(key).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<TKey, TValue>> GetAsync(HashSet<TKey> keys)
    {
        var result = new Dictionary<TKey, TValue>(keys.Count);
        var keysToFetch = new HashSet<TKey>();
        var alreadyInflight = new List<(TKey Key, Lazy<Task<TValue?>> Lazy)>();

        // Partition: cached / already in-flight / need to fetch
        foreach (var key in keys)
        {
            if (TryGetFromStore(key, out var value))
                result[key] = value;
            else if (_inflightFetches.TryGetValue(key, out var existing))
                alreadyInflight.Add((key, existing));
            else
                keysToFetch.Add(key);
        }

        var missCount = alreadyInflight.Count + keysToFetch.Count;
        if (missCount > 0)
            _metrics.Misses.Add(missCount);

        // Batch-fetch all truly missing keys in ONE db call.
        //
        // The challenge: _inflightFetches stores Lazy<Task<TValue?>> (per-key), but the
        // batch DB call returns all keys at once in a dictionary. We bridge this with
        // two layers of Lazy:
        //
        // 1. batchLazy — wraps the single FetchBatchCoreAsync call. No matter how many
        //    per-key lazys reference it, the DB call happens exactly once.
        //
        // 2. perKeyLazy (one per missing key) — awaits batchLazy.Value (the shared batch
        //    result) and extracts just its own key. These get registered in _inflightFetches
        //    so that a concurrent single-key Get(key) calling FetchAndCacheAsync will find
        //    them via GetOrAdd and piggyback on the batch — no extra DB call.
        //
        // Example: Bulk Get({A,B,C}) runs while a concurrent Get(B) arrives.
        //   - Bulk registers perKeyLazy for A, B, C in _inflightFetches
        //   - Concurrent Get(B) calls GetOrAdd(B) → finds the existing perKeyLazy → awaits it
        //   - All four callers (bulk + single) resolve from the same single batch DB call
        if (keysToFetch.Count > 0)
        {
            var batchLazy = new Lazy<Task<IReadOnlyDictionary<TKey, TValue>>>(
                () => FetchBatchCoreAsync(keysToFetch));

            foreach (var key in keysToFetch)
            {
                var k = key; // capture for closure — loop variable would be wrong
                var perKeyLazy = new Lazy<Task<TValue?>>(async () =>
                {
                    var batchResult = await batchLazy.Value.ConfigureAwait(false); // triggers the ONE batch DB call
                    return batchResult.TryGetValue(k, out var v) ? v : default;
                });

                // GetOrAdd: if another thread registered this key first, we get theirs instead
                var registered = _inflightFetches.GetOrAdd(key, perKeyLazy);
                alreadyInflight.Add((key, registered));
            }
        }

        // Await all pending keys (pre-existing in-flight from other callers + our batch-derived)
        // Then clean up _inflightFetches so future requests create fresh fetches.
        var pendingTasks = alreadyInflight.Select(async item =>
        {
            try
            {
                return (item.Key, Value: await item.Lazy.Value.ConfigureAwait(false));
            }
            finally
            {
                // Remove only our specific Lazy instance (KeyValuePair overload),
                // so we don't clobber a newer Lazy registered by a subsequent request
                _inflightFetches.TryRemove(
                    new KeyValuePair<TKey, Lazy<Task<TValue?>>>(item.Key, item.Lazy));
            }
        });

        foreach (var (key, value) in await Task.WhenAll(pendingTasks).ConfigureAwait(false))
        {
            if (value is not null)
                result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Pushes a change notification for the given key onto the change queue. The background change
    /// processor picks it up and, for <see cref="ChangeType.Added"/>/<see cref="ChangeType.Updated"/>,
    /// refetches and repopulates the cache itself — no reader pays for the refresh. Call this from
    /// wherever you learn about the change (e.g. a message-bus consumer reacting to a CRUD event).
    /// </summary>
    public void Notify(TKey key, ChangeType changeType) => Notify([key], changeType);

    /// <inheritdoc cref="Notify(TKey,ChangeType)"/>
    public void Notify(IReadOnlyCollection<TKey> keys, ChangeType changeType)
    {
        // Bounded channel is DropOldest, so this never blocks or throws under backpressure.
        _changeChannel.Writer.TryWrite(new CacheChangeNotification<TKey>(keys, changeType));
    }

    /// <summary>See the <see cref="InitAsync"/> remarks on the class for when this actually runs.</summary>
    Task IHostedService.StartAsync(CancellationToken cancellationToken) => InitAsync(cancellationToken);

    Task IHostedService.StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Override to run one-time startup logic (e.g. pre-warming) before this cache serves its first request.</summary>
    protected virtual Task InitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Single-key fetch with coalescing (see remarks on <see cref="GetAsync(HashSet{TKey})"/>).</summary>
    private async Task<TValue?> FetchAndCacheAsync(TKey key)
    {
        var lazy = _inflightFetches.GetOrAdd(key, k =>
            new Lazy<Task<TValue?>>(() => FetchSingleCoreAsync(k)));

        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        finally
        {
            _inflightFetches.TryRemove(new KeyValuePair<TKey, Lazy<Task<TValue?>>>(key, lazy));
        }
    }

    private async Task<IReadOnlyDictionary<TKey, TValue>> FetchBatchCoreAsync(HashSet<TKey> keys)
    {
        // Double-check: some may have been populated while waiting
        var stillMissing = keys.Where(k => !_store.ContainsKey(k)).ToHashSet();

        if (stillMissing.Count == 0)
            return new Dictionary<TKey, TValue>();

        var freshData = await FetchAsync(stillMissing, _cts.Token).ConfigureAwait(false);
        foreach (var kvp in freshData)
            _store[kvp.Key] = new CacheEntry<TValue>(kvp.Value);

        return freshData;
    }

    private async Task<TValue?> FetchSingleCoreAsync(TKey key)
    {
        // Double-check: may have been populated while waiting
        if (TryGetFromStore(key, out var value))
            return value;

        var result = await FetchAsync([key], _cts.Token).ConfigureAwait(false);

        if (result.TryGetValue(key, out var fetched))
        {
            _store[key] = new CacheEntry<TValue>(fetched);
            return fetched;
        }

        return default;
    }

    /// <summary>
    /// Fetches the given keys from the underlying source. Implementations should return only the keys
    /// that actually exist — a missing key is treated as "not found" rather than an error.
    /// </summary>
    protected abstract Task<IReadOnlyDictionary<TKey, TValue>> FetchAsync(HashSet<TKey> keys, CancellationToken ct);

    /// <summary>
    /// Called whenever a key leaves the store for good — TTL/idle purge, LRU eviction, or an explicit
    /// <see cref="ChangeType.Deleted"/> notification (NOT the evict-then-refetch step for
    /// <see cref="ChangeType.Added"/>/<see cref="ChangeType.Updated"/>, since that key is about to be
    /// repopulated). Override to clean up a side-index keyed off this cache's entries — e.g. a
    /// <see cref="DependencyIndex{TSourceKey,TDependentKey}"/> tracking which of this cache's keys were
    /// built from which source entities, so it doesn't grow forever.
    /// </summary>
    protected virtual void OnEvicted(TKey key)
    {
    }

    private async Task ProcessChangesAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var notification in _changeChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    switch (notification.ChangeType)
                    {
                        case ChangeType.Deleted:
                            foreach (var key in notification.Keys)
                            {
                                if (_store.TryRemove(key, out _))
                                    OnEvicted(key);
                            }

                            break;

                        case ChangeType.Added:
                        case ChangeType.Updated:
                            // Added: pre-warm regardless of whether it's cached yet (it can't be).
                            // Updated: only refetch keys someone already has cached — no point
                            // proactively pulling data nobody has asked for.
                            var keysToRefresh = notification.ChangeType == ChangeType.Added
                                ? notification.Keys.ToHashSet()
                                : notification.Keys.Where(_store.ContainsKey).ToHashSet();

                            if (keysToRefresh.Count == 0)
                                break;

                            // Evict stale entries first
                            foreach (var key in keysToRefresh)
                                _store.TryRemove(key, out _);

                            // Batch fetch fresh data
                            var freshData = await FetchAsync(keysToRefresh, ct).ConfigureAwait(false);
                            foreach (var kvp in freshData)
                                _store[kvp.Key] = new CacheEntry<TValue>(kvp.Value);
                            break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    DataCacheLog.ChangeProcessingFailed(_logger, ex, GetType().Name, notification.ChangeType, notification.Keys.Count);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    // ──────────────────────────────────────────────
    // Background: Purge loop
    // ──────────────────────────────────────────────
    private async Task PurgeLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_options.PurgeInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var now = DateTime.UtcNow;

                var keysToRemove = _store
                    .Where(kvp =>
                        (now - kvp.Value.LastAccessedUtc) > _options.UnusedThreshold ||
                        (now - kvp.Value.CreatedAtUtc) > _options.AbsoluteExpiration)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    if (_store.TryRemove(key, out _))
                        OnEvicted(key);
                }

                var removedCount = keysToRemove.Count;

                // LRU eviction
                if (_options.MaxItems.HasValue && _store.Count > _options.MaxItems.Value)
                {
                    var excess = _store
                        .OrderBy(x => x.Value.LastAccessedUtc)
                        .Take(_store.Count - _options.MaxItems.Value)
                        .Select(x => x.Key)
                        .ToList();

                    foreach (var key in excess)
                    {
                        if (_store.TryRemove(key, out _))
                            OnEvicted(key);
                    }

                    removedCount += excess.Count;
                }

                if (removedCount > 0)
                    _metrics.Evictions.Add(removedCount);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private bool TryGetFromStore(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_store.TryGetValue(key, out var entry) &&
            (DateTime.UtcNow - entry.CreatedAtUtc) <= _options.AbsoluteExpiration)
        {
            entry.Touch();
            value = entry.Value;
            _metrics.Hits.Add(1);
            return true;
        }

        value = default;
        return false;
    }

    public int Count => _store.Count;

    public void Dispose()
    {
        _cts.Cancel();

        _changeChannel.Writer.TryComplete();

        try
        {
            _changeProcessorTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }

        try
        {
            _purgeTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }

        _cts.Dispose();
        _metrics.Dispose();
        _store.Clear();
        _inflightFetches.Clear();

        GC.SuppressFinalize(this);
    }
}

// Compile-time logging via the source generator — zero allocations when the level is disabled.
internal static partial class DataCacheLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "{CacheType} failed processing a {ChangeType} notification for {KeyCount} key(s)")]
    public static partial void ChangeProcessingFailed(ILogger logger, Exception ex, string cacheType, ChangeType changeType, int keyCount);
}
