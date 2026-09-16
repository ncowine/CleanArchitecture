# Caching — Cache-Aside With HybridCache

**Who this is for:** someone whose hottest read is doing a database round trip on every
single call, and wants to stop paying that cost without changing what the read returns or
how the handler that mutates the data works.

**What you'll be able to do by the end:** add a cache-aside read to any module, invalidate
it correctly from every write that can change it, prove — not assume — that it's actually
being served from cache, and know which reads should never be cached at all.

**What you need first:** a read that goes through its own interface already — the shape
[chapter 10](20-add-a-feature.md#10-read-features--the-shortcut) of
[Adding a feature](20-add-a-feature.md) walks through.

---

## Table of contents

| # | Chapter | What you do there |
|---|---|---|
| 1 | [What cache-aside is](#1-what-cache-aside-is) | The four-step flow, and why this shape |
| 2 | [What NOT to cache](#2-what-not-to-cache) | Save yourself the debugging session |
| 3 | [The moving parts](#3-the-moving-parts) | Three small pieces, one interface |
| 4 | [Step 1 — The plain read](#4-step-1--the-plain-read) | The miss path, written first |
| 5 | [Step 2 — The caching decorator](#5-step-2--the-caching-decorator) | Wraps the interface, not the handler |
| 6 | [Step 3 — One cache key per identity](#6-step-3--one-cache-key-per-identity) | A single place the key format lives |
| 7 | [Step 4 — Invalidate on write](#7-step-4--invalidate-on-write) | Explicit, not automatic — and why |
| 8 | [Step 5 — Wire it into DI](#8-step-5--wire-it-into-di) | The decorator registration, line by line |
| 9 | [Step 6 — Verify it's actually caching](#9-step-6--verify-its-actually-caching) | Prove it, don't assume |
| 10 | [Going to Redis](#10-going-to-redis) | The one-line upgrade |
| 11 | [The checklist](#11-the-checklist) | Run this when doing it for real |
| 12 | [Troubleshooting](#12-troubleshooting) | Symptom, cause, fix |
| 13 | [Cheat sheet](#13-cheat-sheet) | Settings and code, in one place |
| 14 | [Glossary](#14-glossary) | Every term used in this guide |

---

## 1. What cache-aside is

Cache-aside (also called lazy loading) is the simplest of the caching patterns, and the one
this codebase uses everywhere it caches at all:

1. **Ask the cache first.**
2. **Miss?** Load from the real source (the database, here).
3. **Store what you loaded in the cache**, so the next ask is a hit.
4. **Return it.**

Nothing populates the cache proactively, and nothing but a read ever fills it. That is the
whole pattern — the appeal is that it is easy to reason about: the cache is never more than
"whatever the last read happened to load," and if you deleted the entire cache right now,
correctness would not change, only latency would.

**Why this one, and not read-through or write-through?** Read-through and write-through push
caching logic into the data layer itself — every write updates the cache as part of the
write. That keeps the cache warmer, but it means the caching code has to run inside the same
transaction as the write, for every write path, forever. Cache-aside keeps caching entirely
on the *read* side: the write side only has to know one thing — "invalidate key X" — and
that's [chapter 7](#7-step-4--invalidate-on-write).

---

## 2. What NOT to cache

Before reaching for `HybridCache`, check the read actually deserves it:

| Cache it | Don't |
|---|---|
| A single entity, looked up by id, read far more than it's written | A search or list result — too many distinct filter/paging shapes to be worth the memory |
| Data where a few seconds of staleness is genuinely fine | Anything where staleness has a real business cost — a stock check right before charging a card |
| A read hit by repeat traffic (the same id, over and over) | A read hit once per session — you'd cache one hit and never read it back |

`Equipment.Infrastructure/Caching/` only caches `GetEquipment` — a single asset by id — for
exactly this reason. `SearchEquipment`, the paged inventory search right next to it, has no
caching decorator at all. Same module, two reads, one is worth it and one isn't; that
distinction is the first decision to make, before any code.

> **Why this matters:** a cached search result is a trap waiting to spring. The moment
> someone adds a new filter combination, it's a new cache key with its own miss — you end up
> caching every distinct query shape a user could type, which is not caching, it's a second,
> worse database.

---

## 3. The moving parts

Three pieces, all in `Equipment.Infrastructure/Caching/`, plus one interface in
`Equipment.Application/Abstractions/`:

```
   GetEquipment.Handler
        │  depends on
        ▼
   IEquipmentDirectory                    ← one interface, two implementations
        │
        ▼
   CachingEquipmentDirectory              ← registered as IEquipmentDirectory
        │  cache miss
        ▼
   EquipmentDirectory                     ← the plain database read
        │
        ▼
   EquipmentDbContext

   UpdateEquipment.Handler / DeleteEquipment.Handler
        │  after the write
        ▼
   IEquipmentCacheInvalidator.RemoveAsync(id)   ← evicts the one key that's now stale
```

| Piece | Job |
|---|---|
| `IEquipmentDirectory` | The read interface the handler depends on. Doesn't know caching exists |
| `EquipmentDirectory` | The real implementation — a plain EF query. The "miss" path |
| `CachingEquipmentDirectory` | Also implements `IEquipmentDirectory`; wraps `EquipmentDirectory` behind `HybridCache` |
| `IEquipmentCacheInvalidator` | The write side's only knowledge of caching: one method, `RemoveAsync(id)` |
| `HybridCache` | The cache itself — in-memory today, one config change from Redis-backed |

**The decorator pattern is the whole trick.** `GetEquipment.Handler` depends on
`IEquipmentDirectory` and has never heard of `HybridCache` — DI hands it
`CachingEquipmentDirectory`, which quietly wraps the plain implementation. Delete the caching
decorator and register `EquipmentDirectory` directly, and the handler compiles and runs
exactly the same, just slower.

---

## 4. Step 1 — The plain read

Write the uncached version first — it's the fallback every cache miss falls through to, and
it's what you'd have written anyway if caching didn't exist:

```csharp
// Equipment.Infrastructure/Caching/EquipmentDirectory.cs
internal sealed class EquipmentDirectory : IEquipmentDirectory
{
    private readonly EquipmentDbContext _db;

    public EquipmentDirectory(EquipmentDbContext db)
    {
        _db = db;
    }

    public Task<GetEquipment.Response?> GetAsync(Guid equipmentId, CancellationToken cancellationToken) =>
        _db.Equipment
            .AsNoTracking()
            .Where(asset => asset.Id == equipmentId)
            .Select(asset => new GetEquipment.Response(
                asset.Id,
                asset.Name,
                asset.Category.ToString(),
                asset.AssetTag,
                asset.Status.ToString(),
                asset.CreatedOnUtc))
            .FirstOrDefaultAsync(cancellationToken);
}
```

Nothing unusual: `AsNoTracking` (this is a read, EF doesn't need to track it for changes),
and a `Select` projection straight to the response shape rather than loading the whole
entity. This class is `internal` — nothing outside `Equipment.Infrastructure` is meant to
depend on it directly; the module hands out `IEquipmentDirectory` instead.

The interface it implements is deliberately narrow:

```csharp
// Equipment.Application/Abstractions/IEquipmentDirectory.cs

/// Single-item lookup used by GetEquipment — the cache-aside read path. Kept separate from
/// IEquipmentReadService (the plain paged search) because only this one gets a caching
/// decorator.
public interface IEquipmentDirectory
{
    Task<GetEquipment.Response?> GetAsync(Guid equipmentId, CancellationToken cancellationToken);
}
```

One method, one shape of question ("this id, or nothing"). That narrowness is what makes a
single cache key format work — see [chapter 6](#6-step-3--one-cache-key-per-identity).

---

## 5. Step 2 — The caching decorator

The decorator implements the *same interface*, takes the plain implementation as a
dependency, and wraps every call in `HybridCache.GetOrCreateAsync`:

```csharp
// Equipment.Infrastructure/Caching/CachingEquipmentDirectory.cs
internal sealed class CachingEquipmentDirectory : IEquipmentDirectory
{
    private readonly EquipmentDirectory _inner;
    private readonly HybridCache _cache;

    public CachingEquipmentDirectory(EquipmentDirectory inner, HybridCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public Task<GetEquipment.Response?> GetAsync(Guid equipmentId, CancellationToken cancellationToken) =>
        _cache.GetOrCreateAsync(
            EquipmentCacheKeys.ForEquipment(equipmentId),
            (inner: _inner, equipmentId),
            static (state, ct) => new ValueTask<GetEquipment.Response?>(state.inner.GetAsync(state.equipmentId, ct)),
            cancellationToken: cancellationToken).AsTask();
}
```

Read `GetOrCreateAsync` as the four steps from [chapter 1](#1-what-cache-aside-is) collapsed
into one call: check the cache for this key; on a miss, invoke the factory (which is the
plain `EquipmentDirectory` call); store what came back; return it — hit or miss, the caller
sees the same `Task<GetEquipment.Response?>`.

**Why `HybridCache` and not a raw `IMemoryCache` dictionary:** two things a hand-rolled cache
gets wrong by default, that `HybridCache` gets right for free.

- **Stampede protection.** If ten requests miss the same key at the same instant — a cold
  cache, or a key that just expired under load — `GetOrCreateAsync` runs the factory
  **once** and lets the other nine wait on that one result, instead of ten of them hitting
  the database simultaneously.
- **The static lambda + captured `state` tuple.** Passing `(inner: _inner, equipmentId)` as
  the state parameter, with a `static` lambda, avoids allocating a closure on every call.
  It looks like extra ceremony for a small win; it's the idiomatic shape `HybridCache`'s API
  was designed around, and it's worth copying exactly rather than "simplifying" back to a
  capturing lambda.

The decorator is `internal`, same as the plain implementation. Only the interface is public
to the rest of the module.

---

## 6. Step 3 — One cache key per identity

```csharp
// Equipment.Infrastructure/Caching/EquipmentCacheKeys.cs
internal static class EquipmentCacheKeys
{
    public static string ForEquipment(Guid equipmentId) => $"equipment:{equipmentId}";
}
```

That's the entire class — one static method. It looks almost pointless until you consider
the alternative: the key format (`$"equipment:{equipmentId}"`) would otherwise be typed twice,
once in the decorator that populates the cache and once in the invalidator that evicts it. If
those two ever drift — a typo, a format change in one but not the other — you get a cache
that never invalidates and nothing tells you why.

**Why this matters:** one class, one format, used by both the read side and the write side.
If you need a second cached lookup in the same module — say, by asset tag instead of id — add
a second method here (`ForAssetTag(string tag)`), not a second inline string somewhere else.

---

## 7. Step 4 — Invalidate on write

This is the step that's easy to forget, because forgetting it doesn't throw — it just serves
stale data forever, silently.

```csharp
// Equipment.Application/Abstractions/IEquipmentCacheInvalidator.cs
public interface IEquipmentCacheInvalidator
{
    Task RemoveAsync(Guid equipmentId, CancellationToken cancellationToken);
}
```

```csharp
// Equipment.Infrastructure/Caching/EquipmentCacheInvalidator.cs
internal sealed class EquipmentCacheInvalidator : IEquipmentCacheInvalidator
{
    private readonly HybridCache _cache;

    public EquipmentCacheInvalidator(HybridCache cache)
    {
        _cache = cache;
    }

    public Task RemoveAsync(Guid equipmentId, CancellationToken cancellationToken) =>
        _cache.RemoveAsync(EquipmentCacheKeys.ForEquipment(equipmentId), cancellationToken).AsTask();
}
```

Called directly — by name, not through any generic pipeline magic — from every handler that
can make the cached value stale:

```csharp
// Equipment.Application/Inventory/UpdateEquipment.cs (excerpt)
public async Task<bool> Handle(Command command, CancellationToken cancellationToken)
{
    var asset = await _equipment.GetAsync(command.EquipmentId, cancellationToken);
    if (asset is null)
    {
        return false;
    }

    asset.Update(command.Name, command.Category, command.AssetTag);
    await _cache.RemoveAsync(asset.Id, cancellationToken);   // ← this

    _realtime.Publish(RealtimeGroups.Equipment(), new RealtimeEvent("EquipmentUpdated", new { /* ... */ }));
    return true;
}
```

`DeleteEquipment.Handler` calls the identical line. `CreateEquipment.Handler` doesn't need
to — there is nothing to invalidate for an id that has never been cached yet.

### Why explicit, not a pipeline behavior

Auditing ([Auditing — who changed what](40-auditing.md)) opts a command in with a marker
interface and the pipeline does the rest automatically. Caching in this codebase is
deliberately **not** built that way, even though it's technically possible to detect "this
command probably invalidates this key" from a naming convention or attribute.

The reason: that kind of automatic invalidation is *convenient right up until it's wrong*. A
convention-based invalidator has to guess which key a given command affects, and a wrong
guess is silent — it either invalidates too little (stale data survives) or too much (you
lose the caching benefit you added it for). An explicit call in the handler is one extra
line, but it's a line you can read, that says exactly what it evicts, sitting right next to
the write that makes the eviction necessary.

> **The gotcha this buys you nothing against:** if you add a *third* write path that can
> change equipment (a bulk import, a background reconciliation job) and forget the
> `RemoveAsync` call there too, you get exactly the same silent staleness as if this were
> automatic. Explicit invalidation doesn't eliminate the mistake — it just makes each
> instance of it a one-line, greppable omission (`grep -L RemoveAsync` across the handlers
> that touch this entity) instead of a hidden convention that failed to match.

---

## 8. Step 5 — Wire it into DI

The registration is the part that actually assembles the decorator, and it's worth reading
line by line — the order matters:

```csharp
// Equipment.Infrastructure/DependencyInjection.cs (excerpt)
services.AddScoped<EquipmentDirectory>();
services.AddScoped<IEquipmentDirectory>(provider => new CachingEquipmentDirectory(
    provider.GetRequiredService<EquipmentDirectory>(),
    provider.GetRequiredService<HybridCache>()));
services.AddScoped<IEquipmentCacheInvalidator, EquipmentCacheInvalidator>();
```

1. **Register the plain implementation as itself** (`EquipmentDirectory`, the concrete
   class) — not as `IEquipmentDirectory`. If it were also registered as the interface, the
   last registration would win and which one actually resolves would depend on
   registration order, not intent.
2. **Register the interface to resolve to the decorator**, constructed by hand with a
   factory delegate so it can pull in both the plain implementation and `HybridCache` by
   type. Every consumer that asks for `IEquipmentDirectory` — which is all of them; nothing
   in the module asks for `EquipmentDirectory` directly — gets the caching path.
3. **Register the invalidator separately.** It doesn't need the plain reader at all, only
   `HybridCache`.

`HybridCache` itself is registered once, host-side, not per module:

```csharp
// CleanArch.Api/DependencyInjection.cs (excerpt)
services.AddHybridCache(options =>
{
    options.MaximumPayloadBytes = 64 * 1024; // 64 KB per entry — these are tiny reference objects
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),           // overall lifetime (and L2 lifetime once Redis is added)
        LocalCacheExpiration = TimeSpan.FromMinutes(1), // in-memory (L1) lifetime — short, to bound memory
    };
});
```

Every module that wants a cache-aside read shares this one registration. There's nothing
Equipment-specific about it — a second module adding its own cached lookup needs no DI
changes here at all, only its own key class, decorator, and invalidator following this same
shape.

> **Why this matters:** `Expiration` and `LocalCacheExpiration` are a real tradeoff, not
> defaults to leave alone. A longer expiration means fewer database hits but a wider window
> where a change made outside this process (a direct SQL update, another instance's write
> before Redis is added) goes unseen. Five minutes and one minute are chosen to be short
> enough that "stale for a bit" is a non-event for equipment inventory; a domain where
> staleness is expensive should choose shorter, and a domain that barely changes could
> choose longer.

---

## 9. Step 6 — Verify it's actually caching

Don't take the wiring on faith — a cache that silently isn't caching (because a registration
was written the wrong way round, or the plain reader got registered as the interface too)
still returns correct data on every call. The only way to catch that is to prove a read
*didn't* touch the database.

The technique used in `EquipmentModuleTests.cs` is to mutate the row **underneath** the
cache, with raw SQL that bypasses the invalidator entirely:

```csharp
[Fact]
public async Task A_second_read_is_served_from_cache_not_the_database()
{
    var id = await sender.Send(
        new CreateEquipment.Command("Dell Monitor", EquipmentCategory.Monitor, "MON-001"), default);

    var first = await sender.Send(new GetEquipment.Query(id), default);
    Assert.Equal("Dell Monitor", first!.Name);

    // Change the row directly at the database level — bypassing the cache invalidator entirely, which
    // only a raw SQL statement (not the domain, which has no "just rename it" backdoor) can do.
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE EquipmentAssets SET Name = 'Mutated Directly In The Database' WHERE Id = {id}");

    var second = await sender.Send(new GetEquipment.Query(id), default);

    // Still the ORIGINAL name — proof the second read came from the cache, not a fresh query.
    Assert.Equal("Dell Monitor", second!.Name);
}
```

If that assertion ever fails — the second read shows the mutated name — the cache isn't
being consulted at all, and you've just found that out in a test instead of in production
metrics six months from now.

The matching, opposite proof lives right next to it — that a normal write (through the
handler, not raw SQL) *does* invalidate:

```csharp
[Fact]
public async Task Updating_equipment_invalidates_the_cache_so_the_next_read_sees_the_change()
{
    var id = await sender.Send(
        new CreateEquipment.Command("Dell Monitor", EquipmentCategory.Monitor, "MON-001"), default);
    await sender.Send(new GetEquipment.Query(id), default); // populate the cache

    await sender.Send(
        new UpdateEquipment.Command(id, "Dell Monitor 4K", EquipmentCategory.Monitor, "MON-001"), default);
    var afterUpdate = await sender.Send(new GetEquipment.Query(id), default);

    Assert.Equal("Dell Monitor 4K", afterUpdate!.Name);
}
```

And the delete path is checked the same way — a read after delete has to be a genuine miss,
not a cache entry serving a since-deleted row:

```csharp
[Fact]
public async Task Deleting_equipment_removes_it_and_a_later_read_is_a_miss()
{
    var id = await sender.Send(
        new CreateEquipment.Command("Spare Phone", EquipmentCategory.Phone, "PHN-001"), default);
    await sender.Send(new GetEquipment.Query(id), default); // populate the cache

    await sender.Send(new DeleteEquipment.Command(id), default);
    var afterDelete = await sender.Send(new GetEquipment.Query(id), default);

    Assert.Null(afterDelete); // cache was invalidated too, not just the row deleted
}
```

**Write all three shapes for any cache you add**: a stale-value proof (mutate underneath the
cache, confirm the old value survives), an invalidation proof (write through the real
handler, confirm the new value appears), and a delete-invalidation proof (delete, confirm
the next read misses cleanly rather than returning a ghost).

---

## 10. Going to Redis

Everything above runs against `HybridCache`'s built-in in-memory (L1) tier — fine for one
process, gone the moment it restarts, and not shared across a scaled-out deployment.

`HybridCache` is designed with a second tier (L2) in mind, and the upgrade is additive:

```csharp
// add the package: Microsoft.Extensions.Caching.StackExchangeRedis
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
});
```

Once an `IDistributedCache` implementation is registered, `HybridCache` uses it automatically
as L2 — behind the in-memory L1 it already has, transparently. **No change to
`CachingEquipmentDirectory`, `EquipmentCacheKeys`, `EquipmentCacheInvalidator`, or any
handler.** The whole point of hiding caching behind `IEquipmentDirectory` was to make this
exact change a configuration decision, not a code change.

What actually improves: a second instance of the API now shares the cache with the first —
an entry one instance populated is a hit on the other — and a restart no longer means a cold
cache. What to watch: `RemoveAsync` now evicts from a shared store, so invalidation correctness
matters more, not less, once more than one process is reading from the same cache.

---

## 11. The checklist

Before adding a cached read:

- [ ] This read is hit far more than the data changes ([chapter 2](#2-what-not-to-cache))
- [ ] A few seconds/minutes of staleness is genuinely acceptable for this data

Per cached read:

- [ ] A plain, uncached implementation exists first — the miss path
- [ ] The decorator implements the *same* interface as the plain implementation
- [ ] The cache key format lives in one static class, used by both the decorator and the invalidator
- [ ] The plain implementation is registered as its concrete type, not as the interface
- [ ] The interface resolves to the decorator, constructed with both dependencies

Per write that can affect a cached read:

- [ ] It calls the invalidator, by name, right where the write happens
- [ ] Every write path checked — not just the obvious ones (bulk imports, background jobs)

Per module:

- [ ] `HybridCache` is registered once, host-side — nothing module-specific to add there

Verification:

- [ ] A test proves a second read is served from cache (mutate underneath it, confirm staleness)
- [ ] A test proves a write invalidates it (write through the handler, confirm freshness)
- [ ] A test proves a delete invalidates it (confirm the next read is a clean miss, not a ghost)

---

## 12. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Reads never seem to get faster / DB logs show every call | The plain implementation is registered as the interface too, and wins the "last registration" race | [Chapter 8](#8-step-5--wire-it-into-di) — check registration order |
| Stale data survives a write forever | The handler doesn't call the invalidator | [Chapter 7](#7-step-4--invalidate-on-write) |
| Stale data survives *only some* writes | A write path (bulk import, background job) was never given the invalidator call | [Chapter 7](#7-step-4--invalidate-on-write) — grep every writer of the entity |
| Deleted entity still "exists" on read | The delete handler didn't invalidate before returning | [Chapter 7](#7-step-4--invalidate-on-write) |
| Two different values come back for what should be the same key | Two different key formats were typed by hand instead of sharing the `*CacheKeys` class | [Chapter 6](#6-step-3--one-cache-key-per-identity) |
| `InvalidOperationException: Unable to resolve service for type 'HybridCache'` | `AddHybridCache()` was never called, or called in a project that isn't wired into the host | [Chapter 8](#8-step-5--wire-it-into-di) |
| A search/list endpoint has its own confusing cache bugs | It's being cached at all — search results are a poor caching candidate | [Chapter 2](#2-what-not-to-cache) — stop caching it |
| Payload too large / entries silently missing under load | `MaximumPayloadBytes` too low for what's being cached, or memory pressure evicting entries early | Raise the limit, or cache a smaller projection |
| Multiple API instances see different data for the same id | Still on the in-memory (L1-only) tier — no shared store across instances | [Chapter 10](#10-going-to-redis) |

---

## 13. Cheat sheet

### The code you write

```csharp
// 1. The interface (Application/Abstractions) — narrow, one shape of question
public interface IEquipmentDirectory
{
    Task<GetEquipment.Response?> GetAsync(Guid equipmentId, CancellationToken cancellationToken);
}

// 2. The plain read (Infrastructure/Caching) — the miss path
internal sealed class EquipmentDirectory : IEquipmentDirectory { /* EF query */ }

// 3. The key format (Infrastructure/Caching) — one place, used by both sides
internal static class EquipmentCacheKeys
{
    public static string ForEquipment(Guid id) => $"equipment:{id}";
}

// 4. The decorator (Infrastructure/Caching)
internal sealed class CachingEquipmentDirectory : IEquipmentDirectory
{
    public Task<GetEquipment.Response?> GetAsync(Guid id, CancellationToken ct) =>
        _cache.GetOrCreateAsync(
            EquipmentCacheKeys.ForEquipment(id),
            (inner: _inner, id),
            static (state, ct) => new ValueTask<GetEquipment.Response?>(state.inner.GetAsync(state.id, ct)),
            cancellationToken: ct).AsTask();
}

// 5. The invalidator (Application/Abstractions + Infrastructure/Caching)
public interface IEquipmentCacheInvalidator { Task RemoveAsync(Guid id, CancellationToken ct); }
internal sealed class EquipmentCacheInvalidator : IEquipmentCacheInvalidator
{
    public Task RemoveAsync(Guid id, CancellationToken ct) =>
        _cache.RemoveAsync(EquipmentCacheKeys.ForEquipment(id), ct).AsTask();
}

// 6. Call it from every write that can make the value stale
await _cache.RemoveAsync(asset.Id, cancellationToken);

// 7. Wire it up (Infrastructure/DependencyInjection.cs)
services.AddScoped<EquipmentDirectory>();
services.AddScoped<IEquipmentDirectory>(provider => new CachingEquipmentDirectory(
    provider.GetRequiredService<EquipmentDirectory>(),
    provider.GetRequiredService<HybridCache>()));
services.AddScoped<IEquipmentCacheInvalidator, EquipmentCacheInvalidator>();

// 8. Once, host-side (Program.cs / host DependencyInjection.cs)
services.AddHybridCache(options =>
{
    options.MaximumPayloadBytes = 64 * 1024;
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1),
    };
});
```

### Going to Redis

```csharp
// dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis
builder.Services.AddStackExchangeRedisCache(options =>
    options.Configuration = builder.Configuration.GetConnectionString("Redis"));
// No other change — HybridCache uses it as L2 automatically.
```

---

## 14. Glossary

| Term | Meaning |
|---|---|
| **Cache-aside** | Check cache → miss → load from source → populate cache → return. This codebase's only caching pattern |
| **Cache key** | The string identifying one cached value. Centralized in one class per module so both sides agree on the format |
| **Decorator** | A class implementing the same interface as another, wrapping it to add behavior — here, caching — invisibly to callers |
| **Hit / miss** | Whether the cache already held the value asked for |
| **HybridCache** | `Microsoft.Extensions.Caching.Hybrid` — in-process (L1) cache with an optional distributed (L2) backing store |
| **Invalidation** | Removing a stale cache entry, called explicitly from the write that made it stale |
| **L1 / L2** | HybridCache's two tiers: L1 is always in-process memory; L2 is an optional distributed store (e.g. Redis) |
| **Read-through / write-through** | Alternative caching patterns where the data layer itself keeps the cache warm on every write. Not used here |
| **Stampede protection** | Collapsing many simultaneous misses for the same key into a single factory call, instead of one database hit per waiting caller |
| **TTL (time to live)** | How long an entry survives before expiring on its own, independent of invalidation |

---

## Where to go next

- **[Adding a feature](20-add-a-feature.md)** — the read/write shape a cached lookup sits on
  top of.
- **[Auditing](40-auditing.md)** — the other infrastructure decorator in this codebase, and
  why *that* one is automatic where caching is deliberately not.
- **[Authentication](70-authentication.md#4-step-1--api-keys)** — the same decorator shape
  applied to API key validation, if you want a second worked example.
