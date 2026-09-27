# ADR 0001: Decouple the RabbitMQ messaging service from Prism

- **Status:** Accepted
- **Date:** 2026-09-26
- **Amended:** 2026-09-26. Legacy projects stay at their current paths, and every library that legacy apps load targets net472 explicitly (sections 1 and 7).
- **Amended by:** [ADR 0002](0002-exchange-per-owner-and-configurable-routing.md) (2026-09-26), for sections 1–3 and the step-0 test baseline: one exchange per application, configurable exchange types and routing keys, and events found by assembly. Where the two conflict, ADR 0002 wins.

## Context

`Common.RabbitMQ` connects the WPF apps (net472 and net8) and the ASP.NET API over RabbitMQ. It was built around Prism's `IEventAggregator`:

- **Contracts are Prism types.** `EmployeeSaved : RemotePubSubEvent<Employee> : PubSubEvent<Employee>`. Anything that sends or receives a message has to reference Prism.Core.
- **The transport depends on Prism.** `RabbitMQService` takes `IEventAggregator` in its constructor, and the only thing it can do with an incoming message is `GetEvent<T>().Publish()`.
- **Publishing goes through Prism.** `PublishRemote` is an extension on a Prism event, so there's no way to publish without the aggregator.
- **The API is forced into desktop semantics.** It gets synchronous `Action<T>` handlers, singleton subscribers (Prism holds weak references), no DI scope per message, no retry or dead-letter handling, and a static service locator (`RabbitMQServiceProvider`).
- **Shared multi-targeted event projects have caused assembly mismatches.** Contracts carry Prism and Newtonsoft dependencies, and non-SDK (packages.config) apps don't pick up transitive packages from project references. When a library is compiled against one version and an app ships another, the result is a `FileLoadException` or a hand-written binding redirect.

### Direction

- CRUD and event publishing move to the server. Over time, clients only **subscribe**.
- The server publishes to **both** buses: Legacy and Modern.
- The service has to work in a modular monolith (for example [CleanArchitecture](https://github.com/ncowine/CleanArchitecture), which uses a custom mediator, OpenTelemetry, correlation IDs and per-module DI).
- Legacy apps will live for a long time. Breaking them is expensive.

### Constraints

| Constraint | Detail |
|---|---|
| Legacy apps | Can be redeployed, but need little or no code change. Treat them as non-SDK: packages.config and manual binding redirects. |
| References | All apps consume the shared libraries as **project references**. |
| Versions | Shared by every app: RabbitMQ.Client **7.1.2**, Prism.Core **8.1.97**, Newtonsoft.Json **12.0.3**. |
| Broker | RabbitMQ **4.x**. |
| Strong naming | Our assemblies are **not** strong-named. |
| Central Package Management | Used for this repository's projects. Legacy apps are **not** forced onto it (see section 7). |
| Observability | Server only. It must not add dependencies or behaviour to clients. |
| Outbox | Deferred. It may be added later as an optional extension. |

## Decision

### 1. Layers

```
src/Messaging/                     the product, with no legacy concepts
  Messaging.Abstractions           net472;netstandard2.0;net8.0, 0 dependencies
  Messaging.RabbitMQ               net472;netstandard2.0;net8.0, depends on RabbitMQ.Client only
  Messaging.Hosting                net8.0, Microsoft.Extensions 8.0 (a net8 client can opt in without being forced up)

src/Contracts/
  <Module>.Contracts               netstandard2.0; references only Messaging.Abstractions (no dependencies) for [Message]

src/ (paths unchanged)             anti-corruption layer that can be deleted one day ("Legacy" solution folder)
  Common.RabbitMQ                  Prism adapter, public API unchanged
  Common.RabbitMQ.Configuration    App.config section, unchanged
  Common.Events                    Prism events, unchanged
```

The legacy projects keep their paths. Legacy apps reference them by relative `ProjectReference` path, so moving them would mean editing every legacy csproj. They are grouped in a "Legacy" solution folder instead.

**Test for the design:** when legacy is retired, deleting the legacy projects requires no change in `src/Messaging`. The core has no `#if NET472`, no Prism concepts and no Newtonsoft.

#### Messaging.Abstractions

- `IMessagePublisher`: `Task PublishAsync<T>(T message, CancellationToken)`
- `IMessageHandler<T>`: `Task Handle(T message, MessageContext context, CancellationToken)`
- `MessageContext`: message ID, correlation ID, headers, bus name, redelivery flag
- `[Message("wire-name")]`: the message's stable identity on the wire
- `IMessageSerializer`
- `IMessagingObserver`: publish, receive, handled, failed and connection-state callbacks. `OnPublishing` runs on the publishing caller's thread, so ambient context (the current `Activity`, a correlation ID) is available, and it is where headers are added. An observer that throws is logged and never affects delivery.

#### Messaging.RabbitMQ (the transport core)

- Owns connections, reconnect loops, the outgoing buffer, topology, the wire-name registry and inbound dispatch through an `IInboundDispatcher`.
- Calls `IMessagingObserver` at each stage. The default is a no-op.
- Takes no dependency on Prism, Newtonsoft, `Microsoft.Extensions.*` or OpenTelemetry.

#### Messaging.Hosting (server)

- `AddMessaging()` using the options pattern, with a **keyed** bus per name ("Legacy", "Modern").
- Routing belongs to the host, not the contract: `Route<EmployeeUpdated>().To("Legacy", "Modern")`.
- Async handlers, resolved in a **DI scope per message**. Messages are acked after the handler succeeds.
- A System.Text.Json serializer configured for output compatible with Newtonsoft (see section 4).
- Until the shared-queue work (delivery plan step 3b), the server uses the same per-instance queues and ack-always behaviour as today. Retry and dead-lettering apply only to the server's shared quorum queues. Client queues never change.
- An observer built on `ActivitySource`, `Meter` and `ILogger`, following OpenTelemetry messaging semantic conventions. It adds the `traceparent` and `correlation-id` headers. The message ID needs no header: the core already sends one in the AMQP `message-id` property on every message.
- Health checks.

#### Legacy adapter (`Common.RabbitMQ`)

- Public API unchanged: `RemotePubSubEvent<T>`, `[RemoteEvent(bus)]`, `GetEvent<T>().PublishRemote(payload, service?)`, `IRabbitMQService.Publish(Type, object)`, `RabbitMQServiceProvider`, and the `RabbitMQService` constructor signature.
- `RabbitMQService` becomes a façade over the core: a `PrismDispatcher` (`GetEvent<T>().Publish`), a Newtonsoft 12.0.3 serializer and the no-op observer.
- New, optional App.config setting: `mode="SubscribeOnly"` skips the publisher connection and buffer. It defaults to the current behaviour.
- `PublishRemote` may later get `[Obsolete]` (a compiler warning, not a break) once publishing has moved to the server.

### 2. Contracts and wire identity

- Contracts are plain classes (POCOs) with no dependencies. The **wire name is the contract**, not the CLR type. Assembly identity never goes on the wire.
- Legacy events keep their `FullName` as the wire name. A server contract that has to reach legacy clients declares the same name, for example `[Message("Common.Events.EmployeeUpdated")]`. The server never references `Common.Events`.
- New messages declare explicit wire names that don't depend on namespaces.

### 3. Compatibility contract (frozen)

- **Routing key** = wire name; **topic exchange**, durable.
- **Headers** `event-type` (wire name) and `source-id` (sender instance, used to drop the sender's own echo).
- **Body** = UTF-8 JSON as Newtonsoft 12 produces it with default settings.
- Legacy client queue: one per instance, `exclusive`, `autoDelete`, non-durable, per-consumer QoS.
- Legacy runtime behaviour: fan-out to every instance, echo drop, the fire-and-forget outgoing buffer and the current ack behaviour.
- **New headers are additive only.** Existing receivers read headers by name and ignore unknown ones. The core writes `event-type` and `source-id` itself, and an observer can't replace them.
- RabbitMQ.Client adds an `x-dotnet-pub-seq-no` header to every message when publisher confirmation tracking is on. Every build, including the baseline, has always sent it. No receiver reads it.

### 4. Serialization

- The server uses System.Text.Json with PascalCase names, case-insensitive reads, ISO 8601 dates and numeric enums.
- **Golden tests** check both directions: bytes from the server deserialize correctly under Newtonsoft 12.0.3, and the reverse.
- Measured: with the relaxed JSON encoder, System.Text.Json writes the legacy employee bodies byte for byte. The only difference found is whole decimals and doubles (`12` instead of `12.0`), which both libraries read to the same value. It is pinned in `type-coverage.stj.json`.
- The serializer is per bus. If a payload can't round-trip, a bus can switch to a Newtonsoft serializer through configuration, with no change to the core.

### 5. Observability

- It exists only in `Messaging.Hosting`, and clients get the no-op observer.
- The hook is public, so a modern client can opt in later. Nothing is imposed on it.
- A legacy client receiving a server message ignores the new headers.

### 6. RabbitMQ 4.x

- The current legacy topology is valid on 4.x: exclusive queues are exempt from the deprecation of transient non-exclusive queues, and global QoS isn't used.
- Server-side shared queues (competing consumers) are **quorum queues**, since classic mirroring was removed in 4.0.
- Quorum queues have a **default delivery limit of 20** in 4.x. The server always declares a **dead-letter queue**, so poison messages aren't silently dropped.
- **Measured on 4.3.6:** the delivery limit counts deliveries lost with a connection (a crashed consumer), **not** `basic.nack` with requeue. A handler that fails and requeues loops forever (6,000+ redeliveries in 3 seconds). So:
  - **Retries run in the process.** The handlers run up to `MaxAttempts` times, with `RetryDelay` between attempts, and the delivery is then rejected without requeue, which dead-letters it. The retry count doesn't depend on broker counting rules.
  - **The delivery limit is the crash guard.** `DeliveryLimit` (default 5) dead-letters a message that takes the process down every time it is handled, with reason `delivery_limit`.
  - **Poison and unknown messages** (a body that won't deserialize, or a message type the bus doesn't handle) are dead-lettered at once, without retries.
- The shared queue is `clientname.busname`, with `clientname.busname.dead-letter` beside it. Both are quorum queues. Dead-lettering goes through the default exchange, at least once (`x-dead-letter-strategy: at-least-once`, which requires `x-overflow: reject-publish`).
- **No echo drop on shared queues.** Which instance picks up a message is arbitrary, so dropping "own" messages would be nondeterministic. A service that subscribes to what it publishes handles it, which also lets modules of one process talk over the bus. Per-instance queues keep the legacy echo drop.
- **Shared queues give each message to one instance.** State kept in process memory (such as the API's employee cache) then diverges once there is more than one instance. Before scaling a service out, move that state out of process, or subscribe the handler per instance (a per-handler queue mode; not built).
- Retries hold the delivery, so `MaxAttempts × RetryDelay` plus handler time must stay well under the broker's `consumer_timeout` (30 minutes by default).

### 7. Dependency hygiene

- Each third-party dependency lives in exactly **one** layer: Prism and Newtonsoft in the legacy adapter, RabbitMQ.Client in the core.
- **Central Package Management:** `Directory.Packages.props` at the repository root pins every version for this repo's projects: RabbitMQ.Client 7.1.2, Prism.Core 8.1.97, Newtonsoft.Json 12.0.3.
- **Legacy apps follow their own conventions.** An SDK-style legacy app inside this tree opts out with `<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>` and keeps its own `Version` attributes (see `WpfApp.Net472`). Non-SDK (packages.config) apps are unaffected by Central Package Management.
- The legacy adapter compiles against the lowest version any legacy app uses, so apps can bind up but are never forced up.
- Newtonsoft.Json 12.0.3 carries advisory GHSA-5crp-9r3c-p9vr (deeply nested JSON can cause a stack overflow, fixed in 13.0.1). It's accepted as a legacy constraint. The server doesn't use Newtonsoft (see section 4). Apps that can move to 13.x may bind up.
- `AssemblyVersion` stays fixed per major version (for example `1.0.0.0`). The build number goes in `FileVersion` and `InformationalVersion`.
- **Every library that legacy apps load targets `net472` explicitly**, alongside `netstandard2.0` and `net8.0`. When a non-SDK net472 app consumes a `netstandard2.0`-only library, it can need the `netstandard.dll` facade files and extra binding redirects. An explicit `net472` build avoids both.
- The net472 build of the core and the adapter adds **no new packages** beyond those the legacy apps already carry. RabbitMQ.Client 7.1.2 already brings DiagnosticSource, System.Memory, Unsafe, Channels, Pipelines, RateLimiting and Bcl.AsyncInterfaces on net472, so there are no new binding redirects.

## Delivery plan

Each step can be released on its own:

0. **Safety net.** Align the repo with the legacy versions (Newtonsoft 12.0.3). Add golden tests for the wire format (headers, routing key, body bytes), plus a compatibility matrix against a real broker: current build ↔ new build, in both directions. The current build is kept as a test fixture. Broker tests skip when no broker is reachable, unless `RABBITMQ_TESTS_REQUIRED=1` is set (for CI).
1. **Extract the core** behind the unchanged `Common.RabbitMQ` façade, with **no behaviour change**. A public API test pins the façade's public surface to the baseline's. Before shipping, check on one real non-SDK legacy app that the new assemblies (`Messaging.Abstractions`, `Messaging.RabbitMQ`) are copied to its output through its project reference to `Common.RabbitMQ`. Ship this to the WPF apps first.
2. **Add the additive headers and observer hook** to the core. The no-op stays the default.
3. **Add `Messaging.Hosting`** and move the API off Prism and `Common.Events`, onto plain-class contracts and routing to both buses.
   - 3a: hosting, System.Text.Json, observability, health checks and the API migration, on the existing per-instance queues.
   - 3b: server-side shared quorum queues with retry and a dead-letter exchange and queue (section 6).
4. **Optional:** add `SubscribeOnly` to the legacy config, mark `PublishRemote` obsolete once server publishing is complete, and add an outbox extension if needed.

## Consequences

### Positive

- The server and modular-monolith modules never load Prism or Newtonsoft.
- Contracts can't cause assembly mismatches, because they have no dependencies.
- The server gets async handlers, DI scopes, retry and dead-lettering, and OpenTelemetry. Legacy pays none of those costs.
- Legacy apps redeploy with no code change.
- Legacy code has a clear exit: delete the legacy projects.

### Negative and risks

- There are more projects to maintain, and the façade has to track the core faithfully.
- Messages reaching legacy clients are tied to a legacy `FullName` wire name that can never change.
- System.Text.Json ↔ Newtonsoft parity relies on golden tests. Edge cases (dates, decimals, nulls) need coverage.
- Non-SDK apps still have to install RabbitMQ.Client's packages themselves (unchanged from today).
- Non-SDK apps now get two more assemblies (`Messaging.Abstractions`, `Messaging.RabbitMQ`) indirectly, through their reference to `Common.RabbitMQ`. MSBuild usually copies such indirect references, but this has to be checked on a real legacy app.
- Adding a public constructor overload to `RabbitMQService` would break DryIoc resolution at runtime, because DryIoc rejects multiple constructors by default. New options must come in through properties or configuration.

## Alternatives considered

- **Keep Prism as the transport contract on the server.** Rejected: it forces desktop semantics (sync, weak references, singletons, no scopes) onto the server and spreads Prism dependencies to every consumer.
- **Replace the service with MassTransit, Wolverine or NServiceBus.** Rejected for now: their wire formats (envelopes, exchange-per-type topology) are incompatible with the legacy apps without a bridge, and they add dependencies to net472 clients.
- **Share CLR types between legacy and server.** Rejected: that's the root cause of past assembly mismatches. The shared contract is the wire name.
- **Put observability in the core for every framework.** Rejected: it adds `Microsoft.Extensions.*` dependencies to non-SDK net472 apps. It's kept behind the observer hook instead.
- **Force Central Package Management onto legacy apps.** Rejected: legacy apps keep their own package conventions, and only this repository's projects opt in.
