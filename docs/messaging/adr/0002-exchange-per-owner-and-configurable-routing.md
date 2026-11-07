# ADR 0002: Exchange per owner, configurable routing, and discovering events by assembly

- **Status:** Accepted
- **Date:** 2026-09-26
- **Amends:** [ADR 0001](0001-decouple-messaging-from-prism.md), sections 1–3, and the test baseline from delivery step 0. Everything else in ADR 0001 stands: the layers, the core and hosting split, serialization, observability, shared queues and dependency hygiene.
- **Update (2026-10-31):** `Route<T>()` was removed from `Messaging.Hosting`. Registering every published class didn't scale to hundreds of events, and the legacy router never needed it. A server publishes like a legacy app: to its only bus, keyed by the wire name, with `PublishOptions.RoutingKey` for a per-call key. Publishing with several buses throws. Below, `Route<T>()`, `.To(...)` and `WithRoutingKey` are history.
- **Update (2026-11-04):** events no longer belong to a bus. `[RemoteEvent("bus")]` and the bus parameter of `RemoteEventRegistry.Add` are removed: a breaking change to the legacy adapter's public API, made on purpose. The publisher picks the bus at publish time: the bus it names (`PublishRemote(payload, new PublishOptions { Bus = "Partners" })`, which replaces the old `IRabbitMQService` parameter), or else the default. `PublishOptions` also carries the routing key, so `PublishRemoteTo` and `PublishRemoteOnlyTo` are removed and mentions of them below are history. `Messaging:DefaultBus` in configuration names the default, and being one value there can be only one; since 2026-11-05 it is required, even with a single bus. With several buses and no default, a publish that names no bus throws. Every registered event is received on every bus; `AddHandlers` and `AddMessages` can still limit a server's handlers to some buses. Below, mentions of `[RemoteEvent]`, `Add(assembly, bus)` and "publishing with several buses throws" are history.

- **Update (2026-11-05):** the real legacy library routes by **application name**, not event name (legacy facts F5 to F7): it publishes every event with its own (= exchange) name as the routing key and binds each exchange once, with the owner's name. The new code now does the same. Subscribing only decides which received messages are dispatched; it never changes bindings, so the 2026-11-04 update below no longer holds for bindings. Section 2's per-type default and section 3's wire-name default are replaced as marked. Reason: one binding per event multiplies the broker's bindings by the number of events, and a load test crashed a broker at 300,000.
- **Update (2026-11-04):** on the modern side, a queue binds only what the application listens for. `IMessageSubscriber.Subscribe<T>` and `MessageEvent<T>.Subscribe` start receiving the type on every bus, so `AddMessages` is only needed for a custom `IMessageSink`. It keeps a client's bindings down when many clients are connected. On the legacy side `RemotePubSubEvent<T>` does the same with no registration, and `PubSubEvent<T>` is local by default. That reverses this ADR's "no base class" in section 4 for events that move: an event assembly now references `Common.RabbitMQ` and each event changes its base class, in exchange for far fewer bindings. `RemoteEventRegistry.Add` stays for assemblies not yet moved, and binds all their events at startup because the library can't see a plain event's `Subscribe`. When the last subscriber goes, on either side, a per-instance queue's binding is removed; a collected weak subscriber is noticed at the next message. A durable shared queue keeps the binding, because it belongs to every instance of the service. Explicit `Unsubscribe` and `UnregisterQueue` still unbind.
- **Update (2026-11-07):** only `RemotePubSubEvent<T>` travels. `PublishRemote` and `PublishRemoteOnly` take a `RemotePubSubEvent<T>`, and `RemoteEventRegistry.Add` and the assemblies parameter of `RabbitMQServiceRouter.FromConfiguration` are removed (breaking, user-approved). Apps no longer share event assemblies: each declares its own event, and `[Message("wire name")]` on a `RemotePubSubEvent<T>` sets the wire name, as it already did on the modern side, so it matches an old app's full type name. Apps on the old library are untouched. This replaces section 4's "plain events found by assembly"; mentions of `Add` above are history.

## Context

ADR 0001 and the replica in this repository assume a topology the real system doesn't use. These facts about the real system came out after steps 0–3b were merged:

| | Replica and ADR 0001 | Real system |
|---|---|---|
| Events | `RemotePubSubEvent<T>`, each with `[RemoteEvent("bus")]` | Plain `PubSubEvent<T>`, with no attribute and no special base class. There are thousands of them. |
| Event assemblies | One `Common.Events` | Legacy apps and modern apps each have their own. A few common events live in a netstandard2.0 project. |
| Exchanges | One shared topic exchange per "bus", used by every app | **One exchange per app.** AppA publishes to its own exchange. AppB binds to AppA's exchange when it needs AppA's events. |
| Exchange type | Topic, hard-coded | Must be configurable: topic, direct, fanout or headers |
| Routing key | Always the wire name | **Chosen by the publishing module** |
| Virtual hosts | `legacy` and `modern`, isolated from each other | The default `/`. Nobody has had to configure one. |
| Modern apps | Prism events over the `Modern` bus | Should use plain message classes, with **no Prism.Core** |

Two consequences:

- **Requiring `[RemoteEvent]` means editing thousands of files.** That breaks ADR 0001's rule that legacy apps need little or no code change.
- **The compatibility tests prove the wrong thing.** The frozen baseline (`tests/Fixtures/Common.RabbitMQ.Baseline`) is the replica, not the real legacy library, so the tests show compatibility with a topology the real apps don't use.

What ADR 0001 got right still holds. The core and the server already identify a message by its **wire name**, carried in the `event-type` header, and never by its exchange or CLR type. Receivers in both the current and baseline builds read the header first and fall back to the routing key only when it's missing. So routing can change without changing identity.

## Decision

### 1. Terms

| Term | Meaning |
|---|---|
| **Wire name** | What a message *is*. Sent in the `event-type` header on every message. Legacy events use their full type name; plain classes use `[Message("…")]`. Unchanged from ADR 0001. |
| **Owner** | The **application** that publishes a message. Each application owns exactly one exchange. Modules inside an application share it and tell their messages apart by routing key. |
| **Routing key** | *Where* a message goes. Chosen by the owner, with the owner's exchange name as the default (the wire name until 2026-11-05). Never used for identity. |
| **Subscription** | A binding from an app's queue to an exchange, with a routing-key pattern or header match. |
| **Connection** | One broker, one virtual host (default `/`). An app normally has exactly one. |

"Bus" (Legacy/Modern) stops being a design concept. The code type `RabbitMQBus` stays, but now means one connection with one owned exchange and a list of subscriptions.

### 2. Topology

- **One exchange per application.** Each application publishes only to its own exchange. The type (`topic` by default, or `direct`, `fanout` or `headers`) and durability are configurable.
- **Set the exchange name explicitly.** Subscribers bind to it by name, so it is part of the contract. It falls back to the application's name, but the fallback shouldn't be relied on. The legacy client name defaults to the process name (`WpfApp.Net472.exe`), so renaming an executable would silently move the exchange.
- **Owners declare their own exchange; everyone else only checks it.** A subscriber passively checks a foreign exchange and never declares or re-types it. If the exchange doesn't exist yet, the subscriber's connect loop retries until the owner has started. An owner that starts later is normal, not an error.
- **Subscriptions are an explicit list.** Each entry names an exchange and how to bind to it:
  - `topic`: patterns (`orders.*.saved`, `AppA.Events.#`)
  - `direct`: exact keys
  - `fanout`: no key
  - `headers`: `x-match` all or any, plus header values
- **One queue can bind to many exchanges.** An app still has one queue per mode, bound to every exchange it subscribes to. Queue modes are unchanged from ADR 0001: per-instance for clients, shared (quorum, retries, dead-letter queue) for servers.
- **Subscriptions without keys or a header match** bind once, with the exchange's name as the key, as legacy applications do (F7). This is the default, so a plain `{ Exchange = "AppA" }` behaves like a legacy subscription. The own exchange is bound the same way (F6).
- **Every subscription brings in types a service doesn't handle.** Those are acknowledged and skipped, never dead-lettered, on both queue modes. (Until 2026-11-05 per-type bindings dead-lettered unknown messages on shared queues.)
- **One virtual host, `/`, by default.** A queue can only bind to exchanges in its own virtual host, so apps that talk to each other must share one. It stays configurable for isolation, but nothing needs to set it.

### 3. Routing keys

- **The routing key belongs to the publishing module.** The default is the publisher's exchange name, which is what legacy applications do (F5). A module can override it at three levels:

  ```csharp
  // Modern / server: a module sets its convention once, statically or from the message
  messaging.Route<OrderSaved>().WithRoutingKey(o => $"orders.{o.Region}.saved");

  // ...or for one call
  publisher.Publish(order, new PublishOptions { RoutingKey = "orders.eu.saved" });
  ```

  ```csharp
  // Legacy: a new method, not an overload (see below). Existing PublishRemote calls are unchanged.
  eventAggregator.GetEvent<OrderSaved>().PublishRemoteTo("orders.eu.saved", order);
  ```

  ```xml
  <!-- Legacy: optional defaults per event or namespace, so existing call sites don't change -->
  <route events="AppA.Events.Orders.*" key="orders.{Region}.saved" />
  ```

- **Why a new method name.** An overload `PublishRemote(payload, string routingKey)` next to the existing `PublishRemote(payload, IRabbitMQService service = null)` would make existing calls written as `PublishRemote(x, null)` ambiguous, which is a compile break. `PublishRemoteTo(routingKey, payload)` avoids that.
- **`event-type` is mandatory on every message** the core sends. Receivers identify messages by the header, so any routing key is safe for every receiver build. The routing-key fallback stays only for senders outside our code that put the wire name in the key.
- **Custom keys don't reach legacy subscribers.** Legacy subscribers bind the owner's name (fact F7), so a message published with a custom key reaches only subscribers that bind that key. Use custom keys for new subscribers, and keep the default key for anything legacy applications receive. Pinned by `CoreTopologyTests.CustomRoutingKey_ReachesSubscribersThatBindIt_NotLegacySubscribers`.
- **A routing-key convention is a contract with subscribers.** Receivers can't break on a key change, but they stop *getting* messages whose key no longer matches their binding. Changing a convention needs the same coordination as changing a public API.

### 4. Discovering events in the legacy adapter

- **Events are found by assembly.** Each app lists the event assemblies it takes part in once, at startup or in App.config. Every concrete `PubSubEvent<T>` subclass in them can be sent and received. There's no attribute, no base class and no per-event edit.

  ```csharp
  RemoteEventRegistry registry = new RemoteEventRegistry()
      .Add(typeof(AppA.Events.OrderSaved).Assembly)
      .Add(typeof(Shared.Events.UserChanged).Assembly);
  ```

- **`PublishRemote` works on any `PubSubEvent<T>`.** This is an additive extension. Existing `RemotePubSubEvent<T>` and `[RemoteEvent]` code keeps compiling and working.
- **The registry only decides what the app can *understand*, not what it *receives*.** Subscriptions decide what arrives. A message whose wire name isn't in the registry is ignored and logged, as today.
- **Local-only events are harmless.** An event like `EmployeeSelected` in a listed assembly is registered but never sent, because nothing calls `PublishRemote` on it. An optional `[LocalEvent]` can exclude one explicitly.

### 5. Modern apps and the server

- **Plain classes with `[Message("wire-name")]`, and no Prism.** Contract projects reference only `Messaging.Abstractions`.
- **Publishing and handling are separate from topology:**

  ```csharp
  services.AddMessaging(messaging => messaging
      .AddConnection(configuration.GetSection("Messaging"))        // own exchange, type, vhost
      .Route<OrderSaved>().WithRoutingKey(o => $"orders.{o.Region}.saved")
      .Subscribe(exchange: "AppA", keys: "AppA.Events.#")
      .Handle<CustomerChanged, CustomerChangedHandler>());
  ```

  Handlers say *which message types* they handle. Subscriptions say *which exchanges and keys* feed the queue.
- **Common netstandard2.0 events.** Legacy apps keep the Prism version. Modern apps get a plain-class twin with the **same wire name** and stop referencing the Prism project. The Prism version is deleted when legacy is retired. This is the pattern ADR 0001 already uses for the server (`Common.Events.EmployeeUpdated` next to `Employees.Contracts.EmployeeUpdated`).
- **Telemetry becomes opt-in** (`.AddTelemetry()`; removed altogether on 2026-11-05, see ADR 0001) so modern clients don't get server observability imposed on them, as ADR 0001 section 5 intended.
- **Desktop apps don't need Prism either.** A view model can't be a scoped handler class, so `Messaging.Hosting` also offers `IMessageSubscriber`: `Subscribe<T>(action, synchronizationContext)` returns an `IDisposable`. It is an `IMessageSink`, so it runs after the handlers, for the exact message type, in subscription order. Passing the UI thread's context posts the call there, like Prism's `ThreadOption.UIThread`. A posted call can't fail the message, because it runs later: its exceptions go to the dispatcher. Without a context, or with the async overload, an exception fails the message like a handler's. Subscriptions are strong references, not Prism's weak ones, so a short-lived subscriber must dispose them.

### 6. The server as publisher (long term)

When CRUD moves to the server, the server publishes events that apps publish today. **Proposed:** the server publishes into the **owning app's exchange**, for example `OrderSaved` into `AppA`, acting on behalf of that domain. Legacy subscribers need no configuration change. The alternative is for the server to publish into its own exchange, with every client adding a subscription to it. That is cleaner ownership, but it touches every client's configuration. See open question 4.

### 7. Compatibility contract (replaces ADR 0001 section 3)

Still frozen:

- the wire name, and the `event-type` and `source-id` headers (plus additive headers only);
- the UTF-8 JSON body as Newtonsoft 12 writes it;
- the per-instance queue shape: exclusive, auto-delete, non-durable;
- the echo drop, the fire-and-forget outgoing buffer, and the ack behaviour on per-instance queues.

No longer frozen, but **configured to match the real system**:

- exchange names and types;
- the routing key, which defaults to the exchange name;
- bindings.

### 8. Test baseline

- **The frozen baseline must be the real legacy library.** Its source is preferred; failing that, the replica rebuilt to reproduce the real library's observable behaviour, including its exchange declaration, routing keys and bindings. Until then, "compatible" only means compatible with the replica.
- **Step 5 as built (2026-09-26).** The real library isn't reachable from this repository, so the baseline is a **model** (`tests/Fixtures/LegacyModel`). It is built from the confirmed facts, with every other behaviour a named, tested assumption listed in [`docs/legacy-baseline-assumptions.md`](../legacy-baseline-assumptions.md). Checking those assumptions against the real library turns the model into the real baseline, one assumption at a time. The replica baseline (`tests/Fixtures/Common.RabbitMQ.Baseline`) stays until steps 6–7, because the current build is still compared against it.
- **The broker test matrix grows** to cover:
  - each exchange type;
  - custom routing keys received by the old build;
  - cross-exchange subscriptions;
  - a subscriber starting before the owner (passive check and retry);
  - one queue bound to several exchanges;
  - a default-key message from the old build received by the new build, and the reverse.

## Delivery plan

These follow ADR 0001's steps 0–3b, which are done. Each can be released on its own.

5. **Baseline.** Accept this ADR. Obtain the real legacy library, or its exact behaviour, and rebuild the frozen baseline and broker tests around the real topology.
6. **Core topology.** Owned exchange with configurable type; subscription list with passive checks of foreign exchanges; routing key per message; `event-type` always sent. Per-instance and shared queue modes unchanged.
7. **Legacy adapter.** Discovery by assembly, `PublishRemote` on `PubSubEvent<T>`, `PublishRemoteTo`, and App.config support for the exchange, `<subscribe>` and `<route>`. The public API test changes from "exact match" to "additions only".
   *As built (2026-09-26):*
   - **Built:**
     - `RemoteEventRegistry.Add(assembly, bus = null)`. An event without a bus belongs to every bus. With several buses, the router refuses to publish it and says why.
     - `PublishRemote` on `PubSubEvent<T>`, `PublishRemoteTo`, and `IRoutingKeyPublisher`, a separate interface so `IRabbitMQService` stays unchanged.
     - In `RabbitMQConfig`: `ExchangeType`, `Subscriptions` and `RoutingKeys` (a default key per event).
     - **Update (2026-11-06):** custom keys are no longer listed in `Subscriptions`. A subscription binds its key
       (`SubscribeOptions { Exchange, RoutingKey }`, or `[RoutingKey]` on a handler), and the last one with that key
       unbinds it, on shared queues too. Each subscribed exchange keeps its exchange-name binding. `RabbitMQConfig.RoutingKeys`
       (a default publish key per event) is removed too: a key is chosen in code, with `PublishOptions.RoutingKey`.
     - In App.config: `exchangeType`, plus `<subscriptions>` and `<routes>` inside `<bus>`.
     - The public API test now checks for "nothing removed, only listed additions, one public constructor".
   - **Deliberately left out until needed:**
     - listing event assemblies in App.config (they are registered in code, one line per assembly);
     - header matches in App.config;
     - namespace patterns and `{Field}` placeholders in `<route>` (exact event names only).
8. **Modern clients.** `AddConnection`, `Subscribe` and `WithRoutingKey` in `Messaging.Hosting`, and opt-in telemetry. Migrate this repository's net8 WPF app to plain classes as the reference, and add plain-class twins for the common events.

   *As built (2026-09-26):*
   - **Routes:** `Route<T>()` with no `.To(...)` means the application's only bus. `.WithRoutingKey(key or o => ...)` sets the key, and `PublishOptions.RoutingKey` overrides it per call.
   - **Receiving and subscriptions:** `AddMessages(assembly)` receives every `[Message]` type without a handler. `Subscribe(exchange, keys...)` adds a subscription in code. `IMessageSink` receives every message after its handlers.
   - **Telemetry:** now opt-in with `AddTelemetry()`, which the API calls. (Removed altogether on 2026-11-05, see ADR 0001.)
   - **`MessagingClient`:** runs messaging without a .NET host, for Prism/DryIoc apps.
   - **`Messaging.Prism`:** `UseEventAggregator(aggregator)` raises each message as `MessageEvent<T>`, with `PublishRemote` and `PublishRemoteTo` on it. This keeps the `IEventAggregator` style for plain classes.
   - **The net8 WPF app is the reference:** its Legacy bus stays on Prism events through the adapter, and its Modern bus uses plain `Employees.Contracts` classes through the bridge, both on one aggregator.
   - **Common-event twins:** the plain-class twins for the common events already exist in `Employees.Contracts` (`EmployeeSaved`, `EmployeeCacheRefreshed`, `EmployeeUpdated`).
9. **Desktop apps without Prism.** `IMessageSubscriber` in `Messaging.Hosting` (section 5), and a demo app that uses no Prism at all.

   *As built (2026-09-26):*
   - **`IMessageSubscriber`** is registered by `AddMessaging`, as a singleton that is also an `IMessageSink`. Its subscription list is copy-on-write, so delivering a message never takes a lock.
   - **`WpfApp.Modern`** (net8.0-windows) uses the .NET Generic Host for DI, `appsettings.json` and logging, CommunityToolkit.Mvvm for its view model, and plain `Employees.Contracts` classes on both its Legacy and its Modern bus. It builds its window, and so its subscriptions, before starting the host, so no early message is missed. The other apps can't tell it apart from the Prism apps, because the wire names are the same.
   - `WpfApp.Net8` stays as the reference for apps that keep Prism.

ADR 0001's optional step 4 (`SubscribeOnly`, `[Obsolete]` on `PublishRemote`, outbox) stays optional and comes after these.

## Open questions

Defaults are proposed; each needs confirming against the real system.

1. **Exchange types in use today.** *Default:* topic, the current declaration, with every type configurable.
2. **How an app chooses what to take from another app's exchange.** *Default:* explicit routing-key patterns in configuration.
3. **Where the common netstandard2.0 events are published.** *Default:* the publishing app's own exchange.
4. **Whether the server publishes into the owning app's exchange, or into its own.** *Proposed:* the owning app's exchange (section 6).
5. **The real legacy library and a sample App.config.** Needed for step 5. What it declares on connect, its routing keys, its bindings (per event or wildcard), and how cross-app subscriptions are configured today.

Resolved:

- **The exchange is per application** (confirmed 2026-09-26), not per events project or module. Modules in one application share its exchange and are told apart by routing key.

## Consequences

### Positive

- No per-event edits: thousands of legacy events work as they are.
- The design matches the real topology, so the compatibility tests prove something real.
- Owners control their exchange and routing keys, and subscribers choose exactly what they bind.
- Modern apps and the server stay free of Prism and Newtonsoft.
- One virtual host removes the replica's artificial Legacy/Modern split, so apps talk directly and the server publishes once.

### Negative and risks

- The baseline depends on getting the real legacy library, or an exact description of it.
- Routing-key conventions become contracts that need coordinating across teams.
- Passive checks mean a subscriber waits until the owner has declared its exchange. This is intended, but it has to be visible in logs and health checks.
- Bindings using wildcards bring in every message in a namespace, including ones an app never uses. Those are ignored on arrival, at a small bandwidth cost.
- The public API test moves from "exact match" to "additions only". Removals and changed signatures are still caught.

## Alternatives considered

- **Keep `[RemoteEvent]` on every event.** Rejected: thousands of edits to legacy code.
- **Keep one shared exchange per bus (ADR 0001).** Rejected: not how the real system works, and it loses ownership.
- **Derive the bus from the event's namespace or assembly.** Superseded: ownership is expressed by the exchange, and subscriptions express interest. The namespace remains useful as a routing-key pattern.
- **Bind one routing key per event.** Still possible, but patterns are the default. Thousands of bindings per queue slow down every connect and reconnect.
- **Let subscribers declare foreign exchanges.** Rejected: a subscriber could create or re-type another app's exchange. Only owners declare.
