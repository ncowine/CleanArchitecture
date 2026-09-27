# ADR 0003: The server publishes through the outbox; legacy apps stay as they are

- **Status:** Accepted
- **Date:** 2026-09-27
- **Scope:** CleanArchitecture only. Written after the code was copied from RabbitMQ-Refactor (see
  [PROVENANCE.md](../PROVENANCE.md)). Doesn't change ADR 0001 or 0002 for legacy apps.

## Context

`RabbitMQBus` sends through an in-memory buffer: `Enqueue` returns at once, and a background loop publishes and waits
for the broker's confirm. Messages still in the buffer are lost if the process stops or crashes (ADR 0001, "Known
limits").

- **Legacy apps** have always worked this way. Users have accepted it, the apps will be retired, and it isn't worth
  changing them.
- **The API** is where new publishing happens. It already has a transactional outbox (`BuildingBlocks.Outbox`): the
  business change and the event are saved in one transaction, and a background processor delivers the event. But the
  processor marks a row delivered as soon as its dispatcher returns. If the dispatcher called
  `IMessagePublisher.PublishAsync`, which only buffers, a row could be marked delivered while the message was still
  in memory, and a restart would lose it for good.
- **Modern WPF apps** only subscribe. They don't publish.

## Decision

1. **Legacy apps are unchanged.** Same adapter, same buffered publishing, same behaviour on close or crash. Nothing in
   this ADR touches `Common.RabbitMQ` or the wire format.
2. **The server publishes only through the outbox.** Code in the API doesn't call `IMessagePublisher` directly for
   events other applications rely on. It calls `IOutbox.Enqueue(event)` in the same unit of work as the change.
3. **A publish that waits for the broker.** `RabbitMQBus.PublishConfirmedAsync` sends at once and completes when the
   broker confirms. Nothing is buffered. If the bus isn't connected, or the connection drops during the publish, it
   throws `BrokerUnavailableException`. `Messaging.Hosting` exposes it as `IConfirmedMessagePublisher`, which uses the
   message's route like `IMessagePublisher`. The wire format is the same as the buffered path's; tests compare it with
   the golden files and check that the frozen original library receives it.
   RabbitMQ.Client 7 serializes publishes on a channel itself and waits for each confirm separately, so this shares the
   publisher channel with the buffered loop without a lock, and the buffered path's code is unchanged.
4. **The outbox relay.** `BuildingBlocks.Outbox.Messaging` (its own project, so the generic outbox doesn't depend on
   RabbitMQ) provides `MessagingOutboxDispatcher<TContext>`. It turns an outbox row back into its message class and
   calls `IConfirmedMessagePublisher`, passing the row's ID as the AMQP message-id. A row is marked delivered only
   after the broker has it. The correlation ID the processor restores from the row (the originating request's) is
   carried into the messaging library's `CorrelationContext` for the publish, so it becomes the message's
   `correlation-id` header.
5. **A broker outage doesn't use up attempts.** The processor retries a row 3 times, 2 seconds apart, then dead-letters
   it, so an outage of a few seconds would have parked good messages. A dispatcher now throws
   `OutboxDeliveryDeferredException` for failures that aren't the message's fault. The processor doesn't count it as
   an attempt and stops the batch there, keeping the order, and tries again on the next poll. The relay throws it for
   `BrokerUnavailableException`. Other failures, such as a message class with no route, still count and dead-letter
   as before.
6. **A clear error when a shared queue's settings change.** A queue's arguments can't change after it exists, so
   changing `DeliveryLimit` made the broker refuse the queue with a bare PRECONDITION_FAILED. The bus now logs which
   queue, and what to do: put the setting back, or delete the queue (losing its messages).

## As built: the first event (2026-09-27)

The Equipment module is the worked example, sending the same event over both channels:

- **Contract:** `Equipment.Messages` (netstandard2.0, references only `Messaging.Abstractions`) holds
  `EquipmentCreated`, `EquipmentUpdated` and `EquipmentDeleted`, wire names `Equipment.<Name>`. The same classes are the
  SignalR payloads, replacing the anonymous objects the handlers used to push.
- **Handlers** (`CreateEquipment`, `UpdateEquipment`, `DeleteEquipment`) build the event once and make two calls:
  `IEquipmentOutbox.Enqueue` (RabbitMQ, guaranteed, ~2 s after the commit) and `IRealtimeDispatch.Publish` (SignalR,
  best effort, right after the commit). Neither sends before the commit; a rolled-back change sends nothing.
- **`IEquipmentOutbox : IOutbox`** is the module's own interface, because the shared `IOutbox` is non-keyed and owned
  by Onboarding. Its implementation wraps `OutboxWriter<EquipmentDbContext>` (made public for this) so every outbox
  writes the same row format. When messaging isn't configured, `NoBrokerEquipmentOutbox` records nothing.
- **Host:** `Program.cs` calls `AddMessaging` (one bus, exchange `CleanArch`, three routes, telemetry) and
  `AddEquipmentIntegrationEvents()` only when `Messaging:Buses:Main` is configured. Development configures it. The
  messaging health check is not added to `/health`: the outbox absorbs broker outages.
- **Guard:** `TransportIndependenceTests` fails if a domain or application assembly, or the message contract,
  references RabbitMQ, the messaging engine or hosting, the outbox relay or SignalR.

Since then: the SignalR hub requires an authenticated caller (`[Authorize]`, the same schemes as the API's write
endpoints).

Not done: Onboarding publishing to RabbitMQ; the API receiving messages; per-module outbox admin endpoints (the admin
services are non-keyed like `IOutbox`); the `access_token` hook browser clients would need for Okta on the hub.

## Consequences

- An event reaches the broker if and only if its business change was committed, even across restarts and crashes.
- Delivery is at least once: a message can arrive twice (for example, confirmed by the broker just before a crash,
  before the row was marked). Receivers must tolerate duplicates. The message-id stays the same, which helps.
- A route to several buses publishes to each in turn. If a later bus fails, the retry sends to the earlier ones again,
  which is also covered by "at least once".
- No new configuration settings.

## Deliberately not done

These were considered and left out until a real need shows up (fewer moving parts, nothing for legacy apps to adopt):

- waiting for the buffer to empty on shutdown: the outbox makes it unnecessary for the server, and legacy apps don't
  need it;
- concurrent consumers, publishing several messages before waiting for confirms, a size limit on the buffer, a
  "Degraded" health status, and managing queue settings by broker policy.
