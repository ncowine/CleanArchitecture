# Real-Time Notifications — Pushing Changes With SignalR

**Who this is for:** someone who needs connected clients to see a change the moment it
happens — a dashboard, a live inventory board, a "someone else just edited this" banner —
without them polling an endpoint on a timer.

**What you'll be able to do by the end:** publish an event from a handler, understand exactly
when it's actually sent to clients (and why that moment is later than you'd guess), connect a
client and join a group, track who's present, and prove delivery in a test without a real
socket.

**What you need first:** a command whose handler already writes through the mediator
pipeline — [chapter 5](20-add-a-feature.md#5-step-3--write-the-slice) of
[Adding a feature](20-add-a-feature.md) is where that shape is written.

---

## Table of contents

| # | Chapter | What you do there |
|---|---|---|
| 1 | [What this gives you](#1-what-this-gives-you) | The job it does, and doesn't |
| 2 | [The moving parts](#2-the-moving-parts) | Collector → behavior → transport → hub |
| 3 | [Why the flush happens after commit](#3-why-the-flush-happens-after-commit) | The one design decision that matters most |
| 4 | [Step 1 — Publish from a handler](#4-step-1--publish-from-a-handler) | One call, no transport knowledge |
| 5 | [Step 2 — Name the group](#5-step-2--name-the-group) | A shared vocabulary, not a magic string |
| 6 | [Step 3 — Registration order](#6-step-3--registration-order) | Where this sits in the pipeline, and why it matters |
| 7 | [Step 4 — The transport](#7-step-4--the-transport) | Binding the abstraction to SignalR |
| 8 | [Step 5 — The hub and presence](#8-step-5--the-hub-and-presence) | Joining a group, tracking who's there |
| 9 | [Step 6 — A client, minimally](#9-step-6--a-client-minimally) | What actually connects to this |
| 10 | [Step 7 — Verify it without a socket](#10-step-7--verify-it-without-a-socket) | The recording-notifier test technique |
| 11 | [Delivery is best-effort](#11-delivery-is-best-effort) | What happens when a client never gets it |
| 12 | [Scaling past one instance](#12-scaling-past-one-instance) | The backplane question |
| 13 | [The checklist](#13-the-checklist) | Run this when doing it for real |
| 14 | [Troubleshooting](#14-troubleshooting) | Symptom, cause, fix |
| 15 | [Cheat sheet](#15-cheat-sheet) | Code, in one place |
| 16 | [Glossary](#16-glossary) | Every term used in this guide |

---

## 1. What this gives you

A client that's connected and subscribed sees a change **as it happens** — no polling, no
refresh button, no "did anything change since I last asked." Someone creates a piece of
equipment on one screen; everyone else watching the inventory board sees it appear.

It is not a message queue, not a way to trigger backend work, and not a delivery guarantee.
If nobody is connected when an event fires, that event is gone — there's no replay, no
catch-up on reconnect beyond whatever the client re-fetches itself. That's a deliberate
scope: this is a *notification* mechanism, telling connected clients "something changed, you
might want to re-check," not a system of record for what changed. If you need guaranteed,
ordered, replayable delivery of a business fact, that's what the outbox
([Talking across modules](60-talking-across-modules.md)) is for — and the two are not
mutually exclusive, they answer different questions.

---

## 2. The moving parts

```
   Handler                                  after the pipeline succeeds
      │ .Publish(group, event)                        │
      ▼                                                ▼
   RealtimeDispatch  ──(buffers, per request)──►  RealtimeDispatchBehavior
   (IRealtimeDispatch)                                  │ .Drain()
                                                         ▼
                                                  IRealtimeNotifier
                                                  (SignalRRealtimeNotifier)
                                                         │
                                                         ▼
                                                   IHubContext<PresenceHub>
                                                         │
                                                         ▼
                                          every connected client in that group
```

| Piece | Job |
|---|---|
| `IRealtimeDispatch` | What a handler injects. One method: `Publish(group, event)` |
| `RealtimeDispatch` | The scoped collector behind it — buffers events for the lifetime of the request |
| `RealtimeDispatchBehavior<,>` | Pipeline behavior that drains the buffer and sends, but only after `next()` returns successfully |
| `IRealtimeNotifier` | The transport abstraction. Default is a no-op; the host overrides it with SignalR |
| `SignalRRealtimeNotifier` | Binds `IRealtimeNotifier` to a real `IHubContext<PresenceHub>` |
| `RealtimeGroups` | Canonical group names, shared by publishers and the hub |
| `PresenceHub` | The actual SignalR hub clients connect to — join/leave a group, get presence updates |
| `IPresenceTracker` | Who's currently in each group, keyed by connection id |

Everything above the dashed line lives in `BuildingBlocks.RealTime` and knows nothing about
SignalR. Everything below it lives in `CleanArch.Api/Realtime/` and is the one place SignalR
is actually referenced. That split is what makes step 7 ("what if this needs to be something
other than SignalR one day") a host-only change.

---

## 3. Why the flush happens after commit

This is the one decision in this whole feature that isn't obvious until you've been burned
by its absence.

If a handler notified clients directly, in the middle of doing its work, a request that
later fails — a validation the database itself rejects, a concurrency conflict, an exception
after the notify call but before the transaction commits — would have already told every
connected client about a change that never actually happened. There's no way to un-ring that
bell; SignalR has no "actually, ignore that."

So publishing is split into two steps:

1. **`Publish` only buffers.** Calling it from a handler doesn't send anything — it appends
   to an in-memory list for the current request.
2. **`RealtimeDispatchBehavior` drains that buffer only after the inner pipeline returns —**
   and because it's registered *outside* each module's transaction behavior
   ([chapter 6](#6-step-3--registration-order)), "the inner pipeline returned" means the
   database transaction already committed.

```csharp
// BuildingBlocks.RealTime/RealtimeDispatchBehavior.cs
public async Task<TResponse> Handle(
    TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
{
    var response = await next();

    // Reached only when the inner pipeline (including any transaction commit) succeeded.
    foreach (var (group, realtimeEvent) in _dispatch.Drain())
    {
        try
        {
            await _notifier.NotifyGroupAsync(group, realtimeEvent, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RealtimeLog.NotifyFailed(_logger, exception, realtimeEvent.Type, group);
        }
    }

    return response;
}
```

If `next()` throws, the `foreach` is never reached, `Drain()` is never called, and whatever
was buffered simply disappears with the failed request. Clients are never told about a
change that rolled back.

> **Why this matters:** the wrong-but-tempting alternative is to notify from inside the
> handler, right after the domain change, because it feels closer to "where the thing
> happened." That's exactly backwards — the domain change isn't real until the transaction
> commits, and a notification is a promise you're making to every connected client.

---

## 4. Step 1 — Publish from a handler

A handler depends on the interface, not the transport:

```csharp
// Equipment.Application/Inventory/CreateEquipment.cs (excerpt)
public sealed class Handler : IRequestHandler<Command, Guid>
{
    private readonly IEquipmentRepository _equipment;
    private readonly IRealtimeDispatch _realtime;

    public Handler(IEquipmentRepository equipment, IRealtimeDispatch realtime)
    {
        _equipment = equipment;
        _realtime = realtime;
    }

    public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
    {
        var asset = EquipmentAsset.Create(command.Name, command.Category, command.AssetTag);
        await _equipment.AddAsync(asset, cancellationToken);

        _realtime.Publish(RealtimeGroups.Equipment(), new RealtimeEvent("EquipmentCreated", new
        {
            id = asset.Id,
            name = asset.Name,
            category = asset.Category.ToString(),
            assetTag = asset.AssetTag,
            status = asset.Status.ToString(),
        }));

        return asset.Id;
    }
}
```

Three things worth noticing:

- **`RealtimeEvent(Type, Payload)`** — `Type` is the client-facing event name (what a client
  subscribes to; see [chapter 9](#9-step-6--a-client-minimally)), `Payload` is an anonymous
  object serialized when it's actually sent. Name the type the way you'd name an audit
  action — something a client developer would recognize, not a generic `"Changed"`.
- **The payload is a fresh, small anonymous object**, not the domain entity. Never publish
  the entity itself — it can carry more than clients should see, and it couples the wire
  shape to your domain model's shape.
- **This line runs before the method returns, but nothing is sent yet** — see
  [chapter 3](#3-why-the-flush-happens-after-commit).

`UpdateEquipment.Handler` and `DeleteEquipment.Handler` follow the identical shape —
`EquipmentUpdated` and `EquipmentDeleted` — each right after the write it describes, in
`Equipment.Application/Inventory/`.

---

## 5. Step 2 — Name the group

A **group** is who receives the event — everyone currently subscribed to that name. Group
names are centralized so the publisher and the hub can't drift apart on what a group is
called:

```csharp
// BuildingBlocks.RealTime/RealtimeGroups.cs
public static class RealtimeGroups
{
    /// Everyone watching equipment inventory changes.
    public static string Equipment() => "equipment";
}
```

A method, not a constant, so a group that needs an id can take one —
`RealtimeGroups.OnboardingRequest(requestId)` for a per-request audience would follow the
same shape. `Equipment()` takes none because there's exactly one inventory to watch; a group
scoped to one entity would look like:

```csharp
public static string OnboardingRequest(Guid requestId) => $"onboarding:{requestId}";
```

**Why this matters:** a raw string literal typed independently at the publish call site and
the hub's `JoinGroup` call is one typo away from an event nobody in the room ever receives —
and nothing errors, because SignalR groups are created on first use; a mistyped group name
is a *valid*, empty group.

---

## 6. Step 3 — Registration order

```csharp
// Program.cs (excerpt)
builder.Services
    // ...
    .AddMediator()
    // Registered after the mediator and before the modules, so the post-commit realtime dispatch behavior
    // sits outside each module's transaction behavior (its flush runs after the commit).
    .AddRealtimeDispatch()
    .AddEquipmentModule(equipmentConnectionString)
    .AddOnboardingModule(onboardingConnectionString);
```

`AddRealtimeDispatch()` registers `RealtimeDispatchBehavior<,>` as a pipeline behavior.
Pipeline behaviors wrap in registration order — first registered, outermost. Calling it
**after** `AddMediator()` (which sets up the pipeline itself) and **before** the modules
(which each add their own `TransactionBehavior<,>`) is what makes the dispatch behavior sit
*outside* every module's transaction — which is the mechanism [chapter 3](#3-why-the-flush-happens-after-commit)
depends on. Reorder those two lines and the dispatch behavior would run *inside* the
transaction, flushing before the commit, and the whole "never notify a rollback" guarantee
would quietly stop holding.

`AddRealtimeDispatch()` itself, in `BuildingBlocks.RealTime/DependencyInjection.cs`:

```csharp
public static IServiceCollection AddRealtimeDispatch(this IServiceCollection services)
{
    services.AddScoped<RealtimeDispatch>();
    services.TryAddScoped<IRealtimeDispatch>(provider => provider.GetRequiredService<RealtimeDispatch>());
    services.TryAddScoped<IRealtimeNotifier, NullRealtimeNotifier>();
    services.TryAddSingleton<IPresenceTracker, InMemoryPresenceTracker>();

    services.AddScoped(typeof(IPipelineBehavior<,>), typeof(RealtimeDispatchBehavior<,>));

    return services;
}
```

It registers a **no-op** `IRealtimeNotifier` by default (`TryAddScoped`, so it only takes
effect if nothing overrides it). That means every module works, and every existing test
keeps passing, even in a host that never wires up a real transport at all — publishing
without a transport just quietly does nothing, rather than failing.

---

## 7. Step 4 — The transport

The host overrides the no-op with a real one, right after the block above:

```csharp
// Program.cs (excerpt)
// Real-time transport (SignalR) — overrides the kit's no-op notifier and hosts the presence hub.
builder.Services.AddSignalR();
builder.Services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();
```

```csharp
// CleanArch.Api/Realtime/SignalRRealtimeNotifier.cs
internal sealed class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<PresenceHub> _hub;

    public SignalRRealtimeNotifier(IHubContext<PresenceHub> hub)
    {
        _hub = hub;
    }

    public Task NotifyGroupAsync(string group, RealtimeEvent realtimeEvent, CancellationToken cancellationToken) =>
        _hub.Clients.Group(group).SendAsync(realtimeEvent.Type, realtimeEvent.Payload, cancellationToken);
}
```

One line of actual work: send `realtimeEvent.Type` (the client-facing event name) as the
SignalR client method name, with `realtimeEvent.Payload` as its argument, to everyone
currently in `group`.

**Why this lives host-side, not module-side:** nothing in `Equipment` or `Onboarding`
references SignalR, `Microsoft.AspNetCore.SignalR`, or `PresenceHub` — they only reference
`IRealtimeDispatch` and `RealtimeGroups`, both in `BuildingBlocks.RealTime`. A module that
never needs a transport swap doesn't carry a dependency on one; only the host, which decides
what transport actually exists, does.

---

## 8. Step 5 — The hub and presence

`PresenceHub` is the concrete endpoint clients connect to (`/hubs/presence`, mapped in
`Program.cs`). It does two jobs: lets a client join/leave a group, and tracks who's present
in each group so a "3 people viewing this" indicator is possible.

```csharp
// CleanArch.Api/Realtime/PresenceHub.cs
public sealed class PresenceHub : Hub
{
    private readonly IPresenceTracker _presence;

    public async Task JoinGroup(string group)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        _presence.Join(group, Context.ConnectionId, CurrentUser());
        await BroadcastPresenceAsync(group);
    }

    public async Task LeaveGroup(string group)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        _presence.Leave(Context.ConnectionId);
        await BroadcastPresenceAsync(group);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var groups = _presence.GroupsFor(Context.ConnectionId);
        _presence.Leave(Context.ConnectionId);
        foreach (var group in groups)
        {
            await BroadcastPresenceAsync(group);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private Task BroadcastPresenceAsync(string group) =>
        Clients.Group(group).SendAsync("presence", new { group, users = _presence.UsersIn(group) });
}
```

A client calling `JoinGroup("equipment")` both subscribes to that group's SignalR messages
*and* registers as present in it — the same call does both jobs, so there's no separate
"announce yourself" step to forget. `OnDisconnectedAsync` walks every group the connection
was in (`GroupsFor`) so a dropped connection is cleaned out of all of them, not just the
last one it joined.

`IPresenceTracker`'s default implementation, `InMemoryPresenceTracker`, is exactly what it
sounds like — two `ConcurrentDictionary`s, one from group to members, one from connection to
the groups it's in, so a disconnect can find and clean up every group in one pass without
scanning all of them. It's process-local, which is exactly the limitation
[chapter 12](#12-scaling-past-one-instance) is about.

---

## 9. Step 6 — A client, minimally

There's no consuming client checked into this repository today — the desktop client this kit
used to ship was built against the modules this rewrite replaced, and a new one hasn't been
built yet. What follows is the minimal shape any client needs, using Microsoft's
`@microsoft/signalr` package, so you have something to point a real client at:

```javascript
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/presence")
    .withAutomaticReconnect()
    .build();

connection.on("EquipmentCreated", (payload) => {
    console.log("created:", payload.id, payload.name);
});
connection.on("EquipmentUpdated", (payload) => { /* ... */ });
connection.on("EquipmentDeleted", (payload) => { /* ... */ });
connection.on("presence", ({ group, users }) => {
    console.log(`${users.length} present in ${group}`);
});

await connection.start();
await connection.invoke("JoinGroup", "equipment");
```

Two things to get right, both easy to miss the first time:

- **`connection.on(...)` names must match `RealtimeEvent.Type` exactly** — `"EquipmentCreated"`,
  not `"equipmentCreated"` or `"EquipmentCreatedEvent"`. There's no compiler checking this
  from JavaScript against the C# string.
- **Call `JoinGroup` with the same string `RealtimeGroups.Equipment()` returns** — `"equipment"`.
  Hardcoding it on the client is fine (there's no way to share the C# static class with a
  JS client), but it has to match, and nothing warns you if it doesn't
  ([chapter 5](#5-step-2--name-the-group)).

`withAutomaticReconnect()` handles the socket dropping and coming back, but reconnecting
doesn't replay anything missed while disconnected — see [chapter 1](#1-what-this-gives-you).
A client that cares about missed events should re-fetch the current state on reconnect, not
assume the stream is gap-free.

---

## 10. Step 7 — Verify it without a socket

Standing up a real SignalR connection in a test is possible but heavy. `EquipmentModuleTests`
substitutes a recording fake for `IRealtimeNotifier` instead — the same interface
`SignalRRealtimeNotifier` implements, minus the actual socket:

```csharp
// tests/CleanArch.Api.IntegrationTests/EquipmentModuleTests.cs (excerpt)
services.AddHybridCache();
services.AddMediator();
services.AddRealtimeDispatch();
services.AddSingleton<IRealtimeNotifier, RecordingRealtimeNotifier>();
services.AddEquipmentModule($"Data Source={_dbPath}");

// ...

private sealed class RecordingRealtimeNotifier : IRealtimeNotifier
{
    public List<(string Group, RealtimeEvent Event)> Sent { get; } = new();

    public Task NotifyGroupAsync(string group, RealtimeEvent realtimeEvent, CancellationToken cancellationToken)
    {
        Sent.Add((group, realtimeEvent));
        return Task.CompletedTask;
    }
}
```

The test that exercises it goes through the real mediator pipeline — real transaction
behavior, real `RealtimeDispatchBehavior`, real database — with only the transport swapped
out:

```csharp
[Fact]
public async Task Creating_equipment_persists_it_and_publishes_a_realtime_event()
{
    var id = await sender.Send(
        new CreateEquipment.Command("ThinkPad X1", EquipmentCategory.Laptop, "LAP-001"), default);

    var stored = await sender.Send(new GetEquipment.Query(id), default);
    Assert.NotNull(stored);

    var (group, evt) = Assert.Single(_notifier.Sent);
    Assert.Equal("equipment", group);
    Assert.Equal("EquipmentCreated", evt.Type);
}
```

This is the same technique — substitute the seam, not the whole transport stack — that
[Testing](80-testing.md) uses throughout: `IRealtimeNotifier` is exactly the abstraction that
exists to make this possible, the same way `IEquipmentReservationService` makes a saga step
testable without a real second module.

To go further and prove the pipeline ordering itself
([chapter 3](#3-why-the-flush-happens-after-commit)) rather than just one happy path, write
the negative case: make the handler throw after calling `Publish`, and assert
`_notifier.Sent` is still empty. That's what proves the behavior never flushes a rolled-back
request, rather than merely asserting that it flushes a successful one.

---

## 11. Delivery is best-effort

Look again at the `try`/`catch` in `RealtimeDispatchBehavior`:

```csharp
try
{
    await _notifier.NotifyGroupAsync(group, realtimeEvent, cancellationToken);
}
catch (Exception exception) when (exception is not OperationCanceledException)
{
    RealtimeLog.NotifyFailed(_logger, exception, realtimeEvent.Type, group);
}
```

If the notify call throws — SignalR's backplane is unreachable, say — it's logged and
swallowed, not surfaced to the caller. By the time this code runs, the business operation has
already committed successfully; a notification failure must never turn a successful write
into a failed HTTP response. The request that created the equipment still returns `201`, the
row is still there, and the only casualty is that connected clients found out a moment later
(on their next fetch) instead of instantly.

**Why this matters:** the alternative — let a notify failure bubble up and fail the request —
would mean an outage in your real-time transport takes down equipment creation, which is a
far more valuable feature than "instant" being instant. If a caller genuinely needs to know
notification failed, that's a different, explicit requirement — not something to bolt onto
this behavior's default.

---

## 12. Scaling past one instance

`InMemoryPresenceTracker` and SignalR's default group membership are both **per process**.
Run two instances of the API behind a load balancer and you get two independent worlds: a
client connected to instance A never receives an event published from a handler running on
instance B, and instance A's presence count only reflects who's connected *to A*.

The fix, when you need more than one instance, is a **SignalR backplane** — most commonly
Redis (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`), which fans a message sent on one
instance out to every instance's connected clients:

```csharp
builder.Services.AddSignalR()
    .AddStackExchangeRedis(builder.Configuration.GetConnectionString("Redis"));
```

That solves message delivery across instances. Presence needs its own answer — swap
`IPresenceTracker`'s registration for a Redis-backed implementation (the interface is
already transport-agnostic; see [chapter 8](#8-step-5--the-hub-and-presence)) so "who's in
this group" is a shared answer, not one per process.

This is the same shape of upgrade as [caching's move to Redis](45-caching.md#10-going-to-redis) —
a single-process default that a distributed backing store slots into later, behind an
interface that was already there.

---

## 13. The checklist

Per event you publish:

- [ ] The handler depends on `IRealtimeDispatch`, not a concrete transport
- [ ] `Publish` is called after the domain change, with a small, purpose-built payload — never the entity itself
- [ ] The event's `Type` name is one a client developer would recognize and search for
- [ ] The group comes from `RealtimeGroups`, not a hand-typed string

Per host:

- [ ] `AddRealtimeDispatch()` is called after `AddMediator()` and before any module's registration
- [ ] `IRealtimeNotifier` is overridden with a real transport (SignalR or otherwise) — the default is a silent no-op
- [ ] The hub is mapped (`app.MapHub<PresenceHub>(...)`)

Per client:

- [ ] Event names subscribed to (`connection.on(...)`) match `RealtimeEvent.Type` exactly
- [ ] The group joined matches what `RealtimeGroups` actually returns
- [ ] Reconnect logic re-fetches state rather than assuming no events were missed

Before scaling to more than one instance:

- [ ] A SignalR backplane is configured
- [ ] `IPresenceTracker` is backed by a shared store, not the in-memory default

Verification:

- [ ] A test proves the event fires on success, using a recording `IRealtimeNotifier`
- [ ] A test proves nothing fires when the handler throws

---

## 14. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Nothing ever arrives at any client | `IRealtimeNotifier` was never overridden — it's still the no-op default | [Chapter 7](#7-step-4--the-transport) |
| A specific event never arrives, others do | Event type name mismatch between `RealtimeEvent.Type` and the client's `connection.on(...)` | [Chapter 9](#9-step-6--a-client-minimally) |
| A client is connected but sees nothing | It never called `JoinGroup`, or joined the wrong group name | [Chapter 5](#5-step-2--name-the-group), [chapter 9](#9-step-6--a-client-minimally) |
| A write that fails validation still seems to notify | `AddRealtimeDispatch()` was registered *after* a module's transaction behavior instead of before | [Chapter 6](#6-step-3--registration-order) |
| A write that later throws still notifies | Same as above — check registration order first | [Chapter 6](#6-step-3--registration-order) |
| Presence count is wrong / stuck | `OnDisconnectedAsync` didn't run (an ungraceful client crash) — the in-memory tracker eventually times the connection out via SignalR's own keep-alive, but not instantly | Expected transient behavior; verify with a clean `LeaveGroup` first |
| Two API instances show different connected users / miss each other's events | No SignalR backplane — presence and groups are per-process | [Chapter 12](#12-scaling-past-one-instance) |
| A notify exception never surfaces anywhere, not even logs | Check the log level — `RealtimeDispatchBehavior` logs at `Warning`, not `Error` | [Chapter 11](#11-delivery-is-best-effort) |
| A test never sees the expected notification | Something threw inside the handler before `Publish` was reached, or the drain never ran because the pipeline never returned normally | Check the test's other assertions first — a thrown exception is often the real story |

---

## 15. Cheat sheet

### The code you write

```csharp
// 1. Publish from a handler, after the domain change
_realtime.Publish(RealtimeGroups.Equipment(), new RealtimeEvent("EquipmentCreated", new
{
    id = asset.Id,
    name = asset.Name,
}));

// 2. Add a new group name (BuildingBlocks.RealTime/RealtimeGroups.cs)
public static string OnboardingRequest(Guid requestId) => $"onboarding:{requestId}";

// 3. Registration (Program.cs) — order matters
.AddMediator()
.AddRealtimeDispatch()          // after mediator, before modules
.AddYourModule(connectionString)

// 4. Transport override (Program.cs) — after AddRealtimeDispatch
builder.Services.AddSignalR();
builder.Services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();

// 5. Map the hub
app.MapHub<PresenceHub>("/hubs/presence");
```

### The client (`@microsoft/signalr`)

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/presence")
    .withAutomaticReconnect()
    .build();

connection.on("EquipmentCreated", (payload) => { /* ... */ });
connection.on("presence", ({ group, users }) => { /* ... */ });

await connection.start();
await connection.invoke("JoinGroup", "equipment");
// ... later
await connection.invoke("LeaveGroup", "equipment");
```

### Verifying without a socket

```csharp
services.AddSingleton<IRealtimeNotifier, RecordingRealtimeNotifier>(); // records instead of sending

var (group, evt) = Assert.Single(_notifier.Sent);
Assert.Equal("equipment", group);
Assert.Equal("EquipmentCreated", evt.Type);
```

### Scaling to more than one instance

```csharp
builder.Services.AddSignalR()
    .AddStackExchangeRedis(builder.Configuration.GetConnectionString("Redis"));
// and: swap IPresenceTracker's registration for a Redis-backed implementation
```

---

## 16. Glossary

| Term | Meaning |
|---|---|
| **Backplane** | A shared message bus (e.g. Redis) letting multiple SignalR server instances fan events out to every instance's clients |
| **Best-effort delivery** | A notification that failed to send is logged and dropped, never turned into a failed request |
| **Group** | A named audience — every connection currently subscribed receives events published to it |
| **Hub** | The concrete SignalR endpoint (`PresenceHub`) a client connects to |
| **Notifier** | `IRealtimeNotifier` — the transport abstraction between the dispatch behavior and the real socket layer |
| **Presence** | Tracking who is currently connected to a given group |
| **Publish (buffer)** | Calling `IRealtimeDispatch.Publish` — this only queues the event for the current request, it does not send it |
| **RealtimeEvent** | The `(Type, Payload)` pair sent to clients — `Type` is the client-facing event/method name |
| **SignalR** | ASP.NET Core's real-time library — WebSockets with automatic fallback transports |

---

## Where to go next

- **[Caching](45-caching.md)** — the other infrastructure decorator hidden behind a plain
  interface, and the twin of this guide's "swap the backing store later" upgrade path.
- **[Talking across modules](60-talking-across-modules.md)** — when you need guaranteed,
  ordered, replayable delivery of a fact instead of a best-effort live nudge.
- **[Testing](80-testing.md)** — the general shape of substituting a seam (like
  `IRealtimeNotifier`) instead of standing up the real transport in a test.
